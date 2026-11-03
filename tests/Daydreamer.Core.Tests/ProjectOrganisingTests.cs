using Daydreamer.Core.Editing;
using Daydreamer.Core.Export;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Storage;

namespace Daydreamer.Core.Tests;

public sealed class PinTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("daydreamer-pins").FullName;
    private readonly Library _library;

    public PinTests() => _library = new Library(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Pinned_documents_come_first_and_the_rest_stay_in_order()
    {
        var project = _library.CreateProject("Story");
        foreach (string title in new[] { "Chapter 1", "Chapter 2", "Chapter 10" }) _library.CreateDocument(project, title);
        project.SetPinned("Chapter 10", true);

        var titles = _library.ListDocuments(project);
        Assert.Equal(["Chapter 10", "Chapter 1", "Chapter 2"], titles.Select(d => d.Title));
        Assert.True(titles[0].IsPinned);
        Assert.False(titles[1].IsPinned);
    }

    [Fact]
    public void Pins_are_saved_with_the_project()
    {
        var project = _library.CreateProject("Story");
        project.SetPinned("Chapter 1", true);
        _library.SaveProject(project);
        Assert.True(_library.LoadProject(project.FolderPath).IsPinned("Chapter 1"));
    }

    [Fact]
    public void Unpinning_leaves_nothing_behind()
    {
        var project = new Project();
        project.SetPinned("A", true);
        project.SetPinned("A", false);
        Assert.Empty(project.Documents);
    }

    [Fact]
    public void A_renamed_document_keeps_its_pin_and_styles()
    {
        var project = new Project();
        project.SetPinned("Old", true);
        project.SettingsFor("Old").Styles.Add(new NamedStyle { Name = "Letter" });

        project.DocumentRenamed("Old", "New");

        Assert.True(project.IsPinned("New"));
        Assert.False(project.IsPinned("Old"));
        Assert.Equal("Letter", project.DocumentStyles("New").Single().Name);
    }

    [Fact]
    public void A_trashed_document_takes_its_settings_with_it()
    {
        var project = new Project();
        project.SetPinned("A", true);
        project.DocumentRemoved("A");
        Assert.Empty(project.Documents);
    }

    [Fact]
    public void Older_project_files_still_open()
    {
        var project = _library.CreateProject("Story");
        File.WriteAllText(Path.Combine(project.FolderPath, Library.ProjectFileName), """{ "Version": 2 }""");
        var loaded = _library.LoadProject(project.FolderPath);
        Assert.Empty(loaded.Documents);
        Assert.Empty(loaded.CustomStyles);
        Assert.Null(loaded.GameFolder);
    }
}

public class CustomStyleTests
{
    private static (ScriptParser Parser, NamedStyle Letter) Setup()
    {
        var letter = new NamedStyle { Name = "Old Letter", Style = new TextStyle { Italic = true, FontFamily = "Georgia" } };
        return (new ScriptParser { CustomStyles = [letter] }, letter);
    }

    [Theory]
    [InlineData("{style:Old Letter} Dear you,")]
    [InlineData("{center style:Old Letter} Dear you,")]
    [InlineData("{heading center style:Old Letter} Dear you,")]
    public void A_tag_names_the_style_and_stays_hidden(string text)
    {
        var (parser, letter) = Setup();
        var line = parser.ParseLine(text);
        Assert.Same(letter, line.CustomStyle);
        Assert.Equal("Dear you,", line.Body.Of(text).Trim());
        Assert.Equal(line.Tag, line.Markup().First());
    }

    [Theory]
    [InlineData("{} text")]
    [InlineData("{sic} text")]
    [InlineData("{ style:x} text")]
    public void Other_braces_are_just_text(string text)
    {
        var (parser, _) = Setup();
        Assert.True(parser.ParseLine(text).Tag.IsEmpty);
    }

    [Fact]
    public void A_style_goes_on_top_of_what_the_line_already_is()
    {
        var (_, letter) = Setup();
        var akira = new Character { Name = "AKIRA", Style = new TextStyle { Bold = true, FontFamily = "Arial" } };
        var parser = new ScriptParser(new CharacterRegistry([akira])) { CustomStyles = [letter] };
        var line = parser.ParseLine("{style:Old Letter} AKIRA: Dear you,");

        Assert.Equal(ElementType.Dialogue, line.Type);
        Assert.Same(akira, line.Character);
        var body = StyleResolver.ResolveBody(StyleSheet.CreateDefault(), line, ThemePalette.Light);
        Assert.True(body.Bold);    // from AKIRA
        Assert.True(body.Italic);  // from the style
        Assert.Equal("Georgia", body.FontFamily); // the style wins
    }

