using System.Text.RegularExpressions;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>A <c>[…]</c> in game dialogue, taken apart. See <see cref="DialogueSyntax"/>.</summary>
/// <param name="Start">Where the opening bracket is in the text.</param>
/// <param name="Length">How long the whole thing is, brackets included.</param>
/// <param name="Effect">What's before the colon in <c>[shake 0.6: words]</c>, or a pause's length in <c>[30]</c>. Null for plain words.</param>
/// <param name="WordsStart">Where the words inside begin.</param>
/// <param name="WordsLength">How long the words are. Zero for a pause.</param>
/// <param name="IsToken">True for what the game fills in: <c>[player]</c>, <c>[speakerName:Guide]</c>.</param>
public readonly record struct DialogueMark(int Start, int Length, string? Effect, int WordsStart, int WordsLength, bool IsToken);

/// <summary>
/// The marks a game dialogue line can carry on top of the usual script syntax:
/// <code>
///   []                 a pause, as long as the punctuation before it suggests
///   [30]               a pause of 30 frames
///   [shake 0.6: words] words with an effect on them
///   [words]            stressed words (the same as [emphasis: words])
///   [player]           the player's name (also [speakerName:Guide], as the game writes it)
///   - Text -> Next     a choice, where it leads, and (dead) or (run: code)
/// </code>
/// The list of effects is in <c>Game/DialogueEffects.cs</c>. This class only finds the marks, so the editor can
/// hide and color them; turning them into what the game reads happens when exporting.
/// </summary>
public static partial class DialogueSyntax
{
    public const string SpeakerNamePrefix = "speakerName:";
    public const string PlayerToken = "player";

    // An effect's name, then its settings: "shake", "shake 0.6", "emphasis 0.25 lift 2". Then a colon.
    [GeneratedRegex(@"^(?<effect>[A-Za-z]+(?:[ ]+[A-Za-z0-9_.\-]+)*)[ ]*:[ ]?")]
    private static partial Regex EffectPrefixRegex();

    // What can come after a choice's words: where it leads, (dead), (run: code).
    [GeneratedRegex(@"(?:(?:^|\s)(?:->|→)\s*[\p{L}\p{N}_.]+)?(?:\s*\(dead\))?(?:\s*\(run:.*\))?\s*$", RegexOptions.RightToLeft)]
    private static partial Regex ChoiceFlowRegex();

    /// <summary>Every <c>[…]</c> in part of a line, left to right. A <c>[</c> with no <c>]</c> is just a bracket, as in the game.</summary>
    public static IEnumerable<DialogueMark> FindMarks(string text, int start, int end)
    {
        for (int i = text.IndexOf('[', start); i >= 0 && i < end;)
        {
            int close = text.IndexOf(']', i + 1);
            if (close < 0 || close >= end) yield break;
            yield return ReadMark(text, i, close);
            i = text.IndexOf('[', close + 1);
        }
    }

    private static DialogueMark ReadMark(string text, int open, int close)
    {
        int length = close - open + 1;
        string inner = text.Substring(open + 1, close - open - 1);
        if (inner.Length == 0)
            return new DialogueMark(open, length, null, close, 0, IsToken: false);

        if (inner.All(char.IsAsciiDigit))
            return new DialogueMark(open, length, $"pause {inner}", close, 0, IsToken: false);

        if (inner.Equals(PlayerToken, StringComparison.OrdinalIgnoreCase) || inner.StartsWith(SpeakerNamePrefix, StringComparison.OrdinalIgnoreCase))
            return new DialogueMark(open, length, null, open + 1, inner.Length, IsToken: true);

        var prefix = EffectPrefixRegex().Match(inner);
        if (prefix.Success)
        {
            int words = open + 1 + prefix.Length;
            return new DialogueMark(open, length, prefix.Groups["effect"].Value, words, close - words, IsToken: false);
        }
        return new DialogueMark(open, length, null, open + 1, inner.Length, IsToken: false);
    }

    /// <summary>Where a choice's words end and its "-> Next (dead)" begins.</summary>
    public static int ChoiceFlowStart(string body)
    {
        var flow = ChoiceFlowRegex().Match(body);
        return flow.Success ? flow.Index : body.Length;
    }

    /// <summary>A parsed line with game dialogue's marks added to its inline spans.</summary>
    internal static ScriptLine AddMarkup(string text, ScriptLine line)
    {
        if (line.Type == ElementType.Heading && !line.Body.IsEmpty)
            return line with { Inlines = [.. line.Inlines, new InlineSpan(InlineKind.Identifier, TextRange.Empty, line.Body, TextRange.Empty)] };

        if (line.Type is not (ElementType.Dialogue or ElementType.Action or ElementType.ListItem) || line.Body.IsEmpty)
            return line;

        var spans = new List<InlineSpan>(line.Inlines);
        int wordsEnd = line.Body.End;
        if (line.Type == ElementType.ListItem)
        {
            int flow = ChoiceFlowStart(line.Body.Of(text));
            while (flow < line.Body.Length && char.IsWhiteSpace(text[line.Body.Start + flow])) flow++;
            if (flow < line.Body.Length)
            {
                wordsEnd = line.Body.Start + flow;
                spans.Add(new InlineSpan(InlineKind.Flow, TextRange.Empty, new TextRange(wordsEnd, line.Body.End - wordsEnd), TextRange.Empty));
            }
        }

        foreach (var mark in FindMarks(text, line.Body.Start, wordsEnd))
        {
            spans.Add(mark.WordsLength == 0
                ? new InlineSpan(InlineKind.Effect, new TextRange(mark.Start, mark.Length), new TextRange(mark.Start + mark.Length, 0), TextRange.Empty)
                : new InlineSpan(mark.IsToken ? InlineKind.Token : InlineKind.Effect,
                    new TextRange(mark.Start, mark.WordsStart - mark.Start),
                    new TextRange(mark.WordsStart, mark.WordsLength),
                    new TextRange(mark.WordsStart + mark.WordsLength, 1)));
        }
        return spans.Count == line.Inlines.Count ? line : line with { Inlines = spans };
    }
}
