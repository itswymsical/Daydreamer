using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>A slice of a line: where it starts (0 = first character of the line) and how long it is.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public static readonly TextRange Empty = new(0, 0);

    public int End => Start + Length;
    public bool IsEmpty => Length <= 0;

    public string Of(string text) => IsEmpty ? "" : text.Substring(Start, Length);
}

public enum InlineKind
{
    Bold,
    Italic,

    /// <summary>Game dialogue: words with an effect on them, <c>[shake: words]</c>, or a pause, <c>[]</c> (just a marker, no words).</summary>
    Effect,

    /// <summary>Game dialogue: something the game fills in, <c>[player]</c>. Not checked for spelling.</summary>
    Token,

    /// <summary>Game dialogue: where a choice leads, <c>-> Greeting_True (dead)</c>. Shown faintly and not checked for spelling.</summary>
    Flow,

    /// <summary>Game dialogue: a conversation's name in its heading. Looks like the rest of the line, not checked for spelling.</summary>
    Identifier,
}

/// <summary>
/// A piece of markup inside a line, such as <c>**bold**</c> or <c>*italic*</c>.
/// <see cref="Content"/> is the text between the markers.
/// </summary>
public sealed record InlineSpan(InlineKind Kind, TextRange OpenMarker, TextRange Content, TextRange CloseMarker);

/// <summary>
/// The parser's understanding of a single line. For <c>AKIRA (whisper): Don't move.</c>:
/// <list type="bullet">
///   <item><see cref="Type"/> = Dialogue, <see cref="Character"/> = Akira</item>
///   <item><see cref="Label"/> covers <c>AKIRA (whisper):</c></item>
///   <item><see cref="ModifierRange"/> covers <c>(whisper)</c>, <see cref="Modifier"/> = "whisper"</item>
///   <item><see cref="Body"/> covers <c>Don't move.</c></item>
/// </list>
/// </summary>
public sealed record ScriptLine
{
    public ElementType Type { get; init; }

    /// <summary>The defined character this line belongs to, if any.</summary>
    public Character? Character { get; init; }

    /// <summary>The speaker name exactly as typed. Set even when the character isn't defined yet.</summary>
    public string? Speaker { get; init; }

    /// <summary>Text inside the parentheses, e.g. "thought" or "off-panel".</summary>
    public string? Modifier { get; init; }

    /// <summary>The tag at the start of the line: "AKIRA:", "SFX:", "# ". Empty if there isn't one.</summary>
    public TextRange Label { get; init; }

    public TextRange ModifierRange { get; init; }

    /// <summary>The actual content of the line.</summary>
    public TextRange Body { get; init; }

    public IReadOnlyList<InlineSpan> Inlines { get; init; } = [];

    /// <summary>For list lines: bulleted, numbered or checklist.</summary>
    public ListKind List { get; init; }

    /// <summary>The list marker itself: <c>-</c>, <c>1.</c> or <c>[ ]</c>.</summary>
    public TextRange ListMarker { get; init; }

    /// <summary>For checklist lines: <c>[x]</c> rather than <c>[ ]</c>.</summary>
    public bool IsChecked { get; init; }

    /// <summary>How far the line is indented: one level per tab (or per four spaces) at the start.</summary>
    public int IndentLevel { get; init; }

    /// <summary>
    /// The line's own settings, written as a tag at its start — <c>{center}</c>, <c>{page}</c>, <c>{caption right}</c>.
    /// The editor hides it; it's how per-line alignment and hand-picked line types are saved in plain text.
    /// </summary>
    public TextRange Tag { get; init; }

    /// <summary>This line's own alignment (from its tag), which wins over its style's alignment.</summary>
    public TextAlign? Alignment { get; init; }

    /// <summary>The name in the line's <c>{style:Letter}</c> tag, even if no such style exists (any more).</summary>
    public string? StyleName { get; init; }

    /// <summary>The style that name refers to in this document, if there is one. It goes on top of all the line's other styles.</summary>
    public NamedStyle? CustomStyle { get; init; }

    /// <summary>The ** or * around a label written in bold or italic, like <c>**AKIRA** Hi.</c></summary>
    public IReadOnlyList<TextRange> LabelMarkers { get; init; } = [];

    /// <summary>True for lines like <c>BOB: hi</c> where BOB hasn't been added as a character yet.</summary>
    public bool IsUnknownSpeaker => Type == ElementType.Dialogue && Character == null;

    /// <summary>True for <c>!text</c> or <c>{text}</c>: written as a description on purpose, whatever it looks like.</summary>
    public bool IsForcedAction => Type == ElementType.Action && !Label.IsEmpty;

    /// <summary>
    /// The characters that only say how to format the line — the {tag}, the "# " of a heading, a leading "!",
    /// and the ** and * around bold and italic. The editor hides them, except on the line being edited.
    /// </summary>
    public IReadOnlyList<TextRange> Markup()
    {
        var ranges = new List<TextRange>();
        if (!Tag.IsEmpty) ranges.Add(Tag);
        if ((Type == ElementType.Heading || IsForcedAction) && !Label.IsEmpty) ranges.Add(Label);
        ranges.AddRange(LabelMarkers);
        foreach (var span in Inlines)
        {
            ranges.Add(span.OpenMarker);
            ranges.Add(span.CloseMarker);
        }

        // In order, with overlapping pieces joined (a {tag} and the "# " after it, say).
        var merged = new List<TextRange>();
        foreach (var range in ranges.Where(r => !r.IsEmpty).OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = new TextRange(merged[^1].Start, Math.Max(merged[^1].End, range.End) - merged[^1].Start);
            else
                merged.Add(range);
        }
        return merged;
    }

    /// <summary>Parts of the line that are names or instructions rather than words to proofread.</summary>
    public IEnumerable<TextRange> NotProse() =>
        Inlines.Where(span => span.Kind is InlineKind.Token or InlineKind.Flow or InlineKind.Identifier).Select(span => span.Content);
}
