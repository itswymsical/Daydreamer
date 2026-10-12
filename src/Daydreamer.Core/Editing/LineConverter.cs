using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Editing;

/// <summary>Things a conversion needs to know about where the line sits.</summary>
/// <param name="Speaker">Who's talking, when converting to dialogue.</param>
/// <param name="SpeakerSeparator">What goes between a speaker and their words: ": " or just " ".</param>
public sealed record LineContext(string? Speaker = null, string SpeakerSeparator = ": ");

/// <summary>
/// The line type menu ("Description ▾"). Changing a line's type keeps its words and records the new type
/// in a small tag at the start of the line — <c>{page}</c>, <c>{caption}</c> — which the editor hides,
/// so you just see the words in the new style. Words that already read as that type (like "SCENE 3"
/// when SCENE is the panel keyword) don't need a tag at all.
/// </summary>
public static class LineConverter
{
    /// <summary>Rewrites <paramref name="line"/> so it reads as <paramref name="target"/>.</summary>
    public static string ConvertTo(string line, ElementType target, ScriptParser parser, LineContext context)
    {
        var parsed = parser.ParseLine(line);
        if (parsed.Type == ElementType.Blank) return line;
        if (parsed.Type == target && target != ElementType.Dialogue) return line;

        var (indent, _, alignment, rest) = parser.SplitTag(line); // a line keeps its own alignment
        bool fromDialogue = parsed.Type == ElementType.Dialogue;

        // A dialogue line converted to anything but a caption keeps its whole text: the usual reason to
        // convert is that something like "INT. STATION: night" was mistaken for a speaker. So does a
        // page turned into a panel (or back): "PANEL 4" is all there is to it.
        bool layoutSwap = parsed.Type is ElementType.Page or ElementType.Panel && target is ElementType.Page or ElementType.Panel;
        string words = fromDialogue || layoutSwap ? rest.Trim() : WordsOf(line, parsed, parser);

        return target switch
        {
            ElementType.Action => Description(indent, alignment, words, parser),
            ElementType.Page or ElementType.Panel when parser.ParseLine(words).Type == target =>
                ScriptParser.JoinTag(indent, null, alignment, words),
            ElementType.Caption when fromDialogue && parsed.Speaker != null =>
                ScriptParser.JoinTag(indent, null, alignment, $"{parser.Keywords.Caption} ({parsed.Speaker}): {parsed.Body.Of(line)}"),
            ElementType.Heading or ElementType.Page or ElementType.Panel or ElementType.Caption or ElementType.Sfx or ElementType.Note =>
                ScriptParser.JoinTag(indent, ScriptParser.TagWords[target], alignment, words),
            ElementType.Dialogue => ScriptParser.JoinTag(indent, null, alignment,
                (context.Speaker ?? parsed.Speaker ?? "NAME") + context.SpeakerSeparator + (fromDialogue ? parsed.Body.Of(line) : words)),
            ElementType.ListItem => ScriptParser.JoinTag(indent, null, alignment, "- " + words),
            _ => line,
        };
    }

    /// <summary>The words of a line without its type syntax: "CAPTION: Tokyo" → "Tokyo", "- item" → "item".</summary>
    public static string WordsOf(string line, ScriptLine parsed, ScriptParser parser)
    {
        string body = parsed.Body.Of(line).Trim();
        switch (parsed.Type)
        {
            case ElementType.Blank:
                return "";
            // "PAGE 3: The Arrival" → "The Arrival". A bare "PANEL 4" keeps its words rather than vanishing.
            case ElementType.Page or ElementType.Panel when parsed.Label.IsEmpty:
                string stripped = parser.StripMarker(body).Trim();
                return stripped.Length > 0 ? stripped : body;
            case ElementType.Note when parsed.Label.IsEmpty:
                return body.TrimStart('/').Trim(); // "// note"
            case ElementType.Dialogue:
                return parser.SplitTag(line).Text.Trim();
            default:
                return body;
        }
    }

    /// <summary>Plain description text — tagged {text} if it would otherwise read as another type.</summary>
    public static string AsDescription(string words, ScriptParser parser) => Description("", null, words, parser);

    private static string Description(string indent, TextAlign? alignment, string words, ScriptParser parser)
    {
        if (words.Length == 0 || parser.ParseLine(words).Type is ElementType.Action or ElementType.Blank)
            return ScriptParser.JoinTag(indent, null, alignment, words);
        return ScriptParser.JoinTag(indent, ScriptParser.TagWords[ElementType.Action], alignment, words);
    }

    public static string LeadingWhitespace(string line) => line[..(line.Length - line.TrimStart().Length)];
}
