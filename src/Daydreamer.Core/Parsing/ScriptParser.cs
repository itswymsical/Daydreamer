using System.Text.RegularExpressions;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Parsing;

/// <summary>
/// Turns one line of text into a <see cref="ScriptLine"/>. Each line is understood on its own
/// (no memory of the lines above it), which keeps the rules simple and the editor fast.
///
/// The rules, checked in order (keywords like PAGE and PANEL can be renamed per project):
/// <code>
///   {center} / {page} …           → the line's own settings (hidden in the editor), then the rules below
///   (empty)                       → Blank
///   // anything                   → Note
///   !anything                     → Action (forced: "read this as a description, whatever it looks like")
///   # Chapter 1                   → Heading
///   PAGE 1 / Page 2 / PAGES 4-5   → Page
///   PANEL 1 / Panel 2             → Panel
///   CAPTION: / SFX: / NOTE:       → Caption / Sfx / Note   (also "CAPTION 1 text" and "SFX BOOM" in capitals)
///   [ ] task / 1. item / - item   → ListItem
///   AKIRA: … / AKIRA (x): …       → Dialogue  (if AKIRA is a defined character, or written in ALL CAPS)
///   AKIRA … (no colon)            → Dialogue  (if AKIRA is a defined character written in capitals)
///   anything else                 → Action
/// </code>
/// </summary>
public sealed partial class ScriptParser
{
    [GeneratedRegex(@"^(\s*#+\s*)(.*)$")]
    private static partial Regex HeadingRegex();

    // "[ ] task", "[x] done", and the GitHub-style "- [ ] task".
    [GeneratedRegex(@"^\s*(?:[-*]\s+)?(?<marker>\[(?<check>[ xX])\])(?:\s+|$)(?<body>.*)$")]
    private static partial Regex ChecklistRegex();

    // "1. item" or "1) item" — the space after the dot matters, so "1.5 hours" stays prose.
    [GeneratedRegex(@"^\s*(?<marker>\d{1,3}[.)])\s+(?<body>.*)$")]
    private static partial Regex NumberedRegex();

    // "- item", "* item", "• item" — again a space must follow, so "*italic*" isn't a bullet.
    [GeneratedRegex(@"^\s*(?<marker>[-*•])\s+(?<body>.*)$")]
    private static partial Regex BulletRegex();

    // A speaker cue with a colon: a short name, an optional (modifier), then the colon.
    [GeneratedRegex(@"^\s*(?<name>[\p{L}\p{N}][\p{L}\p{N}\p{M} .'’\-]{0,40}?)\s*(?<modfull>\((?<mod>[^)]*)\))?\s*:\s*(?<body>.*)$")]
    private static partial Regex CueRegex();

    // After a speaker's name: an optional (modifier), then the words.
    [GeneratedRegex(@"\G(?<modfull>\s*\((?<mod>[^)]*)\))?\s+(?<body>.*)$")]
    private static partial Regex AfterNameRegex();

    // A line's own settings: "{center} ", "{page} ", "{caption, right} " (validated against known words below).
    [GeneratedRegex(@"^(?<indent>\s*)\{(?<words>[A-Za-z][A-Za-z ,]*)\}[ \t]?")]
    private static partial Regex TagRegex();

    // **bold** first, then *italic*. Markers must hug the text ("* not italic *" is left alone).
    [GeneratedRegex(@"\*\*(?=\S)(?<b>.+?)(?<=\S)\*\*|\*(?=\S)(?<i>[^*]+?)(?<=\S)\*")]
    private static partial Regex InlineRegex();

    // Bold or italic right at the start of a line: "**AKIRA** Hi.", "*SFX* boom".
    [GeneratedRegex(@"^\s*(?<open>\*\*|\*)(?=\S)(?<inner>[^*]+?)(?<=\S)(?<close>\k<open>)")]
    private static partial Regex LeadingEmphasisRegex();

