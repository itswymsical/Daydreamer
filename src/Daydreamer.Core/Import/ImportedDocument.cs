using Daydreamer.Core.Editing;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Import;

/// <summary>One paragraph read from a Word file or pasted from Google Docs.</summary>
/// <param name="Text">The words, with bold and italic already written as **bold** and *italic*.</param>
/// <param name="Heading">0 for the Title style, 1–6 for Heading 1–6, null for normal text.</param>
/// <param name="ListLevel">0 for a top-level list item, 1 for one level in, and so on.</param>
/// <param name="LineSpacing">The paragraph's line spacing (1 = single), if the source said.</param>
public sealed record ImportedParagraph(
    string Text,
    int? Heading = null,
    ListKind List = ListKind.None,
    int ListLevel = 0,
    bool IsChecked = false,
    double? LineSpacing = null);

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
            var weights = new Dictionary<double, int>();
            foreach (var p in Paragraphs)
            {
                if (p.Heading != null || p.LineSpacing is not { } spacing || p.Text.Length == 0) continue;
                double key = Math.Round(spacing, 2);
                weights[key] = weights.GetValueOrDefault(key) + p.Text.Length;
            }
            if (weights.Count == 0) return null;

            double common = weights.MaxBy(w => w.Value).Key;
            foreach (double standard in new[] { 1.0, 1.15, 1.5, 2.0 })
            {
                if (Math.Abs(common - standard) <= 0.05) return standard;
            }
            return common;
        }
    }
}

/// <summary>Turns an <see cref="ImportedDocument"/> into script text, using the project's heading map.</summary>
public static class ScriptImporter
{
    public static string ToScript(ImportedDocument document, HeadingMap headings, ScriptParser parser)
    {
        var lines = new List<string>();
        int page = 0, panel = 0;
        // Per indent level: which kind of list is running there, and how many items it has had.
        var listCounters = new Dictionary<int, (ListKind Kind, int Count)>();

        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.List == ListKind.None) listCounters.Clear();

            if (paragraph.Heading is int level)
            {
                string words = LineEditing.ClearEmphasis(paragraph.Text).Trim(); // headings are styled already
                var type = headings.For(level);
                string line = HeadingLine(type, words, parser, ref page, ref panel);

                // Breathing room before pages, panels and headings, like a hand-written script.
                if (type is ElementType.Page or ElementType.Panel or ElementType.Heading && lines.Count > 0 && lines[^1].Length > 0)
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
                lines.Add(indent + LineEditing.Marker(paragraph.List, number, paragraph.IsChecked) + paragraph.Text.Trim());
                continue;
            }

            lines.Add(paragraph.Text.Trim());
        }

        // No stray blank lines at the very start or end.
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static string HeadingLine(ElementType type, string words, ScriptParser parser, ref int page, ref int panel)
    {
        switch (type)
        {
            case ElementType.Page:
                panel = 0;
                // "Page 3" already reads as a page; keep it (and keep counting from it).
                if (parser.ParseLine(words).Type == ElementType.Page)
                {
                    page = FirstNumber(words) ?? page + 1;
                    return words;
                }
                page++;
                return words.Length == 0 ? $"PAGE {page}" : $"PAGE {page}: {words}";

            case ElementType.Panel:
                if (parser.ParseLine(words).Type == ElementType.Panel)
                {
                    panel = FirstNumber(words) ?? panel + 1;
                    return words;
                }
                panel++;
                return words.Length == 0 ? $"PANEL {panel}" : $"PANEL {panel}: {words}";

            case ElementType.Heading: return "# " + words;
            case ElementType.Caption: return "CAPTION: " + words;
            case ElementType.Sfx: return "SFX: " + words;
            case ElementType.Note: return "// " + words;
            default: return LineConverter.AsDescription(words, parser);
        }
    }

    private static int? FirstNumber(string text)
    {
        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : null;
    }
}
