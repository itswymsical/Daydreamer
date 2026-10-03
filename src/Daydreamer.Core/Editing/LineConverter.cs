using System.Text.RegularExpressions;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Editing;

/// <summary>Things a conversion needs to know about where the line sits.</summary>
/// <param name="NextPageNumber">The number a new PAGE here would get.</param>
/// <param name="NextPanelNumber">The number a new PANEL here would get.</param>
/// <param name="Speaker">Who's talking, when converting to dialogue.</param>
public sealed record LineContext(int NextPageNumber = 1, int NextPanelNumber = 1, string? Speaker = null);

/// <summary>
/// The line type menu ("Description ▾"). In a plain-text app a line's type comes from how it's written,
/// so changing the type rewrites the line in the new type's syntax — keeping its words:
/// <code>
///   to Caption:      CAPTION: words
///   to SFX:          SFX: words
///   to Heading:      # words
///   to Page/Panel:   PAGE 3: words
///   to Description:  words   (or !words if they'd otherwise read as something else)
/// </code>
/// </summary>
public static partial class LineConverter
{
    // "PAGE 3: The arrival" → "The arrival". Handles PAGES 4-5, Roman numerals, and a :, . or dash after the number.
    [GeneratedRegex(@"^\s*(?:pages?|panels?)\s*(?:[\divxlcdm]+(?:\s*[-–]\s*[\divxlcdm]+)?)?\s*[:.\-–]?\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MarkerLineRegex();

    /// <summary>Rewrites <paramref name="line"/> so it reads as <paramref name="target"/>.</summary>
    public static string ConvertTo(string line, ElementType target, ScriptParser parser, LineContext context)
    {
        var parsed = parser.ParseLine(line);
        if (parsed.Type == ElementType.Blank) return line;
        if (parsed.Type == target && target != ElementType.Dialogue) return line;

        string indent = LeadingWhitespace(line);
        bool fromDialogue = parsed.Type == ElementType.Dialogue;

        // A dialogue line converted to anything but a caption keeps its whole text: the usual reason
        // to convert is that something like "INT. STATION: night" was mistaken for a speaker.
        string words = fromDialogue ? line.Trim() : WordsOf(line, parsed);

        string converted = target switch
        {
            ElementType.Action => AsDescription(words, parser),
            ElementType.Heading => "# " + words,
            ElementType.Page => WithNumber("PAGE", context.NextPageNumber, words),
            ElementType.Panel => WithNumber("PANEL", context.NextPanelNumber, words),
            ElementType.Caption => fromDialogue && parsed.Speaker != null
                ? $"CAPTION ({parsed.Speaker}): {parsed.Body.Of(line)}"
                : "CAPTION: " + words,
            ElementType.Sfx => "SFX: " + words,
            ElementType.Note => "// " + words,
            ElementType.Dialogue => $"{context.Speaker ?? parsed.Speaker ?? "NAME"}: " + (fromDialogue ? parsed.Body.Of(line) : words),
            ElementType.ListItem => "- " + words,
            _ => line.Trim(),
        };
        return indent + converted;
    }

    /// <summary>The words of a line without its type syntax: "CAPTION: Tokyo" → "Tokyo", "- item" → "item".</summary>
    public static string WordsOf(string line, ScriptLine parsed) => parsed.Type switch
    {
        ElementType.Blank => "",
        ElementType.Page or ElementType.Panel => MarkerLineRegex().Match(line) is { Success: true } m ? m.Groups["rest"].Value.Trim() : line.Trim(),
        ElementType.Note when parsed.Label.IsEmpty => line.Trim().TrimStart('/').Trim(), // "// note"
        ElementType.Dialogue => line.Trim(),
        _ => parsed.Body.Of(line).Trim(),
    };

    /// <summary>Plain description text — with a leading ! if it would otherwise read as another type.</summary>
    public static string AsDescription(string words, ScriptParser parser)
    {
        if (words.Length == 0) return "";
        var type = parser.ParseLine(words).Type;
        return type is ElementType.Action or ElementType.Blank ? words : "!" + words;
    }

    private static string WithNumber(string marker, int number, string words) =>
        words.Length == 0 ? $"{marker} {number}" : $"{marker} {number}: {words}";

    public static string LeadingWhitespace(string line) => line[..(line.Length - line.TrimStart().Length)];
}
