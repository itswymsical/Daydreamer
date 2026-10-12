using System.Text.RegularExpressions;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Analysis;

/// <summary>
/// Finds names that are probably speakers but haven't been added as characters yet. Many scripts write
/// dialogue as a name in capitals and then the line, with no colon — "KAITO It's late." or
/// "**EMA** Come on." — which can't be told apart from an ordinary sentence until you know the name.
/// So a name only counts when it starts at least two lines, followed by more words.
/// </summary>
public static partial class SpeakerFinder
{
    // One to three words in capitals at the start of a line ("KAITO", "DR. TANAKA", "STUDENT 2"),
    // then an optional (modifier) and an optional colon, then the words that are said. "I" and "A"
    // are words rather than parts of a name, so "KAITO I said…" is KAITO speaking.
    [GeneratedRegex(@"^(?<name>\p{Lu}[\p{Lu}'’.\-]+(?: \d{1,3})?(?: (?![IA]\s)\p{Lu}[\p{Lu}'’.\-]*(?: \d{1,3})?){0,2})(?:\s*\([^)]*\))?\s*:?\s+(?<said>\S.*)$")]
    private static partial Regex CapsStartRegex();

    // A bold or italic label at the start of a line: "**EMA (low)** …" → EMA.
    [GeneratedRegex(@"^(?<open>\*\*|\*)(?<name>\p{Lu}[\p{Lu}\d'’.\- ]*?)\s*(?:\([^)]*\))?\k<open>\s*:?\s*\S")]
    private static partial Regex EmphasizedNameRegex();

    /// <summary>Likely speakers, most lines first. Names of defined characters and keywords are left out.</summary>
    public static List<(string Name, int Lines)> Find(IEnumerable<string> lines, ScriptParser parser) =>
        Find(lines.Select(raw => (raw, parser.ParseLine(raw))).ToList(), parser);

    /// <summary>The same, for lines that have already been parsed.</summary>
    public static List<(string Name, int Lines)> Find(IReadOnlyList<(string Raw, ScriptLine Line)> parsed, ScriptParser parser)
    {
        // First pass: how often each possible name (one, two or three words) starts a line.
        var starts = new Dictionary<string, int>();
        foreach (var (raw, line) in parsed)
        {
            foreach (string name in PossibleNames(raw, line, parser))
                starts[name] = starts.GetValueOrDefault(name) + 1;
        }

        // Second pass: each line votes for its longest name that comes up more than once, so
        // "TEACHER THOSE TWO WERE RIGHT!" counts for TEACHER rather than for "TEACHER THOSE TWO".
        var votes = new Dictionary<string, int>();
        var certain = new HashSet<string>();
        foreach (var (raw, line) in parsed)
        {
            string? name;
            if (line.Type == ElementType.Dialogue && line.Character == null && line.Speaker != null)
            {
                name = line.Speaker;
                certain.Add(name); // "BOB: hi" — the colon already makes it dialogue
            }
            else
                name = PossibleNames(raw, line, parser).Where(n => starts[n] >= 2).MaxBy(n => n.Length);

            if (name != null) votes[name] = votes.GetValueOrDefault(name) + 1;
        }

        var keywords = Keywords.Customizable.SelectMany(t => new[] { parser.Keywords.For(t), Keywords.DefaultFor(t) })
            .Append("FX")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return votes
            .Where(v => (v.Value >= 2 || certain.Contains(v.Key)) && v.Key.Count(char.IsLetter) >= 2)
            .Where(v => !keywords.Contains(v.Key) && parser.Registry.Find(v.Key) == null)
            .Select(v => (Name: v.Key, Lines: v.Value))
            .OrderByDescending(v => v.Lines)
            .ThenBy(v => v.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The names a line could start with: "STUDENT 2 This is…" → "STUDENT 2" (and longer runs of capitals).</summary>
    private static IEnumerable<string> PossibleNames(string raw, ScriptLine line, ScriptParser parser)
    {
        // Only plain description lines could be hiding dialogue.
        if (line.Type != ElementType.Action || line.IsForcedAction || !line.Tag.IsEmpty) yield break;
        string text = raw.Trim();

        var emphasized = EmphasizedNameRegex().Match(text);
        if (emphasized.Success)
        {
            // The bold already says where the name ends.
            yield return emphasized.Groups["name"].Value.Trim();
            yield break;
        }

        var match = CapsStartRegex().Match(text);
        if (!match.Success) yield break;
        // Something has to be said, not just more capitals: "DOUBLE SPREAD" is a layout note,
        // and so is anything with PAGE or PANEL in it ("FULL PAGE: …").
        string said = match.Groups["said"].Value;
        if (said.All(c => char.IsUpper(c) || c == ' ')) yield break;
        string[] words = SplitNameWords(match.Groups["name"].Value);
        string firstSaid = said.Split(' ')[0].TrimEnd(':', '.', ',');
        if (words.Append(firstSaid).Any(parser.IsLayoutWord)) yield break;
        for (int count = 1; count <= words.Length; count++)
            yield return string.Join(" ", words.Take(count));
    }

    /// <summary>"DR. TANAKA" → ["DR.", "TANAKA"]; a number stays with its word: "STUDENT 2" → ["STUDENT 2"].</summary>
    private static string[] SplitNameWords(string name)
    {
        var words = new List<string>();
        foreach (string part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (words.Count > 0 && part.All(char.IsDigit)) words[^1] += " " + part;
            else words.Add(part);
        }
        return [.. words];
    }
}
