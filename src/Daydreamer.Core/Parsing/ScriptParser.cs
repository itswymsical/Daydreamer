using System.Text.RegularExpressions;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>
/// Turns one line of text into a <see cref="ScriptLine"/>. Each line is understood on its own
/// (no memory of the lines above it), which keeps the rules simple and the editor fast.
///
/// The rules, checked in order:
/// <code>
///   (empty)                      → Blank
///   // anything                  → Note
///   # Chapter 1                  → Heading
///   PAGE 1 / Page 2 / PAGES 4-5  → Page
///   PANEL 1 / Panel 2            → Panel
///   CAPTION: / SFX: / FX: / NOTE:→ Caption / Sfx / Note
///   AKIRA: ... / AKIRA (x): ...  → Dialogue  (if AKIRA is a defined character,
///                                             or written in ALL CAPS)
///   anything else                → Action
/// </code>
/// </summary>
public sealed partial class ScriptParser
{
    // "PAGE" in capitals always counts. Lowercase "page" only counts when followed by a number,
    // so a prose sentence like "Page after page of notes..." isn't mistaken for a page marker.
    [GeneratedRegex(@"^\s*(?:PAGES?\b|(?i:pages?)\s+\d)")]
    private static partial Regex PageRegex();

    [GeneratedRegex(@"^\s*(?:PANELS?\b|(?i:panels?)\s+\d)")]
    private static partial Regex PanelRegex();

    [GeneratedRegex(@"^(\s*#+\s*)(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*(?<kw>CAPTION|SFX|FX|NOTE)\s*(?<modfull>\((?<mod>[^)]*)\))?\s*:\s*(?<body>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordRegex();

    // A speaker cue: a short name, an optional (modifier), then a colon.
    [GeneratedRegex(@"^\s*(?<name>[\p{L}\p{N}][\p{L}\p{N}\p{M} .'’\-]{0,40}?)\s*(?<modfull>\((?<mod>[^)]*)\))?\s*:\s*(?<body>.*)$")]
    private static partial Regex CueRegex();

    // **bold** first, then *italic*. Markers must hug the text ("* not italic *" is left alone).
    [GeneratedRegex(@"\*\*(?=\S)(?<b>.+?)(?<=\S)\*\*|\*(?=\S)(?<i>[^*]+?)(?<=\S)\*")]
    private static partial Regex InlineRegex();

    public ScriptParser(CharacterRegistry? registry = null)
    {
        Registry = registry ?? CharacterRegistry.Empty;
    }

    /// <summary>Swap this out whenever the character list changes.</summary>
    public CharacterRegistry Registry { get; set; }

    public ScriptLine ParseLine(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return new ScriptLine { Type = ElementType.Blank };

        int contentStart = text.Length - text.TrimStart().Length;
        var wholeLine = new TextRange(contentStart, trimmed.Length);

        if (trimmed.StartsWith("//"))
            return new ScriptLine { Type = ElementType.Note, Body = wholeLine };

        var heading = HeadingRegex().Match(text);
        if (heading.Success)
        {
            var body = RangeOf(heading.Groups[2]);
            return new ScriptLine
            {
                Type = ElementType.Heading,
                Label = RangeOf(heading.Groups[1]),
                Body = body,
                Inlines = ParseInlines(text, body),
            };
        }

        if (PageRegex().IsMatch(text))
            return new ScriptLine { Type = ElementType.Page, Body = wholeLine };

        if (PanelRegex().IsMatch(text))
            return new ScriptLine { Type = ElementType.Panel, Body = wholeLine };

        var keyword = KeywordRegex().Match(text);
        if (keyword.Success)
            return BuildKeywordLine(text, keyword);

        var cue = CueRegex().Match(text);
        if (cue.Success)
        {
            string name = cue.Groups["name"].Value.Trim();
            var character = Registry.Find(name);
            if (character != null || LooksLikeSpeakerName(name))
                return BuildLabelledLine(text, cue, ElementType.Dialogue, name, character);
        }

        return new ScriptLine { Type = ElementType.Action, Body = wholeLine, Inlines = ParseInlines(text, wholeLine) };
    }

    /// <summary>Parses a whole document, one entry per line.</summary>
    public IEnumerable<ScriptLine> ParseDocument(string text)
    {
        foreach (var line in SplitLines(text))
            yield return ParseLine(line);
    }

    public static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r'));

    private ScriptLine BuildKeywordLine(string text, Match match)
    {
        var type = match.Groups["kw"].Value.ToUpperInvariant() switch
        {
            "CAPTION" => ElementType.Caption,
            "NOTE" => ElementType.Note,
            _ => ElementType.Sfx,
        };

        // CAPTION (AKIRA): ... — a caption narrated by a character picks up their font.
        Character? narrator = null;
        string modifier = match.Groups["mod"].Value.Trim();
        if (type == ElementType.Caption && modifier.Length > 0)
            narrator = Registry.Find(modifier);

        return BuildLabelledLine(text, match, type, speaker: narrator?.Name, narrator);
    }

    private static ScriptLine BuildLabelledLine(string text, Match match, ElementType type, string? speaker, Character? character)
    {
        var body = RangeOf(match.Groups["body"]);
        int labelStart = text.Length - text.TrimStart().Length;
        // The label runs from the name up to and including the colon.
        int colonIndex = text.LastIndexOf(':', body.Start > 0 ? body.Start - 1 : 0);
        var label = new TextRange(labelStart, colonIndex + 1 - labelStart);

        var modGroup = match.Groups["mod"];
        return new ScriptLine
        {
            Type = type,
            Character = character,
            Speaker = speaker,
            Modifier = modGroup.Success ? modGroup.Value.Trim() : null,
            Label = label,
            ModifierRange = match.Groups["modfull"].Success ? RangeOf(match.Groups["modfull"]) : TextRange.Empty,
            Body = body,
            Inlines = ParseInlines(text, body),
        };
    }

    /// <summary>
    /// An undefined name only counts as a speaker if it's written like a script cue:
    /// ALL CAPS, at most four words, with at least one letter. "Time: 5pm" stays prose; "BOB: hi" is dialogue.
    /// </summary>
    private static bool LooksLikeSpeakerName(string name)
    {
        if (!name.Any(char.IsLetter)) return false;
        if (name != name.ToUpperInvariant()) return false;
        return name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;
    }

    private static List<InlineSpan> ParseInlines(string text, TextRange within)
    {
        var spans = new List<InlineSpan>();
        if (within.IsEmpty) return spans;

        foreach (Match m in InlineRegex().Matches(text, within.Start))
        {
            if (m.Index + m.Length > within.End) break;
            bool bold = m.Groups["b"].Success;
            int markerLength = bold ? 2 : 1;
            var content = RangeOf(bold ? m.Groups["b"] : m.Groups["i"]);
            spans.Add(new InlineSpan(
                bold ? InlineKind.Bold : InlineKind.Italic,
                new TextRange(m.Index, markerLength),
                content,
                new TextRange(content.End, markerLength)));
        }
        return spans;
    }

    private static TextRange RangeOf(Group group) => new(group.Index, group.Length);
}
