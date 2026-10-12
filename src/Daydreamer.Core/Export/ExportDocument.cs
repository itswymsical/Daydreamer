using System.Text;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Export;

/// <summary>Choices made when exporting.</summary>
/// <param name="IncludeNotes">Notes to yourself (<c>// note</c>, <c>NOTE:</c>) are left out unless this is on.</param>
public sealed record ExportOptions(bool IncludeNotes = false);

/// <summary>A piece of a line with one look: "AKIRA:" in bold, then " It's late." in AKIRA's font.</summary>
/// <param name="Strikethrough">The text of a ticked checklist item, which is struck through.</param>
public sealed record ExportRun(string Text, ResolvedStyle Style, bool Strikethrough = false);

/// <summary>
/// One line of the script, ready for paper: the words people read (no {tags}, "# " or **), and how every
/// part of them looks. Each export format (PDF, Word, text) writes these out in its own way.
/// </summary>
/// <param name="Style">The line's main look. Its alignment and spacing are the paragraph's; an empty line is as tall as its font.</param>
/// <param name="Runs">The words, in pieces that each have one look. Empty for an empty line.</param>
/// <param name="IndentLevel">How many steps the line is indented (one per tab at its start).</param>
/// <param name="Marker">What a list item shows in front of its words: "•", "3." or a checkbox ("☐" / "☑").</param>
/// <param name="Number">For numbered items, the number written in the script.</param>
/// <param name="Character">The character a dialogue line belongs to, if they're defined.</param>
public sealed record ExportParagraph(
    ElementType Type,
    ResolvedStyle Style,
    IReadOnlyList<ExportRun> Runs,
    int IndentLevel = 0,
    ListKind List = ListKind.None,
    string Marker = "",
    int Number = 0,
    bool IsChecked = false,
    Character? Character = null)
{
    /// <summary>The line's words, without its list marker.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// A document worked out for exporting: every line parsed and styled with the project's rules, exactly as
/// the editor shows it in light mode (paper is white). The editor's hidden markup is gone, and so are notes
/// to yourself unless <see cref="ExportOptions.IncludeNotes"/> says otherwise.
/// </summary>
public sealed class ExportDocument
{
    public const string Bullet = "•";
    public const string Unchecked = "☐";
    public const string Checked = "☑";

    private ExportDocument(string title, Project project, ResolvedStyle baseStyle, List<ExportParagraph> paragraphs)
    {
        Title = title;
        Project = project;
        BaseStyle = baseStyle;
        Paragraphs = paragraphs;
    }

    public string Title { get; }

    /// <summary>Where the formatting rules came from (Word export names its styles after them).</summary>
    public Project Project { get; }

    /// <summary>"Default text", which every other style builds on.</summary>
    public ResolvedStyle BaseStyle { get; }

    public IReadOnlyList<ExportParagraph> Paragraphs { get; }

    /// <summary>The page is white, so text uses light mode's grays (unless a style picked its own color).</summary>
    public static ThemePalette Palette => ThemePalette.Light;

    public static ExportDocument Build(string title, string text, Project project, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var parser = new ScriptParser(new CharacterRegistry(project.Characters), project.Keywords);
        var styles = project.Styles;
        var paragraphs = new List<ExportParagraph>();

        var lines = ScriptParser.SplitLines(text).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = parser.ParseLine(lines[i]);
            if (line.Type == ElementType.Note && !options.IncludeNotes)
            {
                // A note on its own between two empty lines would leave a double gap behind: close it up.
                bool gapBefore = paragraphs.Count == 0 || paragraphs[^1].Type == ElementType.Blank;
                if (gapBefore && i + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[i + 1])) i++;
                continue;
            }
            paragraphs.Add(BuildParagraph(lines[i], line, styles));
        }

        // No empty lines left over at the very end.
        while (paragraphs.Count > 0 && paragraphs[^1].Type == ElementType.Blank) paragraphs.RemoveAt(paragraphs.Count - 1);

        return new ExportDocument(title, project, StyleResolver.ResolveBase(styles, Palette), paragraphs);
    }

    private static ExportParagraph BuildParagraph(string text, ScriptLine line, StyleSheet styles)
    {
        var body = StyleResolver.ResolveBody(styles, line, Palette);
        if (line.Type == ElementType.Blank)
            return new ExportParagraph(ElementType.Blank, body, []);

        // Give every character the look of the last segment that covers it (later segments win, as in the editor).
        var looks = new StyledSegment?[text.Length];
        foreach (var segment in StyleResolver.Segment(styles, line, text.Length, Palette))
        {
            for (int i = segment.Range.Start; i < segment.Range.End && i < text.Length; i++)
                looks[i] = segment;
        }

        // Left out: the markup the editor hides ({tags}, "# ", **), and anything before the words — the
        // indentation (it becomes the paragraph's indent) and a list item's marker (written separately).
        var hidden = new bool[text.Length];
        foreach (var range in line.Markup())
        {
            for (int i = range.Start; i < range.End && i < text.Length; i++)
                hidden[i] = true;
        }
        int start = line.Type == ElementType.ListItem ? line.Body.Start : 0;
        while (start < text.Length && (hidden[start] || char.IsWhiteSpace(text[start]))) start++;
        int end = text.Length;
        while (end > start && (hidden[end - 1] || char.IsWhiteSpace(text[end - 1]))) end--;

        // Join neighbouring characters that look the same into runs.
        var runs = new List<ExportRun>();
        var words = new StringBuilder();
        StyledSegment? current = null;
        for (int i = start; i < end; i++)
        {
            if (hidden[i] || looks[i] is not { } look) continue;
            if (current == null || look.Style != current.Style || look.IsCompleted != current.IsCompleted)
            {
                if (current != null) runs.Add(new ExportRun(words.ToString(), current.Style, current.IsCompleted));
                words.Clear();
                current = look;
            }
            words.Append(text[i]);
        }
        if (current != null) runs.Add(new ExportRun(words.ToString(), current.Style, current.IsCompleted));

        string marker = line.List switch
        {
            ListKind.Bullet => Bullet,
            ListKind.Numbered => line.ListMarker.Of(text),
            ListKind.Checkbox => line.IsChecked ? Checked : Unchecked,
            _ => "",
        };
        int number = line.List == ListKind.Numbered && int.TryParse(marker.TrimEnd('.', ')'), out int n) ? n : 0;

        return new ExportParagraph(line.Type, body, runs, line.IndentLevel, line.List, marker, number, line.IsChecked, line.Character);
    }
}
