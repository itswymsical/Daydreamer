using System.Text.RegularExpressions;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Analysis;

/// <summary>Counts for the status bar and the character panel.</summary>
public sealed partial class DocumentStats
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    public int Words { get; private init; }
    public int Pages { get; private init; }
    public int Panels { get; private init; }
    public int DialogueLines { get; private init; }

    /// <summary>Dialogue lines per defined character.</summary>
    public IReadOnlyDictionary<Character, int> LinesByCharacter { get; private init; } = new Dictionary<Character, int>();

    /// <summary>Names that look like speakers but haven't been added as characters yet, most lines first.</summary>
    public IReadOnlyList<string> UnknownSpeakers { get; private init; } = [];

    public static DocumentStats Analyze(string text, ScriptParser parser)
    {
        int words = 0, pages = 0, panels = 0, dialogue = 0;
        var byCharacter = new Dictionary<Character, int>();

        // Parsed once, and shared with the speaker finder.
        var parsed = ScriptParser.SplitLines(text).Select(raw => (Raw: raw, Line: parser.ParseLine(raw))).ToList();
        foreach (var (raw, line) in parsed)
        {
            switch (line.Type)
            {
                case ElementType.Blank or ElementType.Note:
                    continue; // notes are for you, not the reader, so they don't count
                case ElementType.Page:
                    pages++;
                    break;
                case ElementType.Panel:
                    panels++;
                    break;
                case ElementType.Dialogue:
                    dialogue++;
                    if (line.Character != null)
                        byCharacter[line.Character] = byCharacter.GetValueOrDefault(line.Character) + 1;
                    break;
            }

            if (line.Type is not (ElementType.Page or ElementType.Panel))
                words += CountWords(line.Body.Of(raw));
        }

        return new DocumentStats
        {
            Words = words,
            Pages = pages,
            Panels = panels,
            DialogueLines = dialogue,
            LinesByCharacter = byCharacter,
            UnknownSpeakers = [.. SpeakerFinder.Find(parsed, parser).Select(s => s.Name)],
        };
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));

    /// <summary>The number to use for a new PAGE inserted after the given lines.</summary>
    public static int NextPageNumber(IEnumerable<string> linesBefore, ScriptParser parser)
    {
        int last = 0;
        foreach (var raw in linesBefore)
        {
            if (parser.ParseLine(raw).Type == ElementType.Page)
                last = LastNumber(raw) ?? last + 1; // "PAGES 4-5" → next is 6
        }
        return last + 1;
    }

    /// <summary>The number to use for a new PANEL: panels restart at 1 on every page.</summary>
    public static int NextPanelNumber(IEnumerable<string> linesBefore, ScriptParser parser)
    {
        int last = 0;
        foreach (var raw in linesBefore)
        {
            var type = parser.ParseLine(raw).Type;
            if (type == ElementType.Page) last = 0;
            else if (type == ElementType.Panel) last = LastNumber(raw) ?? last + 1;
        }
        return last + 1;
    }

    private static int? LastNumber(string text)
    {
        var matches = NumberRegex().Matches(text);
        return matches.Count > 0 && int.TryParse(matches[^1].Value, out int n) ? n : null;
    }
}
