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
            "{page} The Arrival",
            "",
            "{panel} Wide shot of the station",
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

        Assert.Equal("{page} Chapter One\nShe looked **up**.\n- Milk\n1. Eggs", script);
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
        Assert.StartsWith("{heading} The Arrival\n{caption} Wide shot of the station", script);
    }

    [Fact]
    public void Docx_files_are_read()
    {
        using var file = BuildDocx();
        var document = DocxImporter.Read(file);
        string script = ScriptImporter.ToScript(document, _headings, _parser);

        Assert.Equal(string.Join("\n",
            "{page} The Arrival", // no blank line added: the document has its own paragraph spacing
            "Panel 1",
            "AKIRA: Not **again**.",
            "- Ticket",
            "1. First",
            "Line one",
            "Line two",
            "{center} Centered",
            "Typewriter"), script);
        Assert.Equal(1.15, document.LineSpacing); // most of the text inherits Normal's 1.15 (w:line="276")
    }

    [Fact]
    public void Docx_fonts_sizes_alignment_and_paragraph_spacing_are_read()
    {
        using var file = BuildDocx();
        var document = DocxImporter.Read(file);

        // Most of the text uses the theme's body font at the default 11 pt, with 12 pt between paragraphs.
        Assert.Equal("Lora", document.FontFamily);
        Assert.Equal(11, document.FontSize);
        Assert.Equal(12, document.ParagraphSpacing);
        Assert.Equal(TextAlign.Center, document.Paragraphs.Single(p => p.Text == "Centered").Alignment);
        Assert.Equal("Courier New", document.Paragraphs.Single(p => p.Text == "Typewriter").FontFamily);

        string script = ScriptImporter.ToScript(document, _headings, _parser);
        Assert.Contains("\n{center} Centered\n", script);
    }

    [Fact]
    public void Pasted_html_keeps_fonts_alignment_and_paragraph_spacing()
    {
        const string html = """
            <b style="font-weight:normal;" id="docs-internal-guid-1"><p style="text-align:center;margin-top:12pt;margin-bottom:0pt;"><span style="font-family:'Zen Kaku Gothic New',sans-serif;font-size:11pt;">Title page</span></p>
            <p style="margin-top:12pt;margin-bottom:0pt;"><span style="font-family:'Zen Kaku Gothic New',sans-serif;font-size:11pt;">KAITO It's late.</span></p>
            <p style="margin-top:12pt;margin-bottom:0pt;"><span style="font-family:Arial;font-size:14.6667px;">Short.</span></p></b>
            """;
        var document = HtmlImporter.Read(html);
        Assert.Equal("Zen Kaku Gothic New", document.FontFamily);
        Assert.Equal(11, document.FontSize);
        Assert.Equal(12, document.ParagraphSpacing);
        Assert.Equal(11, document.Paragraphs[2].FontSize); // 14.6667px is 11pt
        Assert.Equal("{center} Title page", ScriptImporter.ToScript(document, _headings, _parser).Split('\n')[0]);
    }

    [Fact]
    public void Headings_that_already_read_as_their_type_keep_their_words()
    {
        var keywords = new Keywords { Panel = "SCENE" };
        var parser = new ScriptParser(null, keywords);
        var document = new ImportedDocument();
        document.Paragraphs.Add(new ImportedParagraph("SCENE 1 (Opening)", Heading: 4));
        document.Paragraphs.Add(new ImportedParagraph("**MOM** Breakfast!"));
        Assert.Equal("SCENE 1 (Opening)\n**MOM** Breakfast!", ScriptImporter.ToScript(document, _headings, parser));
    }

    [Fact]
    public void A_documents_own_word_for_panels_is_noticed()
    {
        var document = new ImportedDocument();
        document.Paragraphs.Add(new ImportedParagraph("Chapter 1: COLLAPSE", Heading: 1));
        document.Paragraphs.Add(new ImportedParagraph("SCENE 1 (Opening)", Heading: 4));
        document.Paragraphs.Add(new ImportedParagraph("Scene 2 (Word of the Day)", Heading: 4));
        document.Paragraphs.Add(new ImportedParagraph("SCENE 3 (Home)", Heading: 4));
        Assert.Equal("SCENE", ScriptImporter.SuggestKeyword(document, _headings, ElementType.Panel));
        Assert.Null(ScriptImporter.SuggestKeyword(document, _headings, ElementType.Page)); // only one page heading
    }

    [Fact]
    public void Mostly_justified_text_only_tags_the_exceptions()
    {
        var document = new ImportedDocument();
        document.Paragraphs.Add(new ImportedParagraph("A long justified paragraph of description.", Alignment: TextAlign.Justify));
        document.Paragraphs.Add(new ImportedParagraph("Left", Alignment: TextAlign.Left));
        Assert.Equal(TextAlign.Justify, document.Alignment);
        Assert.Equal("A long justified paragraph of description.\n{left} Left",
            ScriptImporter.ToScript(document, _headings, _parser, document.Alignment));
    }

    [Fact]
    public void Embedded_fonts_are_unpacked_and_unscrambled()
    {
        // A pretend font file whose first 32 bytes Word scrambled with the key below.
        byte[] original = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        const string fontKey = "{01020304-0506-0708-090A-0B0C0D0E0F10}";
        string hex = new string(fontKey.Where(Uri.IsHexDigit).ToArray());
        byte[] key = Enumerable.Range(0, 16).Select(i => Convert.ToByte(hex.Substring(30 - i * 2, 2), 16)).ToArray();
        byte[] scrambled = original.Select((b, i) => i < 32 ? (byte)(b ^ key[i % 16]) : b).ToArray();

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] data)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(data);
            }
            Add("word/document.xml", Encoding.UTF8.GetBytes("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body/></w:document>"""));
            Add("word/fontTable.xml", Encoding.UTF8.GetBytes($"""
                <w:fonts xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:font w:name="Story Sans"><w:embedRegular w:fontKey="{fontKey}" r:id="rId1"/></w:font>
                  <w:font w:name="Arial"/>
                </w:fonts>
                """));
            Add("word/_rels/fontTable.xml.rels", Encoding.UTF8.GetBytes("""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/font" Target="fonts/font1.odttf"/>
                </Relationships>
                """));
            Add("word/fonts/font1.odttf", scrambled);
        }
        stream.Position = 0;

        var font = Assert.Single(DocxImporter.ReadEmbeddedFonts(stream));
        Assert.Equal("Story Sans", font.FamilyName);
        Assert.Equal("font1.ttf", font.FileName);
        Assert.Equal(original, font.Data);
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
                  <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>Centered</w:t></w:r></w:p>
                  <w:p><w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/></w:rPr><w:t>Typewriter</w:t></w:r></w:p>
                </w:body></w:document>
                """,
            ["word/styles.xml"] = $"""
                <w:styles xmlns:w="{w}">
                  <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:asciiTheme="minorHAnsi" w:hAnsiTheme="minorHAnsi"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
                    <w:pPrDefault><w:pPr><w:spacing w:before="240"/></w:pPr></w:pPrDefault></w:docDefaults>
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:pPr><w:spacing w:line="276" w:lineRule="auto"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading4"><w:name w:val="heading 4"/><w:basedOn w:val="Normal"/></w:style>
                </w:styles>
                """,
            ["word/theme/theme1.xml"] = """
                <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:themeElements><a:fontScheme name="Test">
                  <a:majorFont><a:latin typeface="Merriweather"/></a:majorFont><a:minorFont><a:latin typeface="Lora"/></a:minorFont>
                </a:fontScheme></a:themeElements></a:theme>
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