    [Fact]
    public void A_style_that_is_gone_leaves_the_line_looking_normal()
    {
        var parser = new ScriptParser();
        const string text = "{style:Gone} Words";
        var line = parser.ParseLine(text);
        Assert.Null(line.CustomStyle);
        Assert.Equal("Gone", line.StyleName);
        Assert.False(line.Tag.IsEmpty);
        Assert.Equal("Segoe UI", StyleResolver.ResolveBody(StyleSheet.CreateDefault(), line, ThemePalette.Light).FontFamily);
    }

    [Fact]
    public void A_documents_own_style_wins_over_the_projects()
    {
        var project = new Project();
        project.CustomStyles.Add(new NamedStyle { Name = "Letter", Style = new TextStyle { Bold = true } });
        project.SettingsFor("Chapter 1").Styles.Add(new NamedStyle { Name = "Letter", Style = new TextStyle { Italic = true } });

        var inChapter = new ScriptParser { CustomStyles = project.StylesFor("Chapter 1") }.ParseLine("{style:Letter} Hi");
        var elsewhere = new ScriptParser { CustomStyles = project.StylesFor("Chapter 2") }.ParseLine("{style:Letter} Hi");

        Assert.True(inChapter.CustomStyle!.Style.Italic);
        Assert.True(elsewhere.CustomStyle!.Style.Bold);
    }

    [Fact]
    public void Editing_a_line_keeps_its_style()
    {
        var (parser, _) = Setup();
        const string line = "{style:Old Letter} Dear you,";

        Assert.Equal("{center style:Old Letter} Dear you,", LineEditing.SetLineAlignment(line, TextAlign.Center, parser));
        Assert.Equal("{caption style:Old Letter} Dear you,", LineConverter.ConvertTo(line, ElementType.Caption, parser, new LineContext()));
        Assert.Equal(["{style:Old Letter} - Dear you,"], LineEditing.ToggleList([line], ListKind.Bullet, parser));
    }

    [Fact]
    public void Setting_and_clearing_a_style_keeps_everything_else()
    {
        var (parser, _) = Setup();
        Assert.Equal("{center style:Old Letter} Hi", LineEditing.SetLineStyle("{center} Hi", "Old Letter", parser));
        Assert.Equal("{center} Hi", LineEditing.SetLineStyle("{center style:Old Letter} Hi", null, parser));
        Assert.Equal("Hi", LineEditing.SetLineStyle("{style:Old Letter} Hi", null, parser));
    }

    [Fact]
    public void Renaming_a_style_rewrites_its_lines()
    {
        var (parser, _) = Setup();
        string text = "{style:Old Letter} One\r\nPlain\n{center style:old letter} Two\n{style:Other} Three";
        Assert.Equal("{style:Note} One\r\nPlain\n{center style:Note} Two\n{style:Other} Three", LineEditing.RenameStyle(text, "Old Letter", "Note", parser));
    }

    [Fact]
    public void Editing_a_style_tells_the_project_to_redraw()
    {
        var project = new Project();
        var style = new NamedStyle { Name = "Letter" };
        project.CustomStyles.Add(style);
        var inDocument = new NamedStyle { Name = "Own" };
        project.SettingsFor("A").Styles.Add(inDocument);

        int changes = 0;
        project.FormattingChanged += (_, _) => changes++;
        style.Style.Bold = true;
        inDocument.Style.Italic = true;
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Exports_use_the_style_and_leave_out_its_tag()
    {
        var project = new Project();
        project.CustomStyles.Add(new NamedStyle { Name = "Letter", Style = new TextStyle { Italic = true } });
        var paragraph = ExportDocument.Build("A", "{style:Letter} Dear you,", project).Paragraphs.Single();

        Assert.Equal("Dear you,", paragraph.Text);
        Assert.True(paragraph.Style.Italic);
        Assert.Equal("Letter", paragraph.CustomStyle!.Name);
    }

    [Fact]
    public void A_style_becomes_a_word_style()
    {
        var project = new Project();
        project.CustomStyles.Add(new NamedStyle { Name = "Letter", Style = new TextStyle { Italic = true } });
        var document = ExportDocument.Build("A", "{style:Letter} Dear you,", project);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(DocxExporter.Export(document, PaperSize.ForRegion())));
        using var styles = new StreamReader(zip.GetEntry("word/styles.xml")!.Open());
        Assert.Contains("Letter", styles.ReadToEnd());
    }
}
