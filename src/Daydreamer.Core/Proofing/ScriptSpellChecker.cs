using System.Text;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Proofing;

public enum SpellingIssueKind
{
    /// <summary>A word the dictionary doesn't know.</summary>
    Misspelled,

    /// <summary>The same word twice in a row: "the the".</summary>
    RepeatedWord,
}

/// <summary>A spelling problem in a line: where it is (0 = the line's first character) and what kind it is.</summary>
public sealed record SpellingIssue(TextRange Range, SpellingIssueKind Kind);

/// <summary>
/// The dictionary that does the actual checking. In the app it's Windows' own spell checker (the one
/// Word and Edge use); the tests use a pretend one.
/// </summary>
public interface ISpellingEngine
{
    /// <summary>The problems in a piece of text, with their positions in it.</summary>
    IEnumerable<SpellingIssue> Check(string text);

    /// <summary>Likely corrections for a misspelled word, best first.</summary>
    IReadOnlyList<string> Suggest(string word);
}

/// <summary>
/// Spell checking that understands scripts. Only the words a reader reads are checked: dialogue, captions,
/// descriptions, headings and lists. Speaker names, PAGE and PANEL lines, sound effects, notes and hidden
/// formatting are left alone. Character names (and their aliases), words in capitals (names, shouting, sound
/// words), and the story's own word list never count as mistakes.
/// </summary>
public sealed class ScriptSpellChecker
{
    private const int CacheLimit = 20_000;

    private readonly ISpellingEngine _engine;
    private readonly Dictionary<string, IReadOnlyList<SpellingIssue>> _cache = [];
    private HashSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);

    public ScriptSpellChecker(ISpellingEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Words that are fine in this story: its characters' names and aliases, and its own word list.
    /// A name of several words ("MR. TANAKA") makes each of its words known, and so does a word with a
    /// hyphen ("Neo-Kyoto"), since dictionaries often check the halves separately.
    /// </summary>
    public void SetKnownWords(IEnumerable<Character> characters, IEnumerable<string> storyWords)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string phrase in characters.SelectMany(c => c.AllNames()).Concat(storyWords))
        {
            string trimmed = phrase.Trim();
            if (trimmed.Length > 0) known.Add(trimmed);
            known.UnionWith(trimmed.Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries));
        }
        _knownWords = known;
        _cache.Clear();
    }

    /// <summary>Stops underlining this word until the app closes.</summary>
    public void Ignore(string word)
    {
        _ignored.Add(word);
        _cache.Clear();
    }

    /// <summary>Forget earlier results (call this when what lines mean changes, e.g. a keyword is renamed).</summary>
    public void ClearCache() => _cache.Clear();

    public IReadOnlyList<string> Suggest(string word) => _engine.Suggest(word);

    /// <summary>
    /// The spelling problems in one line. Results are remembered, so checking the same text again is free
    /// (the line is only parsed when it hasn't been seen before).
    /// </summary>
    public IReadOnlyList<SpellingIssue> CheckLine(string text, Func<string, ScriptLine> parse)
    {
        if (_cache.TryGetValue(text, out var known)) return known;

        var ranges = RangesToCheck(parse(text));
        IReadOnlyList<SpellingIssue> issues = ranges.Count == 0
            ? []
            : _engine.Check(MaskOutside(text, ranges))
                .Where(issue => issue.Range.End <= text.Length && !IsAcceptable(issue, issue.Range.Of(text)))
                .ToList();

        if (_cache.Count >= CacheLimit) _cache.Clear();
        _cache[text] = issues;
        return issues;
    }

    /// <summary>
    /// The parts of a line worth checking: the line's content, minus anything that only says how to format it
    /// ({tags}, "# ", the ** around bold). Names, labels, markers, sound effects and notes aren't included.
    /// </summary>
    public static IReadOnlyList<TextRange> RangesToCheck(ScriptLine parsed)
    {
        bool hasWords = parsed.Type is ElementType.Action or ElementType.Dialogue or ElementType.Caption
            or ElementType.Heading or ElementType.ListItem;
        if (!hasWords || parsed.Body.IsEmpty) return [];

        var ranges = new List<TextRange> { parsed.Body };
        foreach (var markup in parsed.Markup().Append(parsed.ModifierRange).Append(parsed.ListMarker))
            ranges = Subtract(ranges, markup);
        return ranges;
    }

    /// <summary>
    /// The line with everything outside the ranges blanked out with spaces. Positions stay the same, and
    /// the dictionary still sees each sentence whole (so it can spot "the the").
    /// </summary>
    private static string MaskOutside(string text, IReadOnlyList<TextRange> ranges)
    {
        var masked = new StringBuilder(new string(' ', text.Length));
        foreach (var range in ranges)
            for (int i = range.Start; i < range.End && i < text.Length; i++)
                masked[i] = text[i];
        return masked.ToString();
    }

    /// <summary>True for problems that aren't really problems in a script.</summary>
    private bool IsAcceptable(SpellingIssue issue, string word)
    {
        bool shouted = word.Count(char.IsLetter) >= 2 && !word.Any(char.IsLower); // AKIRA, NO WAY, KRAKOOM
        if (issue.Kind == SpellingIssueKind.RepeatedWord)
            return shouted || _ignored.Contains(word); // "NO NO NO!" is on purpose; "the the" isn't

        string bare = word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("’s", StringComparison.OrdinalIgnoreCase)
            ? word[..^2] // Akira's → Akira
            : word;
        if (shouted || bare.Any(char.IsDigit)) return true; // 2045, R2D2
        return _knownWords.Contains(bare) || _ignored.Contains(bare) || _knownWords.Contains(word) || _ignored.Contains(word);
    }

    /// <summary>The ranges with one range cut out of them.</summary>
    private static List<TextRange> Subtract(List<TextRange> ranges, TextRange cut)
    {
        if (cut.IsEmpty) return ranges;
        var result = new List<TextRange>();
        foreach (var range in ranges)
        {
            if (cut.End <= range.Start || cut.Start >= range.End)
            {
                result.Add(range);
                continue;
            }
            if (cut.Start > range.Start) result.Add(new TextRange(range.Start, cut.Start - range.Start));
            if (cut.End < range.End) result.Add(new TextRange(cut.End, range.End - cut.End));
        }
        return result;
    }
}
