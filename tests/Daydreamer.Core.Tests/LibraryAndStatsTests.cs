using Daydreamer.Core.Analysis;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Storage;

namespace Daydreamer.Core.Tests;

public sealed class LibraryAndStatsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daydreamer-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Library _library;

    public LibraryAndStatsTests()
    {
        _library = new Library(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Project_round_trips_characters_and_styles()
    {
        var project = _library.CreateProject("Test Series");
        project.Characters.Add(new Character { Name = "AKIRA", NameColor = "#123456", Style = new TextStyle { FontFamily = "Georgia", Italic = true, LineSpacing = 1.5 } });
        project.Styles.For(ElementType.Sfx)!.Color = "#FF0000";
        _library.SaveProject(project);

        var loaded = _library.LoadProject(project.FolderPath);

        var akira = Assert.Single(loaded.Characters);
        Assert.Equal("AKIRA", akira.Name);
        Assert.Equal("#123456", akira.NameColor);
        Assert.Equal("Georgia", akira.Style.FontFamily);
        Assert.True(akira.Style.Italic);
        Assert.Equal(1.5, akira.Style.LineSpacing);
        Assert.Equal(1.15, loaded.Styles.Base.LineSpacing);
        Assert.Equal("#FF0000", loaded.Styles.For(ElementType.Sfx)!.Color);
        Assert.True(loaded.Styles.For(ElementType.Sfx)!.Italic);
    }

    [Fact]
    public void New_projects_have_no_fixed_colors()
    {
        var project = _library.CreateProject("Gray by default");
        var styles = project.Styles;
        Assert.Null(styles.Base.Color);
        Assert.All(styles.Elements, e => Assert.Null(e.Style.Color));
        Assert.All(styles.Modifiers, m => Assert.Null(m.Style.Color));
    }

    [Fact]
    public void Version_1_projects_lose_the_old_default_colors_but_keep_custom_ones()
    {
        string folder = Path.Combine(_root, "Old Project");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Library.ProjectFileName), """
            {
              "Version": 1,
              "Characters": [ { "Name": "AKIRA", "NameColor": "#1971C2", "Style": { "Color": "#E03131" } } ],
              "Styles": {
                "Base": { "FontFamily": "Segoe UI", "FontSize": 12, "Color": "#202124" },
                "Elements": [
                  { "Type": "Page", "Style": { "Bold": true, "Color": "#6741D9" } },
                  { "Type": "Panel", "Style": { "Bold": true, "Color": "#5F6368" } },
                  { "Type": "Note", "Style": { "Italic": true, "Color": "#80868B" } },
                  { "Type": "Sfx", "Style": { "Italic": true, "Color": "#123456" } }
                ],
                "Modifiers": [ { "Keyword": "whisper", "Style": { "Color": "#80868B" } } ]
              }
            }
            """);

        var project = _library.LoadProject(folder);

        Assert.Equal(Project.CurrentVersion, project.Version);
        Assert.Null(project.Styles.Base.Color);
        Assert.Null(project.Styles.For(ElementType.Page)!.Color);
        Assert.Null(project.Styles.For(ElementType.Panel)!.Color);
        Assert.Null(project.Styles.For(ElementType.Note)!.Color);
        Assert.Null(project.Styles.Modifiers.Single().Style.Color);
        Assert.True(project.Styles.For(ElementType.Page)!.Bold); // other settings untouched

        // Colors the writer chose themselves survive.
        Assert.Equal("#123456", project.Styles.For(ElementType.Sfx)!.Color);
        Assert.Equal("#1971C2", project.Characters[0].NameColor);
        Assert.Equal("#E03131", project.Characters[0].Style.Color);
    }

    [Fact]
    public void Loaded_project_still_raises_formatting_changes()
    {
        var project = _library.CreateProject("Events");
        project.Characters.Add(new Character { Name = "MIKA" });
        _library.SaveProject(project);
        var loaded = _library.LoadProject(project.FolderPath);

        int raised = 0;
        loaded.FormattingChanged += (_, _) => raised++;
        loaded.Characters[0].Style.Bold = true;
        loaded.Styles.For(ElementType.Caption)!.FontSize = 14;
        loaded.Styles.Base.FontFamily = "Arial";

        Assert.Equal(3, raised);
    }

    [Fact]
    public void Documents_create_rename_trash()
    {
        var project = _library.CreateProject("Docs");
        var doc = _library.CreateDocument(project, "Chapter 1", "PAGE 1");
        var dupe = _library.CreateDocument(project, "Chapter 1");
        Assert.Equal("Chapter 1 (2)", dupe.Title);

        var renamed = _library.RenameDocument(doc, "Prologue");
        Assert.Equal("PAGE 1", _library.ReadDocument(renamed));

        _library.TrashDocument(renamed);
        Assert.Equal(["Chapter 1 (2)"], _library.ListDocuments(project).Select(d => d.Title));
        Assert.Single(Directory.GetFiles(Path.Combine(project.FolderPath, Library.TrashFolderName)));
    }

    [Fact]
    public void Documents_sort_naturally()
    {
        var project = _library.CreateProject("Sorting");
        foreach (var title in new[] { "Chapter 10", "Chapter 2", "Chapter 1" })
            _library.CreateDocument(project, title);

        Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 10"], _library.ListDocuments(project).Select(d => d.Title));
    }

    [Fact]
    public void Unsafe_names_are_cleaned()
    {
        Assert.Equal("What If", Library.SafeName("What? If", "x"));
        Assert.Equal("x", Library.SafeName("  ", "x"));
    }

    [Fact]
    public void Stats_count_pages_panels_and_lines_per_character()
    {
        var akira = new Character { Name = "AKIRA" };
        var parser = new ScriptParser(new CharacterRegistry([akira]));
        const string script = """
            PAGE 1
            PANEL 1
            AKIRA: one two three
            AKIRA (thought): four
            BOB: five
            // not counted at all
            PANEL 2
            SFX: BOOM
            """;

        var stats = DocumentStats.Analyze(script, parser);

        Assert.Equal(1, stats.Pages);
        Assert.Equal(2, stats.Panels);
        Assert.Equal(3, stats.DialogueLines);
        Assert.Equal(2, stats.LinesByCharacter[akira]);
        Assert.Equal(["BOB"], stats.UnknownSpeakers);
        Assert.Equal(6, stats.Words); // one two three four five BOOM
    }

    [Fact]
    public void Next_page_and_panel_numbers()
    {
        var parser = new ScriptParser();
        string[] lines = ["PAGE 1", "PANEL 1", "PANEL 2", "PAGES 2-3", "PANEL 1"];

        Assert.Equal(4, DocumentStats.NextPageNumber(lines, parser));
        Assert.Equal(2, DocumentStats.NextPanelNumber(lines, parser));
        Assert.Equal(1, DocumentStats.NextPanelNumber(["PAGE 1", "PANEL 4", "PAGE 2"], parser));
    }

    [Fact]
    public void Sample_project_is_created()
    {
        var project = SampleContent.CreateSampleProject(_library);
        Assert.Equal(2, project.Characters.Count);
        Assert.Single(_library.ListDocuments(project));
    }
}