    private static readonly Dictionary<string, TextAlign> AlignmentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = TextAlign.Left,
        ["center"] = TextAlign.Center,
        ["centre"] = TextAlign.Center,
        ["right"] = TextAlign.Right,
        ["justify"] = TextAlign.Justify,
    };

    /// <summary>The words a tag uses for each kind of line (the project's own keywords are accepted too).</summary>
    public static readonly IReadOnlyDictionary<ElementType, string> TagWords = new Dictionary<ElementType, string>
    {
        [ElementType.Heading] = "heading",
        [ElementType.Page] = "page",
        [ElementType.Panel] = "panel",
        [ElementType.Caption] = "caption",
        [ElementType.Sfx] = "sfx",
        [ElementType.Note] = "note",
        [ElementType.Action] = "text",
    };

    private Keywords _keywords = new();
    private KeywordRules _rules;

    public ScriptParser(CharacterRegistry? registry = null, Keywords? keywords = null)
    {
        Registry = registry ?? CharacterRegistry.Empty;
        _keywords = keywords ?? new Keywords();
        _rules = new KeywordRules(_keywords);
    }

    /// <summary>Swap this out whenever the character list changes.</summary>
    public CharacterRegistry Registry { get; set; }

    /// <summary>The project's keywords. Set it again after they change (it rebuilds the matching rules).</summary>
    public Keywords Keywords
    {
        get => _keywords;
        set
        {
            _keywords = value ?? new Keywords();
            _rules = new KeywordRules(_keywords);
        }
    }

    public ScriptLine ParseLine(string text)
    {
        var tag = MatchTag(text);
        if (tag == null) return ParseUntagged(text);

        int indent = IndentLevelOf(text, tag.Value.IndentLength);
        if (tag.Value.Type is { } type)
            return BuildTagged(text, tag.Value, type, indent);

        // An alignment-only tag: read the rest of the line normally, with the tag blanked out so every
        // position in the result still matches the real text.
        string masked = text[..tag.Value.Range.Start] + new string(' ', tag.Value.Range.Length) + text[tag.Value.Range.End..];
        var parsed = ParseUntagged(masked);
        return parsed with { Tag = tag.Value.Range, Alignment = tag.Value.Alignment, IndentLevel = indent };
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
        for (int i = 0; i < contentStart && i < text.Length; i++)
        {
            if (text[i] == '\t') tabs++;
            else spaces++;
        }
        return tabs + spaces / 4;
    }

    /// <summary>
    /// Takes a line apart for editing its tag: the indentation, the tag's line-type word and alignment
    /// (if any), and the rest of the text.
    /// </summary>
    public (string Indent, string? TypeWord, TextAlign? Alignment, string Text) SplitTag(string line)
    {
        var tag = MatchTag(line);
        if (tag == null)
        {
            int start = line.Length - line.TrimStart().Length;
            return (line[..start], null, null, line[start..]);
        }
        var t = tag.Value;
        return (line[..t.IndentLength], t.Type is { } type ? TagWords[type] : null, t.Alignment, line[t.Range.End..]);
    }

    /// <summary>True for the page and panel keywords ("PAGE", "PANELS", or "SCENE" if that's the project's word).</summary>
    public bool IsLayoutWord(string word) => _rules.IsPageOrPanelWord(word);

    /// <summary>"PAGE 3: The Arrival" → "The Arrival"; "SCENE 2 (Home)" → "(Home)". Text without a page/panel marker is returned as is.</summary>
    public string StripMarker(string text)
    {
        var match = _rules.MarkerPrefix.Match(text);
        return match.Success ? match.Groups["rest"].Value : text;
    }

    /// <summary>Writes a line back with its tag (or without one, if there's nothing to say).</summary>
    public static string JoinTag(string indent, string? typeWord, TextAlign? alignment, string rest)
    {
        var words = new List<string>();
        if (typeWord != null) words.Add(typeWord);
        if (alignment != null) words.Add(alignment.Value.ToString().ToLowerInvariant());
        return words.Count == 0 ? indent + rest : $"{indent}{{{string.Join(" ", words)}}} {rest}";
    }

    // ── The rules ───────────────────────────────────────────────────────────

    private ScriptLine ParseUntagged(string text)
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

        // A label in bold or italic still counts: "**AKIRA** Hi.", "**SFX** boom", "**PAGE 2**".
        if (trimmed[0] == '*' && ParseWithEmphasizedLabel(text, indent) is { } emphasized)
            return emphasized;

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

        if (_rules.Page.IsMatch(text))
            return new ScriptLine { Type = ElementType.Page, Body = wholeLine, IndentLevel = indent };

        if (_rules.Panel.IsMatch(text))
            return new ScriptLine { Type = ElementType.Panel, Body = wholeLine, IndentLevel = indent };

        var keyword = _rules.KeywordWithColon.Match(text);
        if (!keyword.Success) keyword = _rules.KeywordInCaps.Match(text);
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
            {
                int colon = text.LastIndexOf(':', cue.Groups["body"].Index > 0 ? cue.Groups["body"].Index - 1 : 0);
                return BuildLabelledLine(text, cue, ElementType.Dialogue, name, character, indent, labelEnd: colon + 1);
            }
        }

        // A defined character's name in capitals, then the line, with no colon: "KAITO It's late."
        if (Registry.FindCapsName(text, contentStart) is { } speaker)
        {
            var after = AfterNameRegex().Match(text, contentStart + speaker.Length);
            if (after.Success && after.Groups["body"].Length > 0)
            {
                int labelEnd = after.Groups["modfull"].Success ? after.Groups["modfull"].Index + after.Groups["modfull"].Length : contentStart + speaker.Length;
                return BuildLabelledLine(text, after, ElementType.Dialogue, text.Substring(contentStart, speaker.Length), speaker.Character, indent, labelEnd);
            }
        }

        return new ScriptLine { Type = ElementType.Action, Body = wholeLine, Inlines = ParseInlines(text, wholeLine), IndentLevel = indent };
    }

    /// <summary>
    /// Reads a line that starts in bold or italic with the ** or * blanked out. If what's inside turns out
    /// to be a label ("**AKIRA**", "**EMA (low)**", "**SFX**") the markers are kept as markup to hide;
    /// a page or panel keeps its emphasis as ordinary bold. Anything else isn't a label: returns null.
    /// </summary>
    private ScriptLine? ParseWithEmphasizedLabel(string text, int indent)
    {
        var match = LeadingEmphasisRegex().Match(text);
        if (!match.Success) return null;

        var open = RangeOf(match.Groups["open"]);
        var inner = RangeOf(match.Groups["inner"]);
        var close = RangeOf(match.Groups["close"]);
        string masked = text[..open.Start] + new string(' ', open.Length) + text[open.End..close.Start] + new string(' ', close.Length) + text[close.End..];
        var line = ParseUntagged(masked) with { IndentLevel = indent };

        switch (line.Type)
        {
            case ElementType.Page or ElementType.Panel:
                var whole = new TextRange(open.Start, text.TrimEnd().Length - open.Start);
                return line with { Body = whole, Inlines = ParseInlines(text, whole) };

            case ElementType.Dialogue or ElementType.Caption or ElementType.Sfx or ElementType.Note
                when !line.Label.IsEmpty && inner.Start >= line.Label.Start && inner.End <= line.Label.End:
                var label = new TextRange(open.Start, Math.Max(line.Label.End, close.End) - open.Start);
                return line with { Label = label, LabelMarkers = [open, close] };

            default:
                return null;
        }
    }

    /// <summary>A line whose tag names its type: "{page} The Arrival" → a Page line reading "The Arrival".</summary>
    private static ScriptLine BuildTagged(string text, (TextRange Range, int IndentLength, ElementType? Type, TextAlign? Alignment) tag, ElementType type, int indent)
    {
        int bodyStart = tag.Range.End;
        while (bodyStart < text.Length && char.IsWhiteSpace(text[bodyStart])) bodyStart++;
        var body = new TextRange(bodyStart, Math.Max(0, text.TrimEnd().Length - bodyStart));
        return new ScriptLine
        {
            Type = type,
            Label = tag.Range,
            Tag = tag.Range,
            Alignment = tag.Alignment,
            Body = body,
            Inlines = ParseInlines(text, body),
            IndentLevel = indent,
        };
    }

    private (TextRange Range, int IndentLength, ElementType? Type, TextAlign? Alignment)? MatchTag(string text)
    {
        if (text.IndexOf('{') < 0) return null;
        var match = TagRegex().Match(text);
        if (!match.Success) return null;

        ElementType? type = null;
        TextAlign? alignment = null;
        foreach (string word in match.Groups["words"].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (AlignmentWords.TryGetValue(word, out var a) && alignment == null) alignment = a;
            else if (_rules.TagTypes.TryGetValue(word, out var t) && type == null) type = t;
            else return null; // something like "{sic}" is just text
        }
        int indentLength = match.Groups["indent"].Length;
        return (new TextRange(indentLength, match.Length - indentLength), indentLength, type, alignment);
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
        var type = _rules.KeywordTypes[match.Groups["kw"].Value];

        // CAPTION (AKIRA): ... — a caption narrated by a character picks up their font.
        Character? narrator = null;
        string modifier = match.Groups["mod"].Value.Trim();
        if (type == ElementType.Caption && modifier.Length > 0)
            narrator = Registry.Find(modifier);

        // The label is everything before the words: "CAPTION 1", "SFX:", "CAPTION (AKIRA):".
        int bodyStart = match.Groups["body"].Index;
        int labelEnd = bodyStart;
        while (labelEnd > 0 && char.IsWhiteSpace(text[labelEnd - 1])) labelEnd--;
        return BuildLabelledLine(text, match, type, narrator?.Name, narrator, indent, labelEnd);
    }

    private static ScriptLine BuildLabelledLine(string text, Match match, ElementType type, string? speaker, Character? character, int indent, int labelEnd)
    {
        var body = RangeOf(match.Groups["body"]);
        int labelStart = text.Length - text.TrimStart().Length;
        var modGroup = match.Groups["mod"];
        return new ScriptLine
        {
            Type = type,
            Character = character,
            Speaker = speaker,
            Modifier = modGroup.Success ? modGroup.Value.Trim() : null,
            Label = new TextRange(labelStart, labelEnd - labelStart),
            ModifierRange = match.Groups["modfull"].Success ? TrimmedRange(text, match.Groups["modfull"]) : TextRange.Empty,
            Body = body,
            Inlines = ParseInlines(text, body),
            IndentLevel = indent,
        };
    }

    /// <summary>
    /// An undefined name only counts as a speaker (with a colon) if it's written like a script cue:
    /// ALL CAPS, at most four words, with at least one letter. "Time: 5pm" stays prose; "BOB: hi" is dialogue.
    /// Layout notes like "FULL PAGE:" or "DOUBLE PANEL:" aren't speakers either.
    /// </summary>
    private bool LooksLikeSpeakerName(string name)
    {
        if (!name.Any(char.IsLetter)) return false;
        if (name != name.ToUpperInvariant()) return false;
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 4 && !words.Any(_rules.IsPageOrPanelWord);
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

    // "(whisper)" without the spaces in front of it.
    private static TextRange TrimmedRange(string text, Group group)
    {
        int start = group.Index;
        while (start < group.Index + group.Length && char.IsWhiteSpace(text[start])) start++;
        return new TextRange(start, group.Index + group.Length - start);
    }

    /// <summary>The keyword-dependent patterns, rebuilt whenever the project's keywords change.</summary>
    private sealed class KeywordRules
    {
        // Compiled: these run for every line the editor draws, and are only rebuilt when a keyword changes.
        private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

        public KeywordRules(Keywords keywords)
        {
            Page = MarkerRegex(Words(keywords, ElementType.Page));
            Panel = MarkerRegex(Words(keywords, ElementType.Panel));

            KeywordTypes = new Dictionary<string, ElementType>(StringComparer.OrdinalIgnoreCase) { ["FX"] = ElementType.Sfx };
            foreach (var type in new[] { ElementType.Caption, ElementType.Sfx, ElementType.Note })
            {
                foreach (string word in Words(keywords, type))
                    KeywordTypes[word] = type;
            }
            string alternatives = string.Join("|", KeywordTypes.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

            // "CAPTION: words", "caption 2: words", "CAPTION (AKIRA): words" — any capitalisation, with a colon…
            KeywordWithColon = new Regex(
                $@"^\s*(?<kw>{alternatives})(?:\s*(?<num>\d+))?\s*(?<modfull>\((?<mod>[^)]*)\))?\s*:\s*(?<body>.*)$",
                Options | RegexOptions.IgnoreCase);
            // …or in capitals without one: "SFX CLING!", "CAPTION 1 “Everyone knows…”".
            KeywordInCaps = new Regex(
                $@"^\s*(?<kw>{alternatives})(?:\s+(?<num>\d+))?(?<modfull>\s*\((?<mod>[^)]*)\))?\s+(?<body>\S.*)$",
                Options);

            TagTypes = new Dictionary<string, ElementType>(StringComparer.OrdinalIgnoreCase);
            foreach (var (type, word) in TagWords) TagTypes[word] = type;
            foreach (var type in Keywords.Customizable) TagTypes.TryAdd(keywords.For(type).ToLowerInvariant(), type);

            _pageAndPanelWords = new HashSet<string>(Words(keywords, ElementType.Page).Concat(Words(keywords, ElementType.Panel)), StringComparer.OrdinalIgnoreCase);
            string markers = string.Join("|", _pageAndPanelWords.Select(Regex.Escape));
            MarkerPrefix = new Regex(
                $@"^\s*(?:{markers})s?\s*(?:[\divxlcdm]+(?:\s*[-–]\s*[\divxlcdm]+)?)?\s*[:.\-–]?\s*(?<rest>.*)$",
                Options | RegexOptions.IgnoreCase);
        }

        public Regex Page { get; }
        public Regex Panel { get; }
        public Regex MarkerPrefix { get; }
        public Regex KeywordWithColon { get; }
        public Regex KeywordInCaps { get; }
        public Dictionary<string, ElementType> KeywordTypes { get; }
        public Dictionary<string, ElementType> TagTypes { get; }

        private readonly HashSet<string> _pageAndPanelWords;

        /// <summary>"PAGE", "PANELS", "SCENE" (when that's the panel keyword) and so on.</summary>
        public bool IsPageOrPanelWord(string word) =>
            _pageAndPanelWords.Contains(word) || word.EndsWith('S') && _pageAndPanelWords.Contains(word[..^1]);

        /// <summary>The project's word plus the built-in one, e.g. SCENE and PANEL.</summary>
        private static IEnumerable<string> Words(Keywords keywords, ElementType type) =>
            new[] { keywords.For(type), Keywords.DefaultFor(type) }.Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// "PAGE" in capitals always counts. Lowercase "page" only counts when followed by a number,
        /// so a prose sentence like "Page after page of notes..." isn't mistaken for a page marker.
        /// </summary>
        private static Regex MarkerRegex(IEnumerable<string> words)
        {
            string alternatives = string.Join("|", words.Select(Regex.Escape));
            return new Regex($@"^\s*(?:(?:{alternatives})S?\b|(?i:(?:{alternatives})s?)\s+\d)", Options);
        }
    }
}
