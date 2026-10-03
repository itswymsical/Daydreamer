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
///   !anything                    → Action (forced: "read this as a description, whatever it looks like")
///   # Chapter 1                  → Heading
///   PAGE 1 / Page 2 / PAGES 4-5  → Page
///   PANEL 1 / Panel 2            → Panel
///   CAPTION: / SFX: / FX: / NOTE:→ Caption / Sfx / Note
///   [ ] task / [x] done          → ListItem (checklist)
///   1. item                      → ListItem (numbered)
///   - item / * item / • item     → ListItem (bulleted)
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

    // "[ ] task", "[x] done", and the GitHub-style "- [ ] task".
    [GeneratedRegex(@"^\s*(?:[-*]\s+)?(?<marker>\[(?<check>[ xX])\])(?:\s+|$)(?<body>.*)$")]
    private static partial Regex ChecklistRegex();

    // "1. item" or "1) item" — the space after the dot matters, so "1.5 hours" stays prose.
    [GeneratedRegex(@"^\s*(?<marker>\d{1,3}[.)])\s+(?<body>.*)$")]
    private static partial Regex NumberedRegex();

    // "- item", "* item", "• item" — again a space must follow, so "*italic*" isn't a bullet.
    [GeneratedRegex(@"^\s*(?<marker>[-*•])\s+(?<body>.*)$")]
    private static partial Regex BulletRegex();

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
        int indent = IndentLevelOf(text, contentStart);
        var wholeLine = new TextRange(contentStart, trimmed.Length);

        if (trimmed.StartsWith("//"))
            return new ScriptLine { Type = ElementType.Note, Body = wholeLine, IndentLevel = indent };

        if (trimmed.StartsWith('!'))
        {
            int bodyStart = contentStart + 1;
            var body = new TextRange(bodyStart, text.TrimEnd().Length - bodyStart);
            return new ScriptLine
            {
                Type = ElementType.Action,
                Label = new TextRange(contentStart, 1),
                Body = body,
                Inlines = ParseInlines(text, body),
                IndentLevel = indent,
            };
        }

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
                IndentLevel = indent,
            };
        }

        if (PageRegex().IsMatch(text))
            return new ScriptLine { Type = ElementType.Page, Body = wholeLine, IndentLevel = indent };

        if (PanelRegex().IsMatch(text))
            return new ScriptLine { Type = ElementType.Panel, Body = wholeLine, IndentLevel = indent };

        var keyword = KeywordRegex().Match(text);
        if (keyword.Success)
            return BuildKeywordLine(text, keyword, indent);

        var list = MatchList(text);
        if (list != null)
            return BuildListLine(text, list.Value.Match, list.Value.Kind, indent);

        var cue = CueRegex().Match(text);
        if (cue.Success)
        {
            string name = cue.Groups["name"].Value.Trim();
            var character = Registry.Find(name);
            if (character != null || LooksLikeSpeakerName(name))
                return BuildLabelledLine(text, cue, ElementType.Dialogue, name, character, indent);
        }

        return new ScriptLine { Type = ElementType.Action, Body = wholeLine, Inlines = ParseInlines(text, wholeLine), IndentLevel = indent };
    }

    /// <summary>Parses a whole document, one entry per line.</summary>
    public IEnumerable<ScriptLine> ParseDocument(string text)
    {
        foreach (var line in SplitLines(text))
            yield return ParseLine(line);
    }

    public static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r'));

    /// <summary>One level per tab, or per four spaces, before the first visible character.</summary>
    public static int IndentLevelOf(string text, int contentStart)
    {
        int tabs = 0, spaces = 0;
        for (int i = 0; i < contentStart; i++)
        {
            if (text[i] == '\t') tabs++;
            else spaces++;
        }
        return tabs + spaces / 4;
    }

    private static (Match Match, ListKind Kind)? MatchList(string text)
    {
        var check = ChecklistRegex().Match(text);
        if (check.Success) return (check, ListKind.Checkbox);
        var number = NumberedRegex().Match(text);
        if (number.Success) return (number, ListKind.Numbered);
        var bullet = BulletRegex().Match(text);
        if (bullet.Success) return (bullet, ListKind.Bullet);
        return null;
    }

    private static ScriptLine BuildListLine(string text, Match match, ListKind kind, int indent)
    {
        var body = RangeOf(match.Groups["body"]);
        return new ScriptLine
        {
            Type = ElementType.ListItem,
            List = kind,
            ListMarker = RangeOf(match.Groups["marker"]),
            IsChecked = kind == ListKind.Checkbox && match.Groups["check"].Value is "x" or "X",
            Body = body,
            Inlines = ParseInlines(text, body),
            IndentLevel = indent,
        };
    }

    private ScriptLine BuildKeywordLine(string text, Match match, int indent)
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

        return BuildLabelledLine(text, match, type, speaker: narrator?.Name, narrator, indent);
    }

    private static ScriptLine BuildLabelledLine(string text, Match match, ElementType type, string? speaker, Character? character, int indent)
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
            IndentLevel = indent,
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
