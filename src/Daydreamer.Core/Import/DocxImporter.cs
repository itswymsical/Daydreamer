using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Import;

/// <summary>
/// Reads a Word document (.docx — also what Google Docs downloads). A .docx is a zip of XML files;
/// the text is in word/document.xml, heading and spacing settings in word/styles.xml, and list
/// details in word/numbering.xml. Only what Daydreamer can use is read: headings, lists, bold,
/// italic and line spacing.
/// </summary>
public static class DocxImporter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static ImportedDocument Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var document = Load(zip, "word/document.xml")
            ?? throw new InvalidDataException("This doesn't look like a Word document (it has no word/document.xml).");
        var styles = new Styles(Load(zip, "word/styles.xml"));
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

    private static void ReadParagraph(XElement paragraph, Styles styles, Numbering numbering, ImportedDocument result)
    {
        var properties = paragraph.Element(W + "pPr");
        string? styleId = Val(properties?.Element(W + "pStyle"));

        int? heading = styles.HeadingLevel(styleId);
        if (heading == null && int.TryParse(Val(properties?.Element(W + "outlineLvl")), out int outline) && outline < 6)
            heading = outline + 1; // outline level 0 is Heading 1

        var (list, level, isChecked) = numbering.ListOf(properties?.Element(W + "numPr") ?? styles.NumberingOf(styleId));
        double? spacing = Spacing(properties) ?? styles.Spacing(styleId);

        // Collect the runs, splitting into separate lines at line breaks.
        var lines = new List<List<TextRun>> { new() };
        foreach (var run in paragraph.Descendants(W + "r"))
        {
            // Skip runs that belong to a nested paragraph (text boxes) or to deleted (tracked) text.
            if (run.Ancestors(W + "p").First() != paragraph || run.Ancestors(W + "del").Any()) continue;

            var runProperties = run.Element(W + "rPr");
            bool bold = IsOn(runProperties?.Element(W + "b"));
            bool italic = IsOn(runProperties?.Element(W + "i"));

            foreach (var part in run.Elements())
            {
                string name = part.Name.LocalName;
                if (part.Name.Namespace != W) continue;
                switch (name)
                {
                    case "t": lines[^1].Add(new TextRun(part.Value, bold, italic)); break;
                    case "tab": lines[^1].Add(new TextRun("\t", bold, italic)); break;
                    case "noBreakHyphen": lines[^1].Add(new TextRun("-", bold, italic)); break;
                    case "cr": lines.Add([]); break;
                    case "br" when Val(part, "type") is null or "textWrapping": lines.Add([]); break;
                }
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string text = RunText.Compose(lines[i], plain: heading != null, collapseWhitespace: false);
            // Continuation lines (after a line break) are plain text.
            var imported = i == 0
                ? new ImportedParagraph(text, heading, list, level, isChecked, spacing)
                : new ImportedParagraph(text, LineSpacing: spacing);
            if (imported.Heading != null && text.Length == 0) continue; // empty headings carry nothing
            result.Paragraphs.Add(imported);
        }
    }

    /// <summary>
    /// Word line spacing: "auto" counts in 240ths of a line (240 = single, 276 = 1.15, 360 = 1.5);
    /// "exact"/"atLeast" are in 20ths of a point, roughly converted assuming 11 pt text.
    /// </summary>
    internal static double? Spacing(XElement? paragraphProperties)
    {
        var spacing = paragraphProperties?.Element(W + "spacing");
        if (!double.TryParse(Val(spacing, "line"), NumberStyles.Float, CultureInfo.InvariantCulture, out double line) || line <= 0)
            return null;

        double value = Val(spacing, "lineRule") is null or "auto" ? line / 240 : line / 20 / (11 * 1.15);
        return value is >= 0.5 and <= 4 ? Math.Round(value, 2) : null;
    }

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

    /// <summary>Paragraph styles: which are headings, their spacing and numbering, following "based on" chains.</summary>
    private sealed class Styles
    {
        private readonly Dictionary<string, XElement> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? _defaultId;
        private readonly double? _documentDefaultSpacing;

        public Styles(XDocument? styles)
        {
            if (styles?.Root == null) return;
            foreach (var style in styles.Root.Elements(W + "style"))
            {
                if (style.Attribute(W + "type")?.Value != "paragraph" || style.Attribute(W + "styleId")?.Value is not { } id) continue;
                _byId[id] = style;
                if (style.Attribute(W + "default")?.Value is "1" or "true") _defaultId = id;
            }
            _documentDefaultSpacing = DocxImporter.Spacing(styles.Root.Element(W + "docDefaults")?.Element(W + "pPrDefault")?.Element(W + "pPr"));
        }

        public int? HeadingLevel(string? id)
        {
            foreach (var style in Chain(id))
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

        public double? Spacing(string? id)
        {
            foreach (var style in Chain(id ?? _defaultId))
            {
                if (DocxImporter.Spacing(style.Element(W + "pPr")) is { } spacing) return spacing;
            }
            return _documentDefaultSpacing;
        }

        public XElement? NumberingOf(string? id) =>
            Chain(id).Select(s => s.Element(W + "pPr")?.Element(W + "numPr")).FirstOrDefault(n => n != null);

        private IEnumerable<XElement> Chain(string? id)
        {
            for (int depth = 0; id != null && depth < 12 && _byId.TryGetValue(id, out var style); depth++)
            {
                yield return style;
                id = Val(style.Element(W + "basedOn"));
            }
        }
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
