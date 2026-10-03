using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Tests;

public class StyleResolverTests
{
    private static readonly ThemePalette Light = ThemePalette.Light;
    private static readonly ThemePalette Dark = ThemePalette.Dark;

    private readonly StyleSheet _sheet = StyleSheet.CreateDefault();
    private readonly Character _akira = new()
    {
        Name = "AKIRA",
        Style = new TextStyle { FontFamily = "Georgia", FontSize = 13 },
    };

    private ScriptParser Parser => new(new CharacterRegistry([_akira]));

    private List<StyledSegment> Segments(string text, ThemePalette? palette = null) =>
        StyleResolver.Segment(_sheet, Parser.ParseLine(text), text.Length, palette ?? Light);

    [Fact]
    public void Later_layers_override_earlier_ones_only_where_set()
    {
        var result = StyleResolver.Merge(
            StyleResolver.Start("#111111"),
            new TextStyle { FontFamily = "Arial", FontSize = 12 },
            new TextStyle { FontSize = 20 },
            new TextStyle { Bold = true });

        Assert.Equal("Arial", result.FontFamily);
        Assert.Equal(20, result.FontSize);
        Assert.True(result.Bold);
        Assert.Equal("#111111", result.Color); // nobody set a color, so the starting gray stays
    }

    [Fact]
    public void Character_font_applies_to_their_dialogue()
    {
        var body = StyleResolver.ResolveBody(_sheet, Parser.ParseLine("AKIRA: Hello."), Light);
        Assert.Equal("Georgia", body.FontFamily);
        Assert.Equal(13, body.FontSize);
    }

    [Fact]
    public void Sfx_is_italic_and_caption_is_bold_by_default()
    {
        Assert.True(StyleResolver.ResolveBody(_sheet, Parser.ParseLine("SFX: BOOM"), Light).Italic);
        Assert.True(StyleResolver.ResolveBody(_sheet, Parser.ParseLine("CAPTION: Later..."), Light).Bold);
    }

    [Fact]
    public void Thought_modifier_makes_dialogue_italic_but_keeps_character_font()
    {
        var body = StyleResolver.ResolveBody(_sheet, Parser.ParseLine("AKIRA (thought): Hmm."), Light);
        Assert.True(body.Italic);
        Assert.Equal("Georgia", body.FontFamily);
    }

    [Fact]
    public void Changing_a_character_style_changes_the_result_immediately()
    {
        var line = Parser.ParseLine("AKIRA: Hello.");
        _akira.Style.FontFamily = "Consolas";
        Assert.Equal("Consolas", StyleResolver.ResolveBody(_sheet, line, Light).FontFamily);
    }

    [Theory]
    [InlineData("A quiet street.")]
    [InlineData("AKIRA: Hello.")]
    [InlineData("SFX: BOOM")]
    [InlineData("CAPTION: Later.")]
    public void With_no_colors_set_text_is_the_theme_gray(string text)
    {
        var line = Parser.ParseLine(text);
        Assert.Equal(Light.Text, StyleResolver.ResolveBody(_sheet, line, Light).Color);
        Assert.Equal(Dark.Text, StyleResolver.ResolveBody(_sheet, line, Dark).Color);
    }

    [Fact]
    public void Headings_pages_panels_and_notes_use_their_own_grays()
    {
        Assert.Equal(Dark.Strong, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("PAGE 1"), Dark).Color);
        Assert.Equal(Dark.Secondary, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("PANEL 1"), Dark).Color);
        Assert.Equal(Dark.Muted, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("// note"), Dark).Color);
    }

    [Fact]
    public void A_chosen_color_wins_over_the_theme_in_both_modes()
    {
        _akira.Style.Color = "#1971C2";
        var line = Parser.ParseLine("AKIRA: Hello.");
        Assert.Equal("#1971C2", StyleResolver.ResolveBody(_sheet, line, Light).Color);
        Assert.Equal("#1971C2", StyleResolver.ResolveBody(_sheet, line, Dark).Color);
    }

    [Fact]
    public void Name_tag_is_bold_and_gray_unless_a_name_color_is_set()
    {
        var label = Assert.Single(Segments("AKIRA: Hello."), s => s.Role == SegmentRole.Label);
        Assert.True(label.Style.Bold);
        Assert.Equal(Light.Text, label.Style.Color);

        _akira.NameColor = "#1971C2";
        label = Assert.Single(Segments("AKIRA: Hello."), s => s.Role == SegmentRole.Label);
        Assert.Equal("#1971C2", label.Style.Color);
    }

    [Fact]
    public void Unknown_speaker_label_is_a_warning_but_stays_gray()
    {
        var label = Assert.Single(Segments("BOB: hi"), s => s.Role == SegmentRole.Label);
        Assert.True(label.IsWarning);
        Assert.Equal(Light.Text, label.Style.Color);
    }

    [Fact]
    public void Keyword_labels_are_faint()
    {
        var label = Assert.Single(Segments("SFX: BOOM", Dark), s => s.Role == SegmentRole.Label);
        Assert.Equal(Dark.Faint, label.Style.Color);
    }

    [Fact]
    public void Line_spacing_cascades_like_everything_else()
    {
        // New projects start at 1.15 for all text...
        Assert.Equal(1.15, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("A quiet street."), Light).LineSpacing);

        // ...a kind of line can change it...
        _sheet.For(ElementType.Dialogue)!.LineSpacing = 2;
        Assert.Equal(2, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("AKIRA: Hello."), Light).LineSpacing);
        Assert.Equal(1.15, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("SFX: BOOM"), Light).LineSpacing);

        // ...and a character can change it again, just for their lines.
        _akira.Style.LineSpacing = 1.5;
        Assert.Equal(1.5, StyleResolver.ResolveBody(_sheet, Parser.ParseLine("AKIRA: Hello."), Light).LineSpacing);
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.155, 1.16)] // rounded to hundredths
    [InlineData(0.2, null)]   // out of range means "inherit"
    [InlineData(9.0, null)]
    public void Line_spacing_only_accepts_sensible_values(double input, double? expected)
    {
        Assert.Equal(expected, new TextStyle { LineSpacing = input }.LineSpacing);
    }

    [Fact]
    public void Inline_bold_inside_italic_line_is_bold_italic()
    {
        const string text = "SFX: KRA**KOOM**";
        var emphasis = Segments(text).Last(s => s.Role == SegmentRole.Body);

        Assert.Equal("KOOM", emphasis.Range.Of(text));
        Assert.True(emphasis.Style.Bold);
        Assert.True(emphasis.Style.Italic);
    }
}
