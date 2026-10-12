using System.Windows.Media;
using Daydreamer.App;
using Daydreamer.App.Ambient;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

// The WPF helpers keep simple caches, so run these one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Daydreamer.App.Tests;

public class AmbientTests
{
    /// <summary>The orbs add light because their pixels keep their color but have zero alpha (see OrbSprites.AsLight).</summary>
    [Fact]
    public void Orb_images_are_pure_light()
    {
        var sprite = new OrbSprites(AmbientLook.Blue).For(0.5);
        int stride = sprite.PixelWidth * 4;
        var pixels = new byte[stride * sprite.PixelHeight];
        sprite.CopyPixels(pixels, stride, 0);

        bool anyAlpha = false, anyColor = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            anyColor |= pixels[i] > 0 || pixels[i + 1] > 0 || pixels[i + 2] > 0;
            anyAlpha |= pixels[i + 3] > 0;
        }
        Assert.True(anyColor);
        Assert.False(anyAlpha);
    }
}

public class LineSpacingTests
{
    [Fact]
    public void Single_spacing_uses_the_font_as_is()
    {
        Assert.Equal("Georgia", WpfStyles.Family("Georgia", 1.0).Source);
    }

    [Fact]
    public void Spaced_fonts_report_taller_lines_and_keep_the_text_centred()
    {
        var plain = new FontFamily("Georgia");
        var spaced = WpfStyles.Family("Georgia", 1.5);

        Assert.Equal(plain.LineSpacing * 1.5, spaced.LineSpacing, precision: 6);
        Assert.Equal(plain.Baseline + plain.LineSpacing * 0.25, spaced.Baseline, precision: 6);
    }
}

public class ReadableColorTests
{
    [Fact]
    public void A_dark_color_is_lightened_on_the_dark_page()
    {
        var original = Color.FromRgb(0x1C, 0x2A, 0x5A);
        var shown = WpfStyles.ReadableBrush("#1C2A5A", ThemePalette.Dark.Paper).Color;
        Assert.True(shown.R + shown.G + shown.B > original.R + original.G + original.B);
    }

    [Fact]
    public void A_color_that_is_already_readable_is_left_alone()
    {
        Assert.Equal(Color.FromRgb(0x19, 0x71, 0xC2), WpfStyles.ReadableBrush("#1971C2", ThemePalette.Light.Paper).Color);
    }

    [Fact]
    public void The_themes_faint_grays_stay_faint()
    {
        // Labels and ** markers are low-contrast on purpose; only colors someone picked get adjusted.
        var ghost = WpfStyles.TextBrush(ThemePalette.Light.Ghost, ThemePalette.Light).Color;
        Assert.Equal(WpfStyles.TryParseColor(ThemePalette.Light.Ghost), ghost);
    }
}

public class FormatTargetTests
{
    private static (FormatTarget Target, Character Akira) AkiraTarget()
    {
        var sheet = StyleSheet.CreateDefault();
        var akira = new Character { Name = "AKIRA" };
        var target = new FormatTarget();
        target.Set("AKIRA", "", akira.Style, palette => StyleResolver.ResolveCharacter(sheet, akira, palette), akira);
        return (target, akira);
    }

    [Fact]
    public void Shows_the_inherited_look_before_anything_is_set()
    {
        var (target, _) = AkiraTarget();
        Assert.Equal("Segoe UI", target.FontFamily);
        Assert.Equal("12", target.FontSize);
        Assert.Equal(1.15, target.LineSpacing);
        Assert.Null(target.Color); // default gray
    }

    [Fact]
    public void Choosing_a_font_sets_it_for_the_character()
    {
        var (target, akira) = AkiraTarget();
        target.FontFamily = "Georgia";
        Assert.Equal("Georgia", akira.Style.FontFamily);
    }

    [Fact]
    public void Re_picking_the_inherited_font_does_not_pin_it()
    {
        var (target, akira) = AkiraTarget();
        target.FontFamily = "Segoe UI";
        Assert.Null(akira.Style.FontFamily); // still follows Default text
    }

    [Fact]
    public void Line_spacing_applies_to_the_character_and_ignores_nonsense()
    {
        var (target, akira) = AkiraTarget();
        target.LineSpacing = 2;
        Assert.Equal(2, akira.Style.LineSpacing);

        target.LineSpacing = 12; // out of range: ignored rather than silently reset
        Assert.Equal(2, akira.Style.LineSpacing);
    }

    [Theory]
    [InlineData("14", 14.0)]
    [InlineData("10.5", 10.5)]
    [InlineData("3", null)]
    [InlineData("abc", null)]
    public void Size_box_only_accepts_sensible_sizes(string typed, double? expected)
    {
        var (target, akira) = AkiraTarget();
        target.FontSize = typed;
        Assert.Equal(expected, akira.Style.FontSize);
    }

    [Fact]
    public void Theme_setting_parses()
    {
        Assert.Equal(AppTheme.Dark, ThemeManager.FromSetting("Dark"));
        Assert.Equal(AppTheme.Light, ThemeManager.FromSetting("light"));
    }
}

public class MusicPlayerTests
{
    [Theory]
    [InlineData(@"C:\Music\Theme.mp3")]
    [InlineData(@"C:\Music\Track #1.mp3")]
    [InlineData(@"C:\Music\100% Rain.mp3")]
    [InlineData(@"C:\Music\Scene 7 (ver. 2) a%20b.mp3")]
    [InlineData(@"E:\OneDrive\Documents\@Applications\夜の街.flac")]
    public void Song_files_are_found_whatever_their_names(string path)
    {
        Assert.Equal(path, Daydreamer.App.Music.WpfAudioPlayer.ToUri(path).LocalPath);
    }
}

public class WindowsSpellingTests
{
    // These use the real Windows spell checker, with its US English dictionary.
    private static readonly Daydreamer.App.Editor.WindowsSpellChecker? English = Daydreamer.App.Editor.WindowsSpellChecker.TryCreate("en-US");

    [Fact]
    public void Finds_a_misspelling_and_suggests_the_word()
    {
        Assert.NotNull(English);
        var issue = Assert.Single(English.Check("I can't beleive it."));
        Assert.Equal(new Daydreamer.Core.Parsing.TextRange(8, 7), issue.Range);
        Assert.Contains("believe", English.Suggest("beleive"));
    }

    [Fact]
    public void Spots_a_repeated_word()
    {
        Assert.NotNull(English);
        var issue = Assert.Single(English.Check("It was the the last train."));
        Assert.Equal(Daydreamer.Core.Proofing.SpellingIssueKind.RepeatedWord, issue.Kind);
    }

    [Fact]
    public void Blanked_out_parts_of_a_line_do_not_confuse_it()
    {
        Assert.NotNull(English);
        // What the script checker sends for "AKIRA (whisper): Don't move.": the name blanked out with spaces.
        Assert.Empty(English.Check("                 Don't move."));
    }
}
