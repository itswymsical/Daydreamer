using Daydreamer.Core.Analysis;
using Daydreamer.Core.Editing;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Tests;

public class LineTagTests
{
    private readonly ScriptParser _parser = new(new CharacterRegistry([new Character { Name = "AKIRA" }]));

    [Fact]
    public void Alignment_tag_is_read_and_the_rest_parses_normally()
    {
        const string text = "{center} AKIRA: Hi.";
        var line = _parser.ParseLine(text);
        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.Equal(TextAlign.Center, line.Alignment);
        Assert.Equal("{center} ", line.Tag.Of(text));
        Assert.Equal("AKIRA:", line.Label.Of(text));
        Assert.Equal("Hi.", line.Body.Of(text));
    }

    [Theory]
    [InlineData("{page} The Arrival", ElementType.Page, null)]
    [InlineData("{panel} Wide shot", ElementType.Panel, null)]
    [InlineData("{caption right} Tokyo, 1999.", ElementType.Caption, TextAlign.Right)]
    [InlineData("{sfx} boom", ElementType.Sfx, null)]
    [InlineData("{note} check this", ElementType.Note, null)]
    [InlineData("{heading} Chapter 2", ElementType.Heading, null)]
    [InlineData("{text justify} AKIRA: not dialogue", ElementType.Action, TextAlign.Justify)]
    public void Type_tags_set_the_line_type(string text, ElementType type, TextAlign? alignment)
    {
        var line = _parser.ParseLine(text);
        Assert.Equal(type, line.Type);
        Assert.Equal(alignment, line.Alignment);
        Assert.Equal(text[(text.IndexOf('}') + 2)..], line.Body.Of(text));
    }

    [Theory]
    [InlineData("{sic} the text")]       // not a word Daydreamer knows
    [InlineData("A {center} in the middle")]
    public void Unknown_or_misplaced_braces_are_just_text(string text)
    {
        var line = _parser.ParseLine(text);
        Assert.Equal(ElementType.Action, line.Type);
        Assert.True(line.Tag.IsEmpty);
    }

    [Fact]
    public void A_line_tag_overrides_its_style_alignment()
    {
        var sheet = StyleSheet.CreateDefault();
        sheet.Base.Alignment = TextAlign.Justify;
        var style = StyleResolver.ResolveBody(sheet, _parser.ParseLine("{right} Signed, A."), ThemePalette.Light);
        Assert.Equal(TextAlign.Right, style.Alignment);
    }

    [Theory]
    [InlineData("Hello", TextAlign.Center, "{center} Hello")]
    [InlineData("{center} Hello", null, "Hello")]
    [InlineData("{page center} The Arrival", TextAlign.Right, "{page right} The Arrival")]
    [InlineData("\t- item", TextAlign.Center, "\t{center} - item")]
    public void Line_alignment_is_stored_in_the_tag(string line, TextAlign? alignment, string expected)
    {
        Assert.Equal(expected, LineEditing.SetLineAlignment(line, alignment, _parser));
    }

    [Fact]
    public void Lists_keep_a_lines_alignment()
    {
        Assert.Equal(["{center} - Milk"], LineEditing.ToggleList(["{center} Milk"], ListKind.Bullet, _parser));
        Assert.Equal(["{center} Milk"], LineEditing.ToggleList(["{center} - Milk"], ListKind.Bullet, _parser));
    }

    [Fact]
    public void Markup_lists_what_the_editor_hides()
    {
        const string heading = "{center} # Title";
        Assert.Equal(["{center} # "], _parser.ParseLine(heading).Markup().Select(r => r.Of(heading)));

        const string emphasis = "She **really** *did*.";
        Assert.Equal(["**", "**", "*", "*"], _parser.ParseLine(emphasis).Markup().Select(r => r.Of(emphasis)));

        Assert.Equal(["!"], _parser.ParseLine("!AKIRA: no").Markup().Select(r => r.Of("!AKIRA: no")));
    }
}

public class KeywordTests
{
    private static ScriptParser ParserWith(Action<Keywords> change)
    {
        var keywords = new Keywords();
        change(keywords);
        return new ScriptParser(null, keywords);
    }

    [Theory]
    [InlineData("SCENE 3 (Home)")]
    [InlineData("Scene 4")]
    [InlineData("PANEL 2")]   // the built-in word keeps working
    [InlineData("{scene} Wide shot")]
    public void A_custom_panel_keyword_marks_panels(string text)
    {
        var parser = ParserWith(k => k.Panel = "scene");
        Assert.Equal(ElementType.Panel, parser.ParseLine(text).Type);
    }

    [Fact]
    public void Keywords_are_cleaned_and_named_for_display()
    {
        var keywords = new Keywords { Panel = " scene! ", Sfx = "" };
        Assert.Equal("SCENE", keywords.Panel);
        Assert.Equal("SFX", keywords.Sfx); // empty falls back to the default
        Assert.Equal("Scene", keywords.NameOf(ElementType.Panel));
        Assert.Equal("SFX", keywords.NameOf(ElementType.Sfx));
        Assert.Equal("Dialogue", keywords.NameOf(ElementType.Dialogue));
    }

