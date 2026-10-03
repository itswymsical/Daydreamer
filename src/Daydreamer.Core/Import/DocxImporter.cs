using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Import;

/// <summary>A font file that came packed inside a document.</summary>
/// <param name="FamilyName">The font's name, e.g. "Zen Kaku Gothic New".</param>
/// <param name="FileName">A file name for it, e.g. "ZenKakuGothicNew-bold.ttf".</param>
public sealed record EmbeddedFont(string FamilyName, string FileName, byte[] Data);

/// <summary>
/// Reads a Word document (.docx — also what Google Docs downloads). A .docx is a zip of XML files:
/// the text is in word/document.xml, styles in word/styles.xml, list details in word/numbering.xml
/// and the theme's fonts in word/theme/theme1.xml. Only what Daydreamer can use is read: headings,
/// lists, bold, italic, alignment, fonts, sizes and spacing.
/// </summary>
public static class DocxImporter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static ImportedDocument Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var document = Load(zip, "word/document.xml")
            ?? throw new InvalidDataException("This doesn't look like a Word document (it has no word/document.xml).");
        var styles = new Styles(Load(zip, "word/styles.xml"), Load(zip, "word/theme/theme1.xml"));
        var numbering = new Numbering(Load(zip, "word/numbering.xml"));

        var result = new ImportedDocument();
        var body = document.Root?.Element(W + "body");
        if (body == null) return result;

        // Paragraphs in reading order (including ones in tables). Word stores some content twice for
        // older readers ("Fallback"); skip that copy.
        foreach (var paragraph in body.Descendants(W + "p").Where(p => !p.Ancestors(Mc + "Fallback").Any()))
            ReadParagraph(paragraph, styles, numbering, result);
        return result;
    }

    /// <summary>
    /// The font files saved inside the document. Google Docs (and Word, if asked) pack the fonts a document
    /// uses into the .docx, so it looks right on computers that don't have them installed. Word scrambles
    /// the first 32 bytes of each file with a key from word/fontTable.xml; that's undone here.
    /// </summary>
    public static List<EmbeddedFont> ReadEmbeddedFonts(Stream stream)
    {
        var fonts = new List<EmbeddedFont>();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var table = Load(zip, "word/fontTable.xml");
        var relationships = Load(zip, "word/_rels/fontTable.xml.rels");
        if (table?.Root == null || relationships?.Root == null) return fonts;

        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var targets = relationships.Root.Elements()
            .Where(e => e.Attribute("Id") != null && e.Attribute("Target") != null)
            .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        foreach (var font in table.Root.Elements(W + "font"))
        {
            if (Val(font, "name") is not { } name) continue;
            foreach (var embedded in font.Elements().Where(e => e.Name.LocalName.StartsWith("embed", StringComparison.Ordinal)))
            {
                if (embedded.Attribute(r + "id")?.Value is not { } id || !targets.TryGetValue(id, out var target)) continue;
                var entry = zip.GetEntry("word/" + target.TrimStart('/'));
                if (entry == null) continue;

                using var data = new MemoryStream();
                using (var source = entry.Open()) source.CopyTo(data);
                byte[] bytes = data.ToArray();
                Unscramble(bytes, Val(embedded, "fontKey"));
                string fileName = Path.ChangeExtension(Path.GetFileName(target), ".ttf");
                fonts.Add(new EmbeddedFont(name, fileName, bytes));
            }
        }
        return fonts;
    }

    /// <summary>Undoes Word's font scrambling: the first 32 bytes are XORed with the key, read backwards.</summary>
    private static void Unscramble(byte[] font, string? fontKey)
    {
        string hex = new string((fontKey ?? "").Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 32 || font.Length < 32) return;
        var key = new byte[16];
        for (int i = 0; i < 16; i++)
            key[i] = Convert.ToByte(hex.Substring(30 - i * 2, 2), 16);
        for (int i = 0; i < 32; i++)
            font[i] ^= key[i % 16];
    }

    private static void ReadParagraph(XElement paragraph, Styles styles, Numbering numbering, ImportedDocument result)
    {
        var properties = paragraph.Element(W + "pPr");
        string? styleId = Val(properties?.Element(W + "pStyle"));

        int? heading = styles.HeadingLevel(styleId);
        if (heading == null && int.TryParse(Val(properties?.Element(W + "outlineLvl")), out int outline) && outline < 6)
            heading = outline + 1; // outline level 0 is Heading 1

        var (list, level, isChecked) = numbering.ListOf(properties?.Element(W + "numPr") ?? styles.NumberingOf(styleId));

        // Each setting comes from the paragraph itself if it says, otherwise from its style (and so on).
        var settings = styles.ParagraphSettings(properties, styleId).ToList();
        double? lineSpacing = settings.Select(LineSpacingIn).FirstOrDefault(s => s != null);
        TextAlign? alignment = settings.Select(AlignmentIn).FirstOrDefault(a => a != null);
        double? spaceBefore = settings.Select(s => SpaceIn(s, "before")).FirstOrDefault(s => s != null);
        double? spaceAfter = settings.Select(s => SpaceIn(s, "after")).FirstOrDefault(s => s != null);
        double? paragraphSpacing = spaceBefore == null && spaceAfter == null ? null : (spaceBefore ?? 0) + (spaceAfter ?? 0);

        // Collect the runs, splitting into separate lines at line breaks.
        var lines = new List<List<TextRun>> { new() };
        foreach (var run in paragraph.Descendants(W + "r"))
        {
            // Skip runs that belong to a nested paragraph (text boxes) or to deleted (tracked) text.
            if (run.Ancestors(W + "p").First() != paragraph || run.Ancestors(W + "del").Any()) continue;

            var runSettings = styles.RunSettings(run.Element(W + "rPr"), styleId).ToList();
            bool bold = IsOn(runSettings.Select(s => s.Element(W + "b")).FirstOrDefault(b => b != null));
            bool italic = IsOn(runSettings.Select(s => s.Element(W + "i")).FirstOrDefault(i => i != null));
            string? font = runSettings.Select(styles.FontIn).FirstOrDefault(f => f != null);
            double? size = runSettings.Select(SizeIn).FirstOrDefault(s => s != null);
            TextRun Piece(string text) => new(text, bold, italic, font, size);

            foreach (var part in run.Elements())
            {
                if (part.Name.Namespace != W) continue;
                switch (part.Name.LocalName)
                {
                    case "t": lines[^1].Add(Piece(part.Value)); break;
                    case "tab": lines[^1].Add(Piece("\t")); break;
                    case "noBreakHyphen": lines[^1].Add(Piece("-")); break;
                    case "cr": lines.Add([]); break;
                    case "br" when Val(part, "type") is null or "textWrapping": lines.Add([]); break;
                }
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string text = RunText.Compose(lines[i], plain: heading != null, collapseWhitespace: false);
            var (font, size) = RunText.MostUsedLook(lines[i]);
            var imported = new ImportedParagraph(text, LineSpacing: lineSpacing, Alignment: alignment,
                FontFamily: font, FontSize: size, ParagraphSpacing: paragraphSpacing);
            // Continuation lines (after a line break) are plain text.
            if (i == 0)
                imported = imported with { Heading = heading, List = list, ListLevel = level, IsChecked = isChecked };
            if (imported.Heading != null && text.Length == 0) continue; // empty headings carry nothing
            result.Paragraphs.Add(imported);
        }
    }

    /// <summary>
    /// Word line spacing: "auto" counts in 240ths of a line (240 = single, 276 = 1.15, 360 = 1.5);
    /// "exact"/"atLeast" are in 20ths of a point, roughly converted assuming 11 pt text.
    /// </summary>
    internal static double? LineSpacingIn(XElement paragraphProperties)
    {
        var spacing = paragraphProperties.Element(W + "spacing");
        if (!double.TryParse(Val(spacing, "line"), NumberStyles.Float, CultureInfo.InvariantCulture, out double line) || line <= 0)
            return null;

        double value = Val(spacing, "lineRule") is null or "auto" ? line / 240 : line / 20 / (11 * 1.15);
        return value is >= 0.5 and <= 4 ? Math.Round(value, 2) : null;
    }

    /// <summary>Space before or after a paragraph, stored in 20ths of a point (240 = 12 pt).</summary>
    private static double? SpaceIn(XElement paragraphProperties, string side)
    {
        string? value = Val(paragraphProperties.Element(W + "spacing"), side);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double twentieths) && twentieths >= 0
            ? Math.Round(twentieths / 20, 1)
            : null;
    }

    private static TextAlign? AlignmentIn(XElement paragraphProperties) => Val(paragraphProperties.Element(W + "jc")) switch
    {
        "left" or "start" => TextAlign.Left,
        "center" => TextAlign.Center,
        "right" or "end" => TextAlign.Right,
        "both" or "distribute" => TextAlign.Justify,
        _ => null,
    };

    /// <summary>Font sizes are stored in half points (24 = 12 pt).</summary>
    private static double? SizeIn(XElement runProperties) =>
        double.TryParse(Val(runProperties.Element(W + "sz")), NumberStyles.Float, CultureInfo.InvariantCulture, out double halfPoints) && halfPoints > 0
            ? halfPoints / 2
            : null;

    private static bool IsOn(XElement? toggle) =>
        toggle != null && Val(toggle) is null or "1" or "true" or "on";

    private static string? Val(XElement? element, string attribute = "val") => element?.Attribute(W + attribute)?.Value;

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry == null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>
    /// The document's styles: which paragraphs are headings, and where a paragraph or piece of text gets
    /// its settings from when it doesn't set them itself (its style, the styles that one is based on, and
    /// finally the document's defaults).
    /// </summary>
    private sealed class Styles
    {
        private readonly Dictionary<string, XElement> _paragraphStyles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XElement> _characterStyles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? _defaultId;
        private readonly XElement? _defaultParagraph;
        private readonly XElement? _defaultRun;
        private readonly string? _headingsFont;
        private readonly string? _bodyFont;

        public Styles(XDocument? styles, XDocument? theme)
        {
            // The theme names two fonts: one for headings ("major") and one for body text ("minor").
            var fontScheme = theme?.Root?.Descendants(A + "fontScheme").FirstOrDefault();
            _headingsFont = NonEmpty(fontScheme?.Element(A + "majorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value);
            _bodyFont = NonEmpty(fontScheme?.Element(A + "minorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value);

            if (styles?.Root == null) return;
            foreach (var style in styles.Root.Elements(W + "style"))
            {
                if (style.Attribute(W + "styleId")?.Value is not { } id) continue;
                switch (style.Attribute(W + "type")?.Value)
                {
                    case "paragraph":
                        _paragraphStyles[id] = style;
                        if (style.Attribute(W + "default")?.Value is "1" or "true") _defaultId = id;
                        break;
                    case "character":
                        _characterStyles[id] = style;
                        break;
                }
            }
            var defaults = styles.Root.Element(W + "docDefaults");
            _defaultParagraph = defaults?.Element(W + "pPrDefault")?.Element(W + "pPr");
            _defaultRun = defaults?.Element(W + "rPrDefault")?.Element(W + "rPr");
        }

        public int? HeadingLevel(string? id)
        {
            foreach (var style in Chain(_paragraphStyles, id))
            {
                string name = Val(style.Element(W + "name"))?.ToLowerInvariant() ?? "";
                if (name == "title") return 0;
                if (name.StartsWith("heading ") && int.TryParse(name["heading ".Length..], out int level) && level is >= 1 and <= 6)
                    return level;
                if (int.TryParse(Val(style.Element(W + "pPr")?.Element(W + "outlineLvl")), out int outline) && outline < 6)
                    return outline + 1;
            }
            return null;
        }

        public XElement? NumberingOf(string? id) =>
            Chain(_paragraphStyles, id).Select(s => s.Element(W + "pPr")?.Element(W + "numPr")).FirstOrDefault(n => n != null);

        /// <summary>Paragraph settings (&lt;w:pPr&gt;) to look through, most specific first.</summary>
        public IEnumerable<XElement> ParagraphSettings(XElement? own, string? styleId)
        {
            if (own != null) yield return own;
            foreach (var style in Chain(_paragraphStyles, styleId ?? _defaultId))
            {
                if (style.Element(W + "pPr") is { } settings) yield return settings;
            }
            if (_defaultParagraph != null) yield return _defaultParagraph;
        }

        /// <summary>Text settings (&lt;w:rPr&gt;) to look through, most specific first.</summary>
        public IEnumerable<XElement> RunSettings(XElement? own, string? paragraphStyleId)
        {
            if (own != null) yield return own;
            foreach (var style in Chain(_characterStyles, Val(own?.Element(W + "rStyle"))))
            {
                if (style.Element(W + "rPr") is { } settings) yield return settings;
            }
            foreach (var style in Chain(_paragraphStyles, paragraphStyleId ?? _defaultId))
            {
                if (style.Element(W + "rPr") is { } settings) yield return settings;
            }
            if (_defaultRun != null) yield return _defaultRun;
        }

        /// <summary>The font named in some text settings — directly, or as "the theme's heading/body font".</summary>
        public string? FontIn(XElement runProperties)
        {
            var fonts = runProperties.Element(W + "rFonts");
            if (fonts == null) return null;
            if (NonEmpty(Val(fonts, "ascii") ?? Val(fonts, "hAnsi")) is { } font) return font;

            string? themeFont = Val(fonts, "asciiTheme") ?? Val(fonts, "hAnsiTheme");
            if (themeFont == null) return null;
            return themeFont.StartsWith("major", StringComparison.Ordinal) ? _headingsFont : _bodyFont;
        }

        /// <summary>A style, then the style it's based on, and so on.</summary>
        private static IEnumerable<XElement> Chain(Dictionary<string, XElement> styles, string? id)
        {
            for (int depth = 0; id != null && depth < 12 && styles.TryGetValue(id, out var style); depth++)
            {
                yield return style;
                id = Val(style.Element(W + "basedOn"));
            }
        }

        private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>List definitions: whether a list level is bullets, numbers or checkboxes.</summary>
    private sealed class Numbering
    {
        private readonly Dictionary<string, XElement> _abstracts = [];
        private readonly Dictionary<string, string> _abstractIdByNumId = [];

        public Numbering(XDocument? numbering)
        {
            if (numbering?.Root == null) return;
            foreach (var definition in numbering.Root.Elements(W + "abstractNum"))
            {
                if (definition.Attribute(W + "abstractNumId")?.Value is { } id) _abstracts[id] = definition;
            }
            foreach (var num in numbering.Root.Elements(W + "num"))
            {
                if (num.Attribute(W + "numId")?.Value is { } numId && Val(num.Element(W + "abstractNumId")) is { } abstractId)
                    _abstractIdByNumId[numId] = abstractId;
            }
        }

        public (ListKind Kind, int Level, bool IsChecked) ListOf(XElement? numberingProperties)
        {
            string? numId = Val(numberingProperties?.Element(W + "numId"));
            if (numId is null or "0") return (ListKind.None, 0, false);
            int level = int.TryParse(Val(numberingProperties?.Element(W + "ilvl")), out int l) ? l : 0;

            XElement? levelDefinition = null;
            if (_abstractIdByNumId.TryGetValue(numId, out var abstractId) && _abstracts.TryGetValue(abstractId, out var definition))
                levelDefinition = definition.Elements(W + "lvl").FirstOrDefault(x => x.Attribute(W + "ilvl")?.Value == level.ToString(CultureInfo.InvariantCulture));

            string format = Val(levelDefinition?.Element(W + "numFmt")) ?? "bullet";
            string symbol = Val(levelDefinition?.Element(W + "lvlText")) ?? "";
            if (symbol.IndexOfAny(['☐', '❏', '□']) >= 0) return (ListKind.Checkbox, level, false);
            if (symbol.IndexOfAny(['☑', '☒', '✓', '✔']) >= 0) return (ListKind.Checkbox, level, true);
            return (format is "bullet" or "none" ? ListKind.Bullet : ListKind.Numbered, level, false);
        }
    }
}
