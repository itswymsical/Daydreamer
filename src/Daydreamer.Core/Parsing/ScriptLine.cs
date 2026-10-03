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

public enum InlineKind { Bold, Italic }

/// <summary>
/// Inline emphasis inside a line: <c>**bold**</c> or <c>*italic*</c>.
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
public sealed class ScriptLine
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

    /// <summary>True for lines like <c>BOB: hi</c> where BOB hasn't been added as a character yet.</summary>
    public bool IsUnknownSpeaker => Type == ElementType.Dialogue && Character == null;

    /// <summary>True for <c>!text</c>: written as a description on purpose, whatever it looks like.</summary>
    public bool IsForcedAction => Type == ElementType.Action && !Label.IsEmpty;
}
