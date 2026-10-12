using Daydreamer.Core.Editing;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Import;

/// <summary>One paragraph read from a Word file or pasted from Google Docs.</summary>
/// <param name="Text">The words, with bold and italic already written as **bold** and *italic*.</param>
/// <param name="Heading">0 for the Title style, 1–6 for Heading 1–6, null for normal text.</param>
/// <param name="ListLevel">0 for a top-level list item, 1 for one level in, and so on.</param>
/// <param name="LineSpacing">The paragraph's line spacing (1 = single), if the source said.</param>
/// <param name="Alignment">Centered, right-aligned or justified paragraphs (null for normal left).</param>
/// <param name="FontFamily">The font most of the paragraph is in, if the source said.</param>
/// <param name="FontSize">Its size in points, if the source said.</param>
/// <param name="ParagraphSpacing">The space the paragraph keeps above and below itself, in points.</param>
public sealed record ImportedParagraph(
    string Text,
    int? Heading = null,
    ListKind List = ListKind.None,
    int ListLevel = 0,
    bool IsChecked = false,
    double? LineSpacing = null,
    TextAlign? Alignment = null,
    string? FontFamily = null,
    double? FontSize = null,
    double? ParagraphSpacing = null);

/// <summary>A document read from another app, before it's turned into Daydreamer script text.</summary>
public sealed class ImportedDocument
{
    public List<ImportedParagraph> Paragraphs { get; } = [];

    /// <summary>
    /// The line spacing most of the text uses (weighted by how much text has it), snapped to the usual
    /// values (1, 1.15, 1.5, 2) when it's close. Null if the source never said.
    /// </summary>
    public double? LineSpacing
    {
        get
        {
            var common = MostUsed(p => p.LineSpacing is { } s ? Math.Round(s, 2) : null);
            if (common is not { } spacing) return null;
            foreach (double standard in new[] { 1.0, 1.15, 1.5, 2.0 })
            {
                if (Math.Abs(spacing - standard) <= 0.05) return standard;
            }
            return spacing;
        }
    }

    /// <summary>The font most of the body text uses.</summary>
    public string? FontFamily =>
        BodyText().Where(p => p.FontFamily != null)
            .GroupBy(p => p.FontFamily!)
            .MaxBy(group => group.Sum(p => p.Text.Length))?.Key;

    /// <summary>The size most of the body text uses, in points (to the nearest half point).</summary>
    public double? FontSize => MostUsed(p => p.FontSize is { } s ? Math.Round(s * 2) / 2 : null);

    /// <summary>The space most paragraphs keep between each other, in points.</summary>
    public double? ParagraphSpacing => MostUsed(p => p.ParagraphSpacing is { } s ? Math.Round(s) : null);

    /// <summary>How most of the body text is aligned (left if the source never said).</summary>
    public TextAlign Alignment =>
        BodyText().GroupBy(p => p.Alignment ?? TextAlign.Left)
            .MaxBy(group => group.Sum(p => p.Text.Length))?.Key ?? TextAlign.Left;

    /// <summary>Whichever value covers the most body text.</summary>
    private double? MostUsed(Func<ImportedParagraph, double?> valueOf) =>
        BodyText().Where(p => valueOf(p) != null)
            .GroupBy(p => valueOf(p)!.Value)
            .MaxBy(group => group.Sum(p => p.Text.Length))?.Key;

    /// <summary>Paragraphs with words in them, leaving out headings (they're usually bigger than the rest).</summary>
    private IEnumerable<ImportedParagraph> BodyText() =>
        Paragraphs.Where(p => p.Heading == null && p.Text.Length > 0);
}

