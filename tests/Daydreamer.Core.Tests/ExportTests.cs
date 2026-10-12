using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Daydreamer.Core.Export;
using Daydreamer.Core.Import;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Tests;

public class ExportTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Project NewProject()
    {
        var project = new Project();
        project.Characters.Add(new Character { Name = "AKIRA" });
        return project;
    }

    private static ExportDocument Build(Project project, params string[] lines) =>
        ExportDocument.Build("Test", string.Join("\n", lines), project);

    private static ExportDocument Build(params string[] lines) => Build(NewProject(), lines);

    // ── What goes on the page ──────────────────────────────────────────────

    [Fact]
    public void Hidden_markup_is_left_out_and_labels_stay()
    {
        var document = Build("{center} **Hello** *there*", "# Chapter 1", "AKIRA: It's late.", "{caption} Tokyo", "!PANEL 1 is just words");

        Assert.Equal(["Hello there", "Chapter 1", "AKIRA: It's late.", "Tokyo", "PANEL 1 is just words"],
            document.Paragraphs.Select(p => p.Text));
        var first = document.Paragraphs[0];
        Assert.Equal(TextAlign.Center, first.Style.Alignment);
        Assert.True(first.Runs[0].Style.Bold);
        Assert.True(first.Runs[^1].Style.Italic);
        Assert.Equal(ElementType.Caption, document.Paragraphs[3].Type);
    }

    [Fact]
    public void A_dialogue_line_keeps_its_name_tag_and_modifier_looks()
    {
        var line = Build("AKIRA (thought): Hmm.").Paragraphs.Single();

        Assert.Equal("AKIRA", line.Character?.Name);
        Assert.Equal("AKIRA (thought): Hmm.", line.Text);
        Assert.True(line.Runs[0].Style.Bold);           // the name tag
        Assert.False(line.Runs[0].Style.Italic);
        Assert.True(line.Runs[^1].Style.Italic);        // (thought) makes the words italic
        Assert.EndsWith("Hmm.", line.Runs[^1].Text);
    }

    [Fact]
    public void Notes_are_left_out_unless_asked_for_without_leaving_a_double_gap()
    {
        string[] script = ["PANEL 1", "", "// note to self", "", "AKIRA: Hi.", "NOTE: another"];

        var without = ExportDocument.Build("Test", string.Join("\n", script), NewProject());
        Assert.Equal([ElementType.Panel, ElementType.Blank, ElementType.Dialogue], without.Paragraphs.Select(p => p.Type));

        var with = ExportDocument.Build("Test", string.Join("\n", script), NewProject(), new ExportOptions(IncludeNotes: true));
        Assert.Equal(6, with.Paragraphs.Count);
        Assert.Equal("// note to self", with.Paragraphs[2].Text);
    }

    [Fact]
    public void List_items_get_their_marker_number_and_indent()
    {
        var document = Build("- one", "\t- two", "3. three", "[ ] task", "[x] done");

        Assert.Equal(["•", "•", "3.", "☐", "☑"], document.Paragraphs.Select(p => p.Marker));
        Assert.Equal(["one", "two", "three", "task", "done"], document.Paragraphs.Select(p => p.Text));
        Assert.Equal([0, 1, 0, 0, 0], document.Paragraphs.Select(p => p.IndentLevel));
        Assert.Equal(3, document.Paragraphs[2].Number);
        Assert.True(document.Paragraphs[4].IsChecked);
        Assert.True(document.Paragraphs[4].Runs.Single().Strikethrough); // ticked items are struck through
    }

    [Fact]
    public void Empty_lines_at_the_end_are_dropped()
    {
        Assert.Single(Build("AKIRA: Hi.", "", "  ", "").Paragraphs);
    }

    // ── Plain text ─────────────────────────────────────────────────────────

    [Fact]
    public void Text_export_is_the_words_as_the_page_shows_them()
    {
        var document = Build("{center} **Hello**", "\t- item", "[x] done", "AKIRA: Hi.", "", "SFX: BOOM");

        Assert.Equal("Hello\r\n\t• item\r\n☑ done\r\nAKIRA: Hi.\r\n\r\nSFX: BOOM\r\n", TextExporter.ToText(document));
        Assert.Equal(Encoding.UTF8.GetPreamble(), TextExporter.Export(document)[..3]); // so Notepad reads it as UTF-8
    }

    // ── Word ───────────────────────────────────────────────────────────────

    private static XDocument Part(byte[] docx, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static string? Val(XElement? element) => element?.Attribute(W + "val")?.Value;

    [Fact]
    public void Word_export_reads_back_in_with_its_headings_lists_alignment_and_look()
    {
        var project = NewProject();
        var document = Build(project, "PAGE 1", "PANEL 1", "A quiet street.", "AKIRA: It's late.",
            "- bring the ticket", "\t- check the time", "1. First", "2. Second", "[ ] task", "[x] done", "{center} The end");

        var imported = DocxImporter.Read(new MemoryStream(DocxExporter.Export(document, PaperSize.Letter)));
        var p = imported.Paragraphs;

        Assert.Equal(11, p.Count);
        Assert.Equal((1, "PAGE 1"), (p[0].Heading, p[0].Text));   // pages are Heading 1, as Styles → Importing says
        Assert.Equal((4, "PANEL 1"), (p[1].Heading, p[1].Text));  // and panels Heading 4
        Assert.Equal("A quiet street.", p[2].Text);
        Assert.Contains("It's late.", p[3].Text);
        Assert.Equal((ListKind.Bullet, 0, "bring the ticket"), (p[4].List, p[4].ListLevel, p[4].Text));
        Assert.Equal((ListKind.Bullet, 1, "check the time"), (p[5].List, p[5].ListLevel, p[5].Text));
        Assert.Equal([ListKind.Numbered, ListKind.Numbered], new[] { p[6].List, p[7].List });
        Assert.Equal((ListKind.Checkbox, false), (p[8].List, p[8].IsChecked));
        Assert.Equal((ListKind.Checkbox, true), (p[9].List, p[9].IsChecked));
        Assert.Equal((TextAlign.Center, "The end"), (p[10].Alignment, p[10].Text));

        // Importing it would give the project back its own look.
        Assert.Equal("Segoe UI", imported.FontFamily);
        Assert.Equal(12, imported.FontSize);
        Assert.Equal(1.15, imported.LineSpacing);
    }

    [Fact]
    public void Each_kind_of_line_becomes_a_Word_style_named_with_the_projects_words()
    {
        var project = NewProject();
        project.Keywords.Caption = "BOX";
        var document = Build(project, "# Title", "PAGE 1", "AKIRA: Hi.", "BOB: Who?", "BOX: Tokyo.", "SFX: BOOM", "Plain words.");

        var docx = DocxExporter.Export(document, PaperSize.Letter);
        var styles = Part(docx, "word/styles.xml").Root!.Elements(W + "style")
            .ToDictionary(s => s.Attribute(W + "styleId")!.Value, s => Val(s.Element(W + "name")));
        var used = Part(docx, "word/document.xml").Descendants(W + "pStyle").Select(Val).ToList();

        Assert.Equal(["Heading2", "Heading1", "DialogueAKIRA", "Dialogue", "BOX", "SFX", "Normal"], used);
        Assert.Equal("heading 2", styles["Heading2"]); // Word's own name, so it's a real heading
        Assert.Equal("Dialogue – AKIRA", styles["DialogueAKIRA"]);
        Assert.Equal("BOX", styles["BOX"]);
    }

    [Fact]
    public void Word_numbering_counts_the_numbers_written_in_the_script()
    {
        var docx = DocxExporter.Export(Build("1. a", "2. b", "", "1. c", "", "5. d"), PaperSize.Letter);
        var ids = Part(docx, "word/document.xml").Descendants(W + "numId").Select(Val).ToList();
        var starts = Part(docx, "word/numbering.xml").Root!.Elements(W + "num")
            .ToDictionary(n => n.Attribute(W + "numId")!.Value, n => Val(n.Descendants(W + "startOverride").FirstOrDefault()));

        Assert.Equal(ids[0], ids[1]);       // 1, 2: one list
        Assert.NotEqual(ids[1], ids[2]);    // a new list starts again at 1
        Assert.Equal("1", starts[ids[2]!]);
        Assert.Equal("5", starts[ids[3]!]); // and a list can start at any number
    }

    [Fact]
    public void Word_export_uses_the_paper_size()
    {
        var size = Part(DocxExporter.Export(Build("Hi."), PaperSize.A4), "word/document.xml").Descendants(W + "pgSz").Single();
        Assert.Equal("11906", size.Attribute(W + "w")?.Value);
        Assert.Equal("16838", size.Attribute(W + "h")?.Value);
    }

    [Fact]
    public void Characters_xml_cannot_hold_are_dropped_rather_than_breaking_the_file()
    {
        var docx = DocxExporter.Export(Build("Bell\u0007 rings 🔔"), PaperSize.Letter);
        Assert.Equal("Bell rings 🔔", string.Concat(Part(docx, "word/document.xml").Descendants(W + "t").Select(t => t.Value)));
    }
}
