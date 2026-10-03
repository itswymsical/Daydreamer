using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Tests;

public class ScriptParserTests
{
    private static readonly Character Akira = new() { Name = "AKIRA", Aliases = "Aki, The Kid" };
    private static readonly Character Mika = new() { Name = "Mika" };
    private readonly ScriptParser _parser = new(new CharacterRegistry([Akira, Mika]));

    [Theory]
    [InlineData("", ElementType.Blank)]
    [InlineData("    ", ElementType.Blank)]
    [InlineData("# Chapter 1", ElementType.Heading)]
    [InlineData("PAGE 1", ElementType.Page)]
    [InlineData("Page 12", ElementType.Page)]
    [InlineData("PAGES 4-5", ElementType.Page)]
    [InlineData("PAGE ONE", ElementType.Page)]
    [InlineData("PANEL 3", ElementType.Panel)]
    [InlineData("panel 2", ElementType.Panel)]
    [InlineData("CAPTION: Tokyo, 2045.", ElementType.Caption)]
    [InlineData("caption: lowercase works too", ElementType.Caption)]
    [InlineData("SFX: KRAKOOM", ElementType.Sfx)]
    [InlineData("FX: whoosh", ElementType.Sfx)]
    [InlineData("// a note", ElementType.Note)]
    [InlineData("NOTE: fix this panel", ElementType.Note)]
    [InlineData("A quiet street at dusk.", ElementType.Action)]
    public void Recognises_element_types(string line, ElementType expected)
    {
        Assert.Equal(expected, _parser.ParseLine(line).Type);
    }

    [Theory]
    [InlineData("Page after page of notes.")]   // lowercase "page" without a number is prose
    [InlineData("Panels of glass shattered.")]
    [InlineData("Time: 5pm")]                     // mixed-case unknown name isn't a speaker
    [InlineData("10:30 and the train is late.")]  // no letters in the "name"
    [InlineData("Akira looked at the sign: STOP")]
    public void Prose_is_not_mistaken_for_markup(string line)
    {
        Assert.Equal(ElementType.Action, _parser.ParseLine(line).Type);
    }

    [Fact]
    public void Defined_character_line_is_dialogue_with_ranges()
    {
        const string text = "AKIRA: I can't believe it's over.";
        var line = _parser.ParseLine(text);

        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.Same(Akira, line.Character);
        Assert.Equal("AKIRA:", line.Label.Of(text));
        Assert.Equal("I can't believe it's over.", line.Body.Of(text));
        Assert.False(line.IsUnknownSpeaker);
    }

    [Theory]
    [InlineData("akira: lowercase name")]
    [InlineData("Aki: alias")]
    [InlineData("THE KID: alias with a space")]
    [InlineData("   AKIRA: indented")]
    public void Character_lookup_is_case_insensitive_and_uses_aliases(string text)
    {
        Assert.Same(Akira, _parser.ParseLine(text).Character);
    }

    [Fact]
    public void Mixed_case_defined_name_still_counts()
    {
        Assert.Same(Mika, _parser.ParseLine("Mika: Details.").Character);
    }

    [Fact]
    public void Modifier_is_captured()
    {
        const string text = "AKIRA (whisper, off-panel): Don't move.";
        var line = _parser.ParseLine(text);

        Assert.Same(Akira, line.Character);
        Assert.Equal("whisper, off-panel", line.Modifier);
        Assert.Equal("(whisper, off-panel)", line.ModifierRange.Of(text));
        Assert.Equal("AKIRA (whisper, off-panel):", line.Label.Of(text));
        Assert.Equal("Don't move.", line.Body.Of(text));
    }

    [Fact]
    public void Unknown_all_caps_speaker_is_flagged()
    {
        var line = _parser.ParseLine("STRANGER: Found you.");

        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.Null(line.Character);
        Assert.Equal("STRANGER", line.Speaker);
        Assert.True(line.IsUnknownSpeaker);
    }

    [Fact]
    public void Caption_with_character_modifier_uses_that_character()
    {
        const string text = "CAPTION (Akira): I remember that night.";
        var line = _parser.ParseLine(text);

        Assert.Equal(ElementType.Caption, line.Type);
        Assert.Same(Akira, line.Character);
        Assert.Equal("I remember that night.", line.Body.Of(text));
    }

    [Fact]
    public void Colons_inside_dialogue_stay_in_the_body()
    {
        const string text = "MIKA: Rule one: never look back.";
        var line = _parser.ParseLine(text);

        Assert.Equal("MIKA:", line.Label.Of(text));
        Assert.Equal("Rule one: never look back.", line.Body.Of(text));
    }

    [Fact]
    public void Empty_dialogue_body_is_fine()
    {
        var line = _parser.ParseLine("AKIRA:");
        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.True(line.Body.IsEmpty);
    }

    [Fact]
    public void Inline_bold_and_italic_are_found()
    {
        const string text = "MIKA: Are you **getting on** or *what*?";
        var line = _parser.ParseLine(text);

        Assert.Equal(2, line.Inlines.Count);
        Assert.Equal(InlineKind.Bold, line.Inlines[0].Kind);
        Assert.Equal("getting on", line.Inlines[0].Content.Of(text));
        Assert.Equal("**", line.Inlines[0].OpenMarker.Of(text));
        Assert.Equal(InlineKind.Italic, line.Inlines[1].Kind);
        Assert.Equal("what", line.Inlines[1].Content.Of(text));
    }

    [Fact]
    public void Loose_asterisks_are_not_emphasis()
    {
        Assert.Empty(_parser.ParseLine("5 * 3 * 2 = 30").Inlines);
    }

    [Fact]
    public void Heading_label_is_the_hashes()
    {
        const string text = "## Scene 2";
        var line = _parser.ParseLine(text);
        Assert.Equal("## ", line.Label.Of(text));
        Assert.Equal("Scene 2", line.Body.Of(text));
    }
}