    [Theory]
    [InlineData("SFX CLING!", ElementType.Sfx, "SFX", "CLING!")]
    [InlineData("CAPTION 1 “Everyone knows.”", ElementType.Caption, "CAPTION 1", "“Everyone knows.”")]
    [InlineData("caption: lower case with a colon", ElementType.Caption, "caption:", "lower case with a colon")]
    [InlineData("FX: thud", ElementType.Sfx, "FX:", "thud")]
    [InlineData("SOUND hmm", ElementType.Sfx, "SOUND", "hmm")]
    public void Keyword_lines_with_or_without_a_colon(string text, ElementType type, string label, string body)
    {
        var parser = ParserWith(k => k.Sfx = "SOUND");
        var line = parser.ParseLine(text);
        Assert.Equal(type, line.Type);
        Assert.Equal(label, line.Label.Of(text));
        Assert.Equal(body, line.Body.Of(text));
    }

    [Theory]
    [InlineData("Sfx are fun to draw.")]   // no colon and not in capitals: just a sentence
    [InlineData("Caption this.")]
    public void Keywords_in_sentences_stay_sentences(string text)
    {
        Assert.Equal(ElementType.Action, new ScriptParser().ParseLine(text).Type);
    }

    [Fact]
    public void Strip_marker_removes_the_page_or_panel_word()
    {
        var parser = ParserWith(k => k.Panel = "SCENE");
        Assert.Equal("The Arrival", parser.StripMarker("PAGE 3: The Arrival"));
        Assert.Equal("(Home)", parser.StripMarker("SCENE 2 (Home)"));
        Assert.Equal("Just words", parser.StripMarker("Just words"));
    }
}

public class SpeakerTests
{
    private readonly ScriptParser _parser = new(new CharacterRegistry([new Character { Name = "KAITO" }, new Character { Name = "Ema" }]));

    [Theory]
    [InlineData("KAITO It's late.", "KAITO", null, "It's late.")]
    [InlineData("KAITO (whisper) Run.", "KAITO", "whisper", "Run.")]
    [InlineData("EMA Come on.", "EMA", null, "Come on.")]          // defined as "Ema", written in capitals
    [InlineData("**EMA** Come on.", "EMA", null, "Come on.")]
    [InlineData("**EMA (low)** Not now.", "EMA", "low", "Not now.")]
    [InlineData("**EMA**: With a colon.", "EMA", null, "With a colon.")]
    public void Known_names_in_capitals_start_dialogue_without_a_colon(string text, string speaker, string? modifier, string body)
    {
        var line = _parser.ParseLine(text);
        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.Equal(speaker, line.Speaker);
        Assert.NotNull(line.Character);
        Assert.Equal(modifier, line.Modifier);
        Assert.Equal(body, line.Body.Of(text));
    }

    [Theory]
    [InlineData("Kaito walks in.")]        // not in capitals: a description
    [InlineData("KAITO")]                  // nothing said
    [InlineData("MOM Breakfast is ready!")] // MOM isn't a character yet
    [InlineData("**Tokyo**. Midday.")]
    public void Other_lines_are_not_dialogue(string text)
    {
        Assert.Equal(ElementType.Action, _parser.ParseLine(text).Type);
    }

    [Fact]
    public void Bold_labels_hide_their_markers()
    {
        const string text = "**EMA** Hi *there*.";
        var line = _parser.ParseLine(text);
        Assert.Equal("**EMA**", line.Label.Of(text));
        Assert.Equal(["**", "**"], line.LabelMarkers.Select(m => m.Of(text)));
        Assert.Equal(["**", "**", "*", "*"], line.Markup().Select(m => m.Of(text)));
    }

    [Theory]
    [InlineData("**SFX** *hmmm…*", ElementType.Sfx)]
    [InlineData("**PAGE 2**", ElementType.Page)]
    [InlineData("**CAPTION** Later.", ElementType.Caption)]
    public void Bold_keywords_still_count(string text, ElementType type)
    {
        Assert.Equal(type, new ScriptParser().ParseLine(text).Type);
    }

    [Theory]
    [InlineData("FULL PAGE: Kaito stands alone.")]
    [InlineData("DOUBLE PANEL: The city.")]
    public void Layout_notes_are_not_speakers(string text)
    {
        Assert.Equal(ElementType.Action, new ScriptParser().ParseLine(text).Type);
    }

    [Fact]
    public void Speaker_finder_suggests_names_that_start_several_lines()
    {
        string[] script =
        [
            "SHIROGANE The word of the day is Author.",
            "SHIROGANE Why'd I choose that word?",
            "**MOM** Breakfast is ready, Kaito!",
            "**MOM** Did you sleep?",
            "MOM (calling) Kaito!",
            "TEACHER You don't understand!",
            "TEACHER THOSE TWO WERE RIGHT!",
            "BOB: hi",                        // a colon makes it certain, even once
            "KAITO I said something true.",   // KAITO is already a character
            "DOUBLE SPREAD",
            "DOUBLE SPREAD",
            "FULL PAGE: The city.",
            "FULL PAGE: The sea.",
            "SFX CLING!",
            "SFX BOOM!",
            "Kaito arrives.",
        ];
        var found = SpeakerFinder.Find(script, _parser);
        Assert.Equal([("MOM", 3), ("SHIROGANE", 2), ("TEACHER", 2), ("BOB", 1)], found);
    }
}
