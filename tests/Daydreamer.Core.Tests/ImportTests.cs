using System.IO.Compression;
using System.Text;
using Daydreamer.Core.Import;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Tests;

public class ImportTests
{
    private readonly ScriptParser _parser = new(new CharacterRegistry([new Character { Name = "AKIRA" }]));
    private readonly HeadingMap _headings = new();

    // What Google Docs puts on the clipboard (trimmed a little): a bold wrapper that isn't really bold,
    // headings, styled spans, a <br> for an empty line, and lists with <p> inside each <li>.
    private const string GoogleDocsHtml = """
        <meta charset="utf-8"><b style="font-weight:normal;" id="docs-internal-guid-1a2b3c4d-7fff-0000-1111-222233334444">
        <h1 dir="ltr" style="line-height:1.38;margin-top:20pt;margin-bottom:6pt;"><span style="font-size:20pt;font-weight:400;font-style:normal;">The Arrival</span></h1>
        <h4 dir="ltr" style="line-height:1.38;margin-top:14pt;"><span style="font-size:12pt;font-weight:400;">Wide shot of the station</span></h4>
        <p dir="ltr" style="line-height:1.38;margin-top:0pt;margin-bottom:0pt;"><span style="font-weight:400;font-style:normal;">AKIRA: It&#39;s </span><span style="font-weight:700;font-style:normal;">late</span><span style="font-weight:400;font-style:italic;"> again</span><span style="font-weight:400;">.</span></p>
        <br>
        <ul style="margin-top:0;margin-bottom:0;"><li dir="ltr" style="list-style-type:disc;" aria-level="1"><p dir="ltr" style="line-height:1.38;" role="presentation"><span>Bring the ticket</span></p></li><li dir="ltr" style="list-style-type:circle;" aria-level="2"><p dir="ltr" style="line-height:1.38;" role="presentation"><span>Check the time</span></p></li></ul>
        <ol style="margin-top:0;"><li dir="ltr" style="list-style-type:decimal;" aria-level="1"><p dir="ltr" style="line-height:1.38;" role="presentation"><span>First</span></p></li><li dir="ltr" aria-level="1"><p dir="ltr" style="line-height:1.38;" role="presentation"><span>Second</span></p></li></ol>
        </b>
        """;

    [Fact]
    public void Google_Docs_paste_keeps_headings_lists_emphasis_and_spacing()
    {
        var document = HtmlImporter.Read(GoogleDocsHtml);
        string script = ScriptImporter.ToScript(document, _headings, _parser);

        Assert.Equal(string.Join("\n",
            "PAGE 1: The Arrival",
            "",
            "PANEL 1: Wide shot of the station",
            "AKIRA: It's **late** *again*.",
            "",
            "- Bring the ticket",
            "\t- Check the time",
            "1. First",
            "2. Second"), script);
        Assert.Equal(1.15, document.LineSpacing); // Docs writes 1.15 spacing as line-height:1.38
    }

    // Word's clipboard HTML: lists are <p> tags with mso-list styles and a hidden bullet/number glyph.
    private const string WordHtml = """
        <html xmlns:o="urn:schemas-microsoft-com:office:office"><body lang=EN-US><!--StartFragment-->
        <h1>Chapter One</h1>
        <p class=MsoNormal style='line-height:150%'>She looked <b>up</b>.</p>
        <p class=MsoListParagraphCxSpFirst style='text-indent:-.25in;mso-list:l0 level1 lfo1'><![if !supportLists]><span style='font-family:Symbol'><span style='mso-list:Ignore'>·<span style='font:7.0pt "Times New Roman"'>&nbsp;&nbsp;&nbsp; </span></span></span><![endif]>Milk</p>
        <p class=MsoListParagraphCxSpLast style='text-indent:-.25in;mso-list:l1 level1 lfo2'><![if !supportLists]><span style='mso-list:Ignore'>1.<span style='font:7.0pt "Times New Roman"'>&nbsp;&nbsp; </span></span><![endif]>Eggs</p>
        <p class=MsoNormal><o:p>&nbsp;</o:p></p>
        <!--EndFragment--></body></html>
        """;

    [Fact]
    public void Word_paste_reads_its_unusual_list_markup()
    {
        var document = HtmlImporter.Read(WordHtml);
        string script = ScriptImporter.ToScript(document, _headings, _parser);

        Assert.Equal("PAGE 1: Chapter One\nShe looked **up**.\n- Milk\n1. Eggs", script);
        Assert.Equal(1.5, document.LineSpacing); // Word writes 1.5 lines as 150%
    }

    [Fact]
    public void Plain_inline_html_stays_one_line()
    {
        var document = HtmlImporter.Read("<span>just a few <b>words</b></span>");
        Assert.Equal("just a few **words**", ScriptImporter.ToScript(document, _headings, _parser));
    }

    [Fact]
    public void Heading_map_decides_what_headings_become()
    {
        _headings.Heading1 = ElementType.Heading;
        _headings.Heading4 = ElementType.Caption;
        string script = ScriptImporter.ToScript(HtmlImporter.Read(GoogleDocsHtml), _headings, _parser);
        Assert.StartsWith("# The Arrival\nCAPTION: Wide shot of the station", script);
    }

    [Fact]
    public void Docx_files_are_read()
    {
        using var file = BuildDocx();
        var document = DocxImporter.Read(file);
        string script = ScriptImporter.ToScript(document, _headings, _parser);

        Assert.Equal(string.Join("\n",
            "PAGE 1: The Arrival",
            "",
            "Panel 1",
            "AKIRA: Not **again**.",
            "- Ticket",
            "1. First",
            "Line one",
            "Line two"), script);
        Assert.Equal(1.15, document.LineSpacing); // most of the text inherits Normal's 1.15 (w:line="276")
    }

    [Fact]
    public void Not_a_docx_says_so()
    {
        using var empty = new MemoryStream();
        using (new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true)) { }
        empty.Position = 0;
        Assert.Throws<InvalidDataException>(() => DocxImporter.Read(empty));
    }

    private static MemoryStream BuildDocx()
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var parts = new Dictionary<string, string>
        {
            ["word/document.xml"] = $"""
                <w:document xmlns:w="{w}"><w:body>
                  <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>The Arrival</w:t></w:r></w:p>
                  <w:p><w:pPr><w:pStyle w:val="Heading4"/></w:pPr><w:r><w:t>Panel 1</w:t></w:r></w:p>
                  <w:p><w:pPr><w:spacing w:line="360" w:lineRule="auto"/></w:pPr>
                    <w:r><w:t xml:space="preserve">AKIRA: Not </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>again</w:t></w:r><w:r><w:t>.</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Ticket</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>First</w:t></w:r></w:p>
                  <w:p><w:r><w:t>Line one</w:t><w:br/><w:t>Line two</w:t></w:r></w:p>
                </w:body></w:document>
                """,
            ["word/styles.xml"] = $"""
                <w:styles xmlns:w="{w}">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:pPr><w:spacing w:line="276" w:lineRule="auto"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading4"><w:name w:val="heading 4"/><w:basedOn w:val="Normal"/></w:style>
                </w:styles>
                """,
            ["word/numbering.xml"] = $"""
                <w:numbering xmlns:w="{w}">
                  <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="bullet"/><w:lvlText w:val="•"/></w:lvl></w:abstractNum>
                  <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                  <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
                  <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
                </w:numbering>
                """,
        };

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
