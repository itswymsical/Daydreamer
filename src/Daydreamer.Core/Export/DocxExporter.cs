using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Export;

/// <summary>
/// Writes a Word document (.docx), which Word, Google Docs and LibreOffice all open. A .docx is a zip of XML
/// files (DocxImporter reads the same ones): the text in word/document.xml, styles in word/styles.xml and
/// lists in word/numbering.xml.
///
/// Daydreamer's way of formatting kinds of lines carries over: each kind of line becomes a named style in
/// Word (Dialogue, Caption, SFX, and one per character), so changing the Caption style in Word restyles every
/// caption. Headings, pages and panels become Word headings — the reverse of Styles → Importing — so they show
/// in Word's navigation pane and turn back into pages and panels if the file is imported again.
/// </summary>
public static class DocxExporter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Packaging = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    /// <summary>Word measures paragraph indents and margins in twentieths of a point ("twips"): 720 is half an inch.</summary>
    private const int IndentStep = 720;

    public static byte[] Export(ExportDocument document, PaperSize paper)
    {
        var styles = new WordStyles(document);
        var lists = new WordLists();
        var body = new XElement(W + "body");
        ExportParagraph? previous = null;
        foreach (var paragraph in document.Paragraphs)
        {
            if (previous?.List is null or ListKind.None) lists.StartNewList();
            body.Add(Paragraph(paragraph, isFirst: previous == null, styles, lists));
            previous = paragraph;
        }
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", Attr("w", Twips(paper.Width)), Attr("h", Twips(paper.Height))),
            new XElement(W + "pgMar",
                Attr("top", Twips(paper.Margin)), Attr("right", Twips(paper.Margin)),
                Attr("bottom", Twips(paper.Margin)), Attr("left", Twips(paper.Margin)),
                Attr("header", "720"), Attr("footer", "720"), Attr("gutter", "0"))));

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes());
            Add(zip, "_rels/.rels", Relationships(
                ("rId1", OfficeRelationship + "officeDocument", "word/document.xml"),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")));
            Add(zip, "docProps/core.xml", CoreProperties(document.Title));
            Add(zip, "word/_rels/document.xml.rels", Relationships(
                ("rId1", OfficeRelationship + "styles", "styles.xml"),
                ("rId2", OfficeRelationship + "numbering", "numbering.xml"),
                ("rId3", OfficeRelationship + "settings", "settings.xml")));
            Add(zip, "word/document.xml", new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body));
            Add(zip, "word/styles.xml", styles.ToXml());
            Add(zip, "word/numbering.xml", lists.ToXml());
            // Without this, Word opens the file in "Compatibility Mode" (as if it came from Word 2007).
            Add(zip, "word/settings.xml", new XElement(W + "settings", new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "compat",
                    new XElement(W + "compatSetting", Attr("name", "compatibilityMode"), Attr("uri", "http://schemas.microsoft.com/office/word"), Attr("val", "15")))));
        }
        return output.ToArray();
    }

    // ── Paragraphs and runs ────────────────────────────────────────────────

    private static XElement Paragraph(ExportParagraph paragraph, bool isFirst, WordStyles styles, WordLists lists)
    {
        var (styleId, styleLook) = styles.For(paragraph);
        var look = paragraph.Style;

        // Paragraph settings, in the order Word expects them. Only what differs from the style is written.
        var settings = new XElement(W + "pPr", Val("pStyle", styleId));
        int level = Math.Clamp(paragraph.IndentLevel, 0, 8);
        if (paragraph.List != ListKind.None)
            settings.Add(new XElement(W + "numPr", Val("ilvl", level), Val("numId", lists.NumberingFor(paragraph, level))));

        var spacing = new XElement(W + "spacing");
        // Like the editor, no space above the very first line.
        if (isFirst ? styleLook.SpaceBefore != 0 : look.SpaceBefore != styleLook.SpaceBefore)
            spacing.Add(Attr("before", isFirst ? "0" : Twips(look.SpaceBefore)));
        if (look.LineSpacing != styleLook.LineSpacing)
            spacing.Add(LineSpacing(look.LineSpacing));
        if (spacing.HasAttributes) settings.Add(spacing);

        if (paragraph.List == ListKind.None && level > 0)
            settings.Add(new XElement(W + "ind", Attr("left", (level * IndentStep).ToString(CultureInfo.InvariantCulture))));
        if (look.Alignment != styleLook.Alignment)
            settings.Add(Val("jc", Justification(look.Alignment)));

        var element = new XElement(W + "p", settings);
        foreach (var run in paragraph.Runs)
            element.Add(Run(run, styleLook));
        return element;
    }

    private static XElement Run(ExportRun run, ResolvedStyle paragraphLook)
    {
        var element = new XElement(W + "r");
        var settings = RunProperties(run.Style, paragraphLook, run.Strikethrough);
        if (settings.HasElements) element.Add(settings);

        // Tabs are their own element in Word; everything else is text.
        string[] pieces = XmlSafe(run.Text).Split('\t');
        for (int i = 0; i < pieces.Length; i++)
        {
            if (i > 0) element.Add(new XElement(W + "tab"));
            if (pieces[i].Length > 0)
                element.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), pieces[i]));
        }
        return element;
    }

    /// <summary>
    /// Text settings (&lt;w:rPr&gt;) for a look: all of it, or only what differs from <paramref name="inherited"/>
    /// (what the paragraph's style already says). Written in the order Word expects.
    /// </summary>
    private static XElement RunProperties(ResolvedStyle look, ResolvedStyle? inherited, bool strikethrough = false)
    {
        var settings = new XElement(W + "rPr");
        if (inherited?.FontFamily != look.FontFamily)
        {
            string font = look.FontFamily;
            settings.Add(new XElement(W + "rFonts", Attr("ascii", font), Attr("hAnsi", font), Attr("eastAsia", font), Attr("cs", font)));
        }
        if (inherited?.Bold != look.Bold) settings.Add(Toggle("b", look.Bold));
        if (inherited?.Italic != look.Italic) settings.Add(Toggle("i", look.Italic));
        if (strikethrough) settings.Add(new XElement(W + "strike"));
        if (!string.Equals(inherited?.Color, look.Color, StringComparison.OrdinalIgnoreCase))
            settings.Add(Val("color", HexColor(look.Color)));
        if (inherited?.FontSize != look.FontSize)
        {
            // Sizes are in half points (24 = 12 pt).
            string halfPoints = Math.Max(2, (int)Math.Round(look.FontSize * 2)).ToString(CultureInfo.InvariantCulture);
            settings.Add(Val("sz", halfPoints), Val("szCs", halfPoints));
        }
        if (inherited?.Underline != look.Underline) settings.Add(Val("u", look.Underline ? "single" : "none"));
        return settings;
    }

    /// <summary>Paragraph settings for a style: space above, line spacing, alignment.</summary>
    private static XElement ParagraphProperties(ResolvedStyle look, bool keepWithNext, int? outlineLevel)
    {
        var settings = new XElement(W + "pPr");
        // Headings, pages and panels stay on the same page as the line after them.
        if (keepWithNext) settings.Add(new XElement(W + "keepNext"));
        settings.Add(new XElement(W + "spacing", Attr("before", Twips(look.SpaceBefore)), Attr("after", "0"), LineSpacing(look.LineSpacing)));
        settings.Add(Val("jc", Justification(look.Alignment)));
        if (outlineLevel is int level) settings.Add(Val("outlineLvl", level));
        return settings;
    }

    /// <summary>Line spacing as a multiple of single spacing, which Word counts in 240ths ("auto" = proportional).</summary>
    private static object[] LineSpacing(double spacing) =>
        [Attr("line", ((int)Math.Round(spacing * 240)).ToString(CultureInfo.InvariantCulture)), Attr("lineRule", "auto")];

    private static string Justification(TextAlign alignment) => alignment switch
    {
        TextAlign.Center => "center",
        TextAlign.Right => "right",
        TextAlign.Justify => "both",
        _ => "left",
    };

    /// <summary>"#3C4043" → "3C4043". Anything unreadable becomes "auto" (black).</summary>
    private static string HexColor(string color)
    {
        string hex = color.Trim().TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // #AARRGGBB: the alpha can't be kept
        return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : "auto";
    }

    // ── Styles: one per kind of line ───────────────────────────────────────

    /// <summary>
    /// The Word styles a document needs, made as they're first used. Each one holds the whole look of a kind of
    /// line — the same layers the editor stacks (Default text → element → character) — and is "based on" the
    /// one below it, the way Word organises styles.
    /// </summary>
    private sealed class WordStyles
    {
        private readonly ExportDocument _document;
        private readonly ResolvedStyle _normal;
        private readonly Dictionary<string, (string Id, ResolvedStyle Look)> _made = [];
        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<XElement> _definitions = [];

        public WordStyles(ExportDocument document)
        {
            _document = document;
            // Descriptions are Word's "Normal" text; empty lines use it too.
            _normal = StyleResolver.ResolveElement(document.Project.Styles, ElementType.Action, ExportDocument.Palette);
            _ids.Add("Normal");
            _definitions.Add(Definition("Normal", "Normal", null, _normal, isDefault: true));
            _made["Normal"] = ("Normal", _normal);
        }

        public (string Id, ResolvedStyle Look) For(ExportParagraph paragraph)
        {
            // A style you made is a Word style of its own, with that name.
            if (paragraph.CustomStyle is { } custom)
                return Make("custom:" + custom.Name, () => (custom.Name, "Normal", paragraph.Style, HeadingLevel: null));

            // A defined character's lines get the character's own style, built on Dialogue.
            if (paragraph.Type == ElementType.Dialogue && paragraph.Character is { } character)
            {
                return Make("character:" + character.Name, () =>
                {
                    string dialogue = For(ElementType.Dialogue).Id;
                    var look = StyleResolver.ResolveCharacter(_document.Project.Styles, character, ExportDocument.Palette);
                    return ($"{Name(ElementType.Dialogue)} – {character.Name}", dialogue, look, HeadingLevel: null);
                });
            }
            return For(paragraph.Type);
        }

        private (string Id, ResolvedStyle Look) For(ElementType type)
        {
            if (type is ElementType.Blank or ElementType.Action) return _made["Normal"];
            return Make(type.ToString(), () =>
            {
                var look = StyleResolver.ResolveElement(_document.Project.Styles, type, ExportDocument.Palette);
                int? level = HeadingLevelOf(type);
                string name = level switch
                {
                    0 => "Title",
                    int n => $"heading {n}", // Word's own name for its Heading styles
                    null when type == ElementType.ListItem => "List Paragraph",
                    null => Name(type),
                };
                return (name, "Normal", look, level);
            });
        }

        private (string Id, ResolvedStyle Look) Make(string key, Func<(string Name, string BasedOn, ResolvedStyle Look, int? HeadingLevel)> create)
        {
            if (_made.TryGetValue(key, out var made)) return made;

            var (name, basedOn, look, headingLevel) = create();
            string id = UniqueId(name);
            _definitions.Add(Definition(id, name, basedOn, look, isDefault: false, headingLevel));
            return _made[key] = (id, look);
        }

        /// <summary>
        /// Which Word heading a kind of line becomes: the first heading Styles → Importing turns into it
        /// (Heading 1 for pages and Heading 4 for panels, unless that's been changed). Only headings, pages
        /// and panels become Word headings; other kinds of line keep a style of their own.
        /// </summary>
        private int? HeadingLevelOf(ElementType type)
        {
            if (type is not (ElementType.Heading or ElementType.Page or ElementType.Panel)) return null;
            var map = _document.Project.ImportHeadings;
            for (int level = 1; level <= 6; level++)
            {
                if (map.For(level) == type) return level;
            }
            return map.For(0) == type ? 0 : null;
        }

        /// <summary>The name people see, using the project's keywords ("Scene" if panels are called SCENE).</summary>
        private string Name(ElementType type) => _document.Project.Keywords.NameOf(type);

        /// <summary>Style ids are names without spaces or symbols ("heading 1" → "Heading1"), made unique.</summary>
        private string UniqueId(string name)
        {
            string id = new string(name.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (id.Length == 0 || !char.IsAsciiLetter(id[0])) id = "Style" + id;
            id = char.ToUpperInvariant(id[0]) + id[1..];
            string unique = id;
            for (int n = 2; !_ids.Add(unique); n++) unique = id + n.ToString(CultureInfo.InvariantCulture);
            return unique;
        }

        private static XElement Definition(string id, string name, string? basedOn, ResolvedStyle look, bool isDefault, int? headingLevel = null)
        {
            var style = new XElement(W + "style", Attr("type", "paragraph"), Attr("styleId", id));
            if (isDefault) style.Add(Attr("default", "1"));
            style.Add(Val("name", name));
            if (basedOn != null) style.Add(Val("basedOn", basedOn), Val("next", "Normal"));
            style.Add(new XElement(W + "qFormat")); // shown in Word's style gallery
            // Title has no outline level in Word; Heading 1 is level 0, Heading 2 level 1, and so on.
            style.Add(ParagraphProperties(look, keepWithNext: headingLevel != null, outlineLevel: headingLevel is > 0 ? headingLevel - 1 : null));
            style.Add(RunProperties(look, inherited: null));
            return style;
        }

        public XElement ToXml() => new(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
            // The document's defaults: Default text, with no extra space after paragraphs (Word adds some otherwise).
            new XElement(W + "docDefaults",
                new XElement(W + "rPrDefault", RunProperties(_document.BaseStyle, inherited: null)),
                new XElement(W + "pPrDefault", new XElement(W + "pPr",
                    new XElement(W + "spacing", Attr("after", "0"), LineSpacing(_document.BaseStyle.LineSpacing))))),
            _definitions);
    }

    // ── Lists ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Word lists. Each kind of list has a definition (an "abstract" numbering: what the marker looks like at
    /// each of the 9 indent levels), and each paragraph points at a numbering that uses one. Bullets and
    /// checkboxes share one numbering each. Numbered lists get a fresh numbering wherever the script's own
    /// numbers start again, so Word counts exactly the numbers written in the script.
    /// </summary>
    private sealed class WordLists
    {
        private const int Bullets = 1, Boxes = 2, TickedBoxes = 3;
        private const int NumberedDefinition = 1; // the abstract numbering for numbered lists

        private readonly List<XElement> _numbered = [];
        // Per indent level: the numbering a numbered list is using there, and the last number it reached.
        private readonly Dictionary<int, (int Id, int Last)> _running = [];
        private bool _used;

        /// <summary>Called between lists: the next numbered item starts a numbering of its own.</summary>
        public void StartNewList() => _running.Clear();

        public int NumberingFor(ExportParagraph paragraph, int level)
        {
            _used = true;
            switch (paragraph.List)
            {
                case ListKind.Bullet: return Bullets;
                case ListKind.Checkbox: return paragraph.IsChecked ? TickedBoxes : Boxes;
            }

            // Carry on counting if this is the next number; otherwise start again from the number written.
            if (_running.TryGetValue(level, out var running) && paragraph.Number == running.Last + 1)
            {
                _running[level] = (running.Id, paragraph.Number);
                return running.Id;
            }
            int id = TickedBoxes + 1 + _numbered.Count;
            _numbered.Add(new XElement(W + "num", Attr("numId", id),
                Val("abstractNumId", NumberedDefinition),
                new XElement(W + "lvlOverride", Attr("ilvl", level), Val("startOverride", Math.Max(0, paragraph.Number)))));
            _running[level] = (id, paragraph.Number);
            return id;
        }

        public XElement ToXml()
        {
            var numbering = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W));
            if (!_used) return numbering;

            // All the definitions come first, then the numberings that use them.
            numbering.Add(
                Definition(0, level => ("bullet", ExportDocument.Bullet), symbolFont: false),
                Definition(1, level => ("decimal", $"%{level + 1}."), symbolFont: false),
                Definition(2, level => ("bullet", ExportDocument.Unchecked), symbolFont: true),
                Definition(3, level => ("bullet", ExportDocument.Checked), symbolFont: true),
                Use(Bullets, 0), Use(Boxes, 2), Use(TickedBoxes, 3),
                _numbered);
            return numbering;
        }

        private static XElement Use(int id, int definition) =>
            new(W + "num", Attr("numId", id), Val("abstractNumId", definition));

        /// <summary>A list definition: for each level, its marker and indent (half an inch more per level).</summary>
        private static XElement Definition(int id, Func<int, (string Format, string Text)> marker, bool symbolFont)
        {
            var definition = new XElement(W + "abstractNum", Attr("abstractNumId", id), Val("multiLevelType", "hybridMultilevel"));
            for (int level = 0; level < 9; level++)
            {
                var (format, text) = marker(level);
                var lvl = new XElement(W + "lvl", Attr("ilvl", level),
                    Val("start", 1), Val("numFmt", format), Val("lvlText", text), Val("lvlJc", "left"),
                    new XElement(W + "pPr", new XElement(W + "ind", Attr("left", (level + 1) * IndentStep), Attr("hanging", IndentStep / 2))));
                // Checkboxes come from a font that's sure to have them.
                if (symbolFont)
                    lvl.Add(new XElement(W + "rPr", new XElement(W + "rFonts", Attr("ascii", "Segoe UI Symbol"), Attr("hAnsi", "Segoe UI Symbol"))));
                definition.Add(lvl);
            }
            return definition;
        }
    }

    // ── The package around the document ────────────────────────────────────

    private static XElement ContentTypes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        const string word = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
        XElement Override(string part, string type) => new(ns + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));
        return new XElement(ns + "Types",
            new XElement(ns + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            Override("/word/document.xml", word + "document.main+xml"),
            Override("/word/styles.xml", word + "styles+xml"),
            Override("/word/numbering.xml", word + "numbering+xml"),
            Override("/word/settings.xml", word + "settings+xml"),
            Override("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"));
    }

    /// <summary>A relationships file: how the parts of the package find each other.</summary>
    private static XElement Relationships(params (string Id, string Type, string Target)[] links) =>
        new(Packaging + "Relationships", links.Select(link => new XElement(Packaging + "Relationship",
            new XAttribute("Id", link.Id), new XAttribute("Type", link.Type), new XAttribute("Target", link.Target))));

    /// <summary>The document's title (shown in File → Info in Word) and when it was made.</summary>
    private static XElement CoreProperties(string title)
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace terms = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        return new XElement(cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", cp), new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "dcterms", terms), new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XElement(dc + "title", XmlSafe(title)),
            new XElement(terms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
    }

    private static void Add(ZipArchive zip, string path, XElement root)
    {
        using var stream = zip.CreateEntry(path).Open();
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(stream);
    }

    // ── Small helpers ──────────────────────────────────────────────────────

    /// <summary>Points → twentieths of a point.</summary>
    private static string Twips(double points) => ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

    private static XAttribute Attr(string name, object value) =>
        new(W + name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    /// <summary>The common Word shape &lt;w:name w:val="…"/&gt;.</summary>
    private static XElement Val(string name, object value) => new(W + name, Attr("val", value));

    /// <summary>On/off settings like bold: &lt;w:b/&gt; for on, &lt;w:b w:val="0"/&gt; for off.</summary>
    private static XElement Toggle(string name, bool on) => on ? new XElement(W + name) : Val(name, "0");

    /// <summary>Drops the few characters XML files can't hold (control characters), which would make Word refuse the file.</summary>
    private static string XmlSafe(string text)
    {
        if (text.All(c => XmlConvert.IsXmlChar(c))) return text;
        var safe = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (XmlConvert.IsXmlChar(text[i])) safe.Append(text[i]);
            else if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i])) safe.Append(text, i++, 2);
        }
        return safe.ToString();
    }
}