/// <summary>Turns an <see cref="ImportedDocument"/> into script text, using the project's heading map.</summary>
public static class ScriptImporter
{
    /// <param name="normalAlignment">
    /// How the document's text is normally aligned. Paragraphs aligned differently (a centered title,
    /// say) remember their own alignment in a hidden tag.
    /// </param>
    public static string ToScript(ImportedDocument document, HeadingMap headings, ScriptParser parser, TextAlign normalAlignment = TextAlign.Left)
    {
        var lines = new List<string>();
        // Per indent level: which kind of list is running there, and how many items it has had.
        var listCounters = new Dictionary<int, (ListKind Kind, int Count)>();
        // Documents that keep space between paragraphs don't need blank lines added for air.
        bool spaced = document.ParagraphSpacing is >= 4;

        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.List == ListKind.None) listCounters.Clear();
            var ownAlignment = paragraph.Alignment ?? TextAlign.Left;
            TextAlign? alignment = ownAlignment == normalAlignment ? null : ownAlignment;

            if (paragraph.Heading is int level)
            {
                string words = LineEditing.ClearEmphasis(paragraph.Text).Trim(); // headings are styled already
                var type = headings.For(level);
                string line = HeadingLine(type, words, alignment, parser);

                // Breathing room before pages, panels and headings, like a hand-written script.
                if (!spaced && type is ElementType.Page or ElementType.Panel or ElementType.Heading && lines.Count > 0 && lines[^1].Length > 0)
                    lines.Add("");
                lines.Add(line);
                continue;
            }

            if (paragraph.List != ListKind.None)
            {
                foreach (int deeper in listCounters.Keys.Where(l => l > paragraph.ListLevel).ToList())
                    listCounters.Remove(deeper);
                // A different kind of list at this level starts counting again.
                int number = listCounters.TryGetValue(paragraph.ListLevel, out var running) && running.Kind == paragraph.List
                    ? running.Count + 1
                    : 1;
                listCounters[paragraph.ListLevel] = (paragraph.List, number);
                string indent = string.Concat(Enumerable.Repeat(LineEditing.IndentUnit, paragraph.ListLevel));
                lines.Add(ScriptParser.JoinTag(indent, null, alignment, LineEditing.Marker(paragraph.List, number, paragraph.IsChecked) + paragraph.Text.Trim()));
                continue;
            }

            string text = paragraph.Text.Trim();
            lines.Add(text.Length == 0 ? "" : ScriptParser.JoinTag("", null, alignment, text));
        }

        // No stray blank lines at the very start or end.
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// A heading becomes the kind of line the heading map says. Words that already read as that kind
    /// ("Page 3", or "SCENE 3" when SCENE is the panel keyword) stay as they are; otherwise a hidden tag
    /// marks the type, so the line shows just its words.
    /// </summary>
    private static string HeadingLine(ElementType type, string words, TextAlign? alignment, ScriptParser parser)
    {
        if (type is ElementType.Page or ElementType.Panel && parser.ParseLine(words).Type == type)
            return ScriptParser.JoinTag("", null, alignment, words);

        return type switch
        {
            ElementType.Heading or ElementType.Page or ElementType.Panel or ElementType.Caption or ElementType.Sfx or ElementType.Note =>
                ScriptParser.JoinTag("", ScriptParser.TagWords[type], alignment, words),
            _ => alignment == null ? LineConverter.AsDescription(words, parser) : ScriptParser.JoinTag("", "text", alignment, words),
        };
    }

    /// <summary>
    /// The word a document uses for a kind of heading, when most of them start the same way:
    /// "SCENE 1 (Opening)", "SCENE 2 (Home)" → "SCENE". Null if there's no clear pattern.
    /// </summary>
    public static string? SuggestKeyword(ImportedDocument document, HeadingMap headings, ElementType type)
    {
        var firstWords = document.Paragraphs
            .Where(p => p.Heading is int level && headings.For(level) == type)
            .Select(p => LineEditing.ClearEmphasis(p.Text).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(words => words.Length >= 2 && words[0].All(char.IsLetter) && char.IsDigit(words[1][0]) ? words[0].ToUpperInvariant() : null)
            .ToList();
        if (firstWords.Count < 2) return null;

        var common = firstWords.OfType<string>().GroupBy(w => w).MaxBy(g => g.Count());
        return common != null && common.Count() * 10 >= firstWords.Count * 6 ? common.Key : null; // at least 60% of them
    }
}
