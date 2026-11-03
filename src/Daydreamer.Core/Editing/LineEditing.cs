using System.Text.RegularExpressions;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Editing;

/// <summary>What pressing Enter on a list line should do.</summary>
/// <param name="EndList">The item was empty: remove its marker instead of starting another item.</param>
/// <param name="NextPrefix">What the new line should start with, e.g. "\t- " or "3. ".</param>
public sealed record ListEnter(bool EndList, string? NextPrefix)
{
    public static readonly ListEnter NotAList = new(false, null);
    public bool IsList => EndList || NextPrefix != null;
}

/// <summary>
/// The toolbar's text tools — lists, indents and clear formatting. They all work on the plain text,
/// so what you see is exactly what's saved: "- item" is a bullet, a leading tab is an indent.
/// </summary>
public static partial class LineEditing
{
    /// <summary>One indent level.</summary>
    public const string IndentUnit = "\t";

    // Same emphasis pattern the parser uses.
    [GeneratedRegex(@"\*\*(?=\S)(?<b>.+?)(?<=\S)\*\*|\*(?=\S)(?<i>[^*]+?)(?<=\S)\*")]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"^(?<indent>\s*)(?<number>\d{1,3})(?<punct>[.)])\s+")]
    private static partial Regex NumberPrefixRegex();

    /// <summary>The start of a list item: "- ", "3. " or "[ ] ".</summary>
    public static string Marker(ListKind kind, int number = 1, bool isChecked = false) => kind switch
    {
        ListKind.Bullet => "- ",
        ListKind.Numbered => $"{number}. ",
        ListKind.Checkbox => isChecked ? "[x] " : "[ ] ",
        _ => "",
    };

    /// <summary>
    /// Makes the lines a list of the given kind — or, if they already all are, turns them back into
    /// plain lines (like the list buttons in Google Docs). Blank lines are left alone.
    /// </summary>
    public static List<string> ToggleList(IReadOnlyList<string> lines, ListKind kind, ScriptParser parser)
    {
        var parsed = lines.Select(parser.ParseLine).ToList();
        var content = parsed.Where(p => p.Type != ElementType.Blank).ToList();
        bool removing = content.Count > 0 && content.All(p => p.List == kind);

        var result = new List<string>(lines.Count);
        var counters = new Dictionary<int, int>();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            var p = parsed[i];
            if (p.Type == ElementType.Blank)
            {
                result.Add(line);
                continue;
            }

            // Keep the line's own alignment and style; any line-type tag gives way to the list.
            var (indent, _, alignment, style, rest) = parser.SplitTag(line);
            string words = p.Type == ElementType.ListItem || p.IsForcedAction || !p.Tag.IsEmpty && p.Label == p.Tag
                ? p.Body.Of(line)
                : rest.Trim();
            if (removing)
            {
                string plain = LineConverter.AsDescription(words, parser);
                result.Add(ScriptParser.JoinTag(indent, null, alignment, plain, style));
                continue;
            }

            // Numbering counts separately at each indent level, restarting under each parent item.
            foreach (int deeper in counters.Keys.Where(level => level > p.IndentLevel).ToList())
                counters.Remove(deeper);
            int number = counters.GetValueOrDefault(p.IndentLevel) + 1;
            counters[p.IndentLevel] = number;
            result.Add(ScriptParser.JoinTag(indent, null, alignment, Marker(kind, number) + words, style));
        }
        return result;
    }

    /// <summary>Enter on a list item continues the list; Enter on an empty item ends it.</summary>
    public static ListEnter OnEnter(string line, ScriptParser parser)
    {
        var p = parser.ParseLine(line);
        if (p.Type != ElementType.ListItem) return ListEnter.NotAList;
        if (p.Body.IsEmpty) return new ListEnter(EndList: true, NextPrefix: null);

        string indent = parser.SplitTag(line).Indent;
        int number = p.List == ListKind.Numbered && int.TryParse(p.ListMarker.Of(line).TrimEnd('.', ')'), out int n) ? n + 1 : 1;
        // The next item keeps this one's alignment, like the next paragraph does in a word processor.
        return new ListEnter(EndList: false, NextPrefix: ScriptParser.JoinTag(indent, null, p.Alignment, Marker(p.List, number), p.StyleName));
    }

    /// <summary>
    /// Renumbers the list around line <paramref name="index"/> so numbered items count 1, 2, 3 at each
    /// indent level. Returns only the lines that changed (line index → new text).
    /// </summary>
    public static Dictionary<int, string> Renumber(IReadOnlyList<string> lines, int index, ScriptParser parser)
    {
        var changes = new Dictionary<int, string>();
        if (index < 0 || index >= lines.Count || parser.ParseLine(lines[index]).Type != ElementType.ListItem)
            return changes;

        // A list is a run of list lines with no blank or non-list line in between.
        int start = index, end = index;
        while (start > 0 && parser.ParseLine(lines[start - 1]).Type == ElementType.ListItem) start--;
        while (end < lines.Count - 1 && parser.ParseLine(lines[end + 1]).Type == ElementType.ListItem) end++;

        var counters = new Dictionary<int, int>();
        for (int i = start; i <= end; i++)
        {
            var p = parser.ParseLine(lines[i]);
            foreach (int deeper in counters.Keys.Where(level => level > p.IndentLevel).ToList())
                counters.Remove(deeper);
            if (p.List != ListKind.Numbered)
                continue;

            int number = counters.GetValueOrDefault(p.IndentLevel) + 1;
            counters[p.IndentLevel] = number;
            string renumbered = NumberPrefixRegex().Replace(lines[i], m => $"{m.Groups["indent"].Value}{number}{m.Groups["punct"].Value} ", 1);
            if (renumbered != lines[i]) changes[i] = renumbered;
        }
        return changes;
    }

    /// <summary>[ ] ↔ [x]. Lines that aren't checklist items come back unchanged.</summary>
    public static string ToggleCheckbox(string line, ScriptParser parser)
    {
        var p = parser.ParseLine(line);
        if (p.List != ListKind.Checkbox) return line;
        var marker = p.ListMarker;
        return line[..marker.Start] + (p.IsChecked ? "[ ]" : "[x]") + line[marker.End..];
    }

    /// <summary>
    /// Sets one line's own alignment, kept in its hidden tag (e.g. <c>{center}</c>). Pass null to let the
    /// line follow its style's alignment again.
    /// </summary>
    public static string SetLineAlignment(string line, TextAlign? alignment, ScriptParser parser)
    {
        var (indent, typeWord, _, style, rest) = parser.SplitTag(line);
        return ScriptParser.JoinTag(indent, typeWord, alignment, rest, style);
    }

    /// <summary>
    /// Puts a line in one of your own styles, kept in its hidden tag (e.g. <c>{style:Letter}</c>). Pass null
    /// to take the line out of it again.
    /// </summary>
    public static string SetLineStyle(string line, string? style, ScriptParser parser)
    {
        var (indent, typeWord, alignment, _, rest) = parser.SplitTag(line);
        return ScriptParser.JoinTag(indent, typeWord, alignment, rest, style);
    }

    /// <summary>A document's text with every line in the style <paramref name="oldName"/> moved to <paramref name="newName"/>.</summary>
    public static string RenameStyle(string text, string oldName, string newName, ScriptParser parser)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool carriageReturn = lines[i].EndsWith('\r');
            string line = carriageReturn ? lines[i][..^1] : lines[i];
            if (!string.Equals(parser.SplitTag(line).Style, oldName, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = SetLineStyle(line, newName, parser) + (carriageReturn ? "\r" : "");
        }
        return string.Join('\n', lines);
    }

    public static string Indent(string line) => IndentUnit + line;

    /// <summary>Removes one level of indent: a tab, or up to four spaces.</summary>
    public static string Outdent(string line)
    {
        if (line.StartsWith('\t')) return line[1..];
        int spaces = 0;
        while (spaces < 4 && spaces < line.Length && line[spaces] == ' ') spaces++;
        return line[spaces..];
    }

    /// <summary>Clear formatting: removes **bold** and *italic* markers, keeping the words.</summary>
    public static string ClearEmphasis(string text) =>
        EmphasisRegex().Replace(text, m => m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["i"].Value);
}
