using Daydreamer.Core.Editing;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Tests;

public class ListParsingTests
{
    private readonly ScriptParser _parser = new(new CharacterRegistry([new Character { Name = "AKIRA" }]));

    [Theory]
    [InlineData("- Bring the ticket", ListKind.Bullet, "Bring the ticket")]
    [InlineData("* Bring the ticket", ListKind.Bullet, "Bring the ticket")]
    [InlineData("• Bring the ticket", ListKind.Bullet, "Bring the ticket")]
    [InlineData("1. First", ListKind.Numbered, "First")]
    [InlineData("12) Twelfth", ListKind.Numbered, "Twelfth")]
    [InlineData("[ ] Buy ink", ListKind.Checkbox, "Buy ink")]
    [InlineData("[x] Sketch page 3", ListKind.Checkbox, "Sketch page 3")]
    [InlineData("- [ ] GitHub style", ListKind.Checkbox, "GitHub style")]
    [InlineData("- AKIRA: in a list", ListKind.Bullet, "AKIRA: in a list")]
    public void Recognises_list_lines(string text, ListKind kind, string body)
    {
        var line = _parser.ParseLine(text);
        Assert.Equal(ElementType.ListItem, line.Type);
        Assert.Equal(kind, line.List);
        Assert.Equal(body, line.Body.Of(text));
    }

    [Theory]
    [InlineData("1.5 hours later.")]   // no space after the dot
    [InlineData("*italic* words")]     // emphasis, not a bullet
    [InlineData("-dash without space")]
    public void Lookalikes_are_not_lists(string text)
    {
        Assert.NotEqual(ElementType.ListItem, _parser.ParseLine(text).Type);
    }

    [Fact]
    public void Checked_state_and_indent_level_are_read()
    {
        var line = _parser.ParseLine("\t\t[x] Done");
        Assert.True(line.IsChecked);
        Assert.Equal(2, line.IndentLevel);
        Assert.Equal(1, _parser.ParseLine("    - four spaces").IndentLevel);
    }

    [Fact]
    public void Exclamation_mark_forces_a_description()
    {
        const string text = "!AKIRA: not really dialogue";
        var line = _parser.ParseLine(text);
        Assert.Equal(ElementType.Action, line.Type);
        Assert.True(line.IsForcedAction);
        Assert.Equal("AKIRA: not really dialogue", line.Body.Of(text));
    }
}

public class AlignmentAndChecklistStyleTests
{
    private readonly StyleSheet _sheet = StyleSheet.CreateDefault();
    private readonly ScriptParser _parser = new();

    [Fact]
    public void Alignment_cascades_like_other_settings()
    {
        Assert.Equal(TextAlign.Left, StyleResolver.ResolveBody(_sheet, _parser.ParseLine("Text."), ThemePalette.Light).Alignment);

        _sheet.Base.Alignment = TextAlign.Justify;
        _sheet.For(ElementType.Caption)!.Alignment = TextAlign.Center;
        Assert.Equal(TextAlign.Justify, StyleResolver.ResolveBody(_sheet, _parser.ParseLine("Text."), ThemePalette.Light).Alignment);
        Assert.Equal(TextAlign.Center, StyleResolver.ResolveBody(_sheet, _parser.ParseLine("CAPTION: Later."), ThemePalette.Light).Alignment);
    }

    [Fact]
    public void Ticked_items_are_struck_through_and_faded()
    {
        const string text = "[x] Ink page **two**";
        var segments = StyleResolver.Segment(_sheet, _parser.ParseLine(text), text.Length, ThemePalette.Dark);
        var words = segments.Where(s => s.IsCompleted).ToList();

        Assert.NotEmpty(words);
        Assert.All(words, s => Assert.Equal(ThemePalette.Dark.Muted, s.Style.Color));
        Assert.Contains(words, s => s.Range.Of(text) == "two" && s.Style.Bold);
    }
}

public class LineConverterTests
{
    private readonly ScriptParser _parser = new(new CharacterRegistry([new Character { Name = "AKIRA" }]));
    private readonly LineContext _context = new(NextPageNumber: 3, NextPanelNumber: 2, Speaker: "MIKA");

    [Theory]
    [InlineData("A quiet street.", ElementType.Caption, "CAPTION: A quiet street.")]
    [InlineData("A quiet street.", ElementType.Sfx, "SFX: A quiet street.")]
    [InlineData("A quiet street.", ElementType.Note, "// A quiet street.")]
    [InlineData("A quiet street.", ElementType.Heading, "# A quiet street.")]
    [InlineData("A quiet street.", ElementType.Page, "PAGE 3: A quiet street.")]
    [InlineData("A quiet street.", ElementType.Panel, "PANEL 2: A quiet street.")]
    [InlineData("A quiet street.", ElementType.Dialogue, "MIKA: A quiet street.")]
    [InlineData("CAPTION: Tokyo.", ElementType.Action, "Tokyo.")]
    [InlineData("SFX: BOOM", ElementType.Caption, "CAPTION: BOOM")]
    [InlineData("# The End", ElementType.Action, "The End")]
    [InlineData("PAGE 1: The Arrival", ElementType.Heading, "# The Arrival")]
    [InlineData("PANEL 4", ElementType.Action, "")]
    [InlineData("// a note", ElementType.Action, "a note")]
    [InlineData("- item", ElementType.Caption, "CAPTION: item")]
    [InlineData("\tindented", ElementType.Sfx, "\tSFX: indented")]
    public void Converts_lines_keeping_their_words(string line, ElementType target, string expected)
    {
        Assert.Equal(expected, LineConverter.ConvertTo(line, target, _parser, _context));
    }

    [Fact]
    public void Misread_dialogue_becomes_a_forced_description()
    {
        // "INT. STATION" written in caps with a colon reads as a speaker; the ! makes it a description.
        Assert.Equal("!INT. STATION: night", LineConverter.ConvertTo("INT. STATION: night", ElementType.Action, _parser, _context));
    }

    [Fact]
    public void Dialogue_to_caption_keeps_the_speaker_as_narrator()
    {
        Assert.Equal("CAPTION (AKIRA): I remember.", LineConverter.ConvertTo("AKIRA: I remember.", ElementType.Caption, _parser, _context));
    }

    [Fact]
    public void Converting_dialogue_to_dialogue_changes_the_speaker()
    {
        Assert.Equal("MIKA: Hello.", LineConverter.ConvertTo("AKIRA: Hello.", ElementType.Dialogue, _parser, _context));
    }

    [Fact]
    public void Same_type_and_blank_lines_are_left_alone()
    {
        Assert.Equal("CAPTION: x", LineConverter.ConvertTo("CAPTION: x", ElementType.Caption, _parser, _context));
        Assert.Equal("", LineConverter.ConvertTo("", ElementType.Caption, _parser, _context));
    }
}

public class LineEditingTests
{
    private readonly ScriptParser _parser = new();

    [Fact]
    public void Bullets_toggle_on_and_off()
    {
        string[] lines = ["Milk", "", "Eggs"];
        var bulleted = LineEditing.ToggleList(lines, ListKind.Bullet, _parser);
        Assert.Equal(["- Milk", "", "- Eggs"], bulleted);
        Assert.Equal(["Milk", "", "Eggs"], LineEditing.ToggleList(bulleted, ListKind.Bullet, _parser));
    }

    [Fact]
    public void Numbering_counts_per_indent_level()
    {
        string[] lines = ["One", "\tOne-a", "\tOne-b", "Two", "\tTwo-a"];
        Assert.Equal(["1. One", "\t1. One-a", "\t2. One-b", "2. Two", "\t1. Two-a"],
            LineEditing.ToggleList(lines, ListKind.Numbered, _parser));
    }

    [Fact]
    public void Switching_list_kind_replaces_the_markers()
    {
        Assert.Equal(["[ ] Milk", "[ ] Eggs"], LineEditing.ToggleList(["- Milk", "1. Eggs"], ListKind.Checkbox, _parser));
    }

    [Fact]
    public void Enter_continues_lists_and_ends_them_on_an_empty_item()
    {
        Assert.Equal("\t- ", LineEditing.OnEnter("\t- Milk", _parser).NextPrefix);
        Assert.Equal("4. ", LineEditing.OnEnter("3. Third", _parser).NextPrefix);
        Assert.Equal("[ ] ", LineEditing.OnEnter("[x] Done", _parser).NextPrefix);
        Assert.True(LineEditing.OnEnter("- ", _parser).EndList);
        Assert.False(LineEditing.OnEnter("Just text", _parser).IsList);
    }

    [Fact]
    public void Renumber_fixes_a_list_after_an_insert()
    {
        string[] lines = ["Intro", "1. A", "2. B", "2. inserted", "3. C", "", "1. Other list"];
        var changes = LineEditing.Renumber(lines, 3, _parser);
        Assert.Equal(new Dictionary<int, string> { [3] = "3. inserted", [4] = "4. C" }, changes);
    }

    [Fact]
    public void Checkboxes_toggle()
    {
        Assert.Equal("\t[x] Ink", LineEditing.ToggleCheckbox("\t[ ] Ink", _parser));
        Assert.Equal("- [ ] Ink", LineEditing.ToggleCheckbox("- [x] Ink", _parser));
        Assert.Equal("Not a task", LineEditing.ToggleCheckbox("Not a task", _parser));
    }

    [Fact]
    public void Indent_and_outdent()
    {
        Assert.Equal("\t- item", LineEditing.Indent("- item"));
        Assert.Equal("- item", LineEditing.Outdent("\t- item"));
        Assert.Equal("  six", LineEditing.Outdent("      six"));
        Assert.Equal("none", LineEditing.Outdent("none"));
    }

    [Fact]
    public void Clear_formatting_removes_emphasis_markers()
    {
        Assert.Equal("Are you getting on or what?", LineEditing.ClearEmphasis("Are you **getting on** or *what*?"));
        Assert.Equal("5 * 3 * 2", LineEditing.ClearEmphasis("5 * 3 * 2"));
    }
}
