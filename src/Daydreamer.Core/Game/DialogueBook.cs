using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Daydreamer.Core.Editing;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Game;

/// <summary>A choice under a line of dialogue: what the player reads, and what picking it does.</summary>
public sealed class DialogueChoice
{
    public string Text { get; set; } = "";

    /// <summary>The conversation it leads to, in full: "Silvian.Greeting_True". Null if it leads nowhere.</summary>
    public string? Branch { get; set; }

    /// <summary>The answer that means "no": it discards what the conversation was going to do when it ended.</summary>
    public bool Dead { get; set; }

    /// <summary>C# to run when it's picked.</summary>
    public string? OnSelected { get; set; }
}

/// <summary>One line of a conversation, in the form the game reads.</summary>
public sealed class DialogueLine
{
    public string Speaker { get; set; } = "";
    public int Emote { get; set; }
    public double Speed { get; set; }

    /// <summary>The words, with effects reduced to the game's anchors: <c>Well,[] howdy!</c></summary>
    public string Text { get; set; } = "";

    /// <summary>The C# for each anchor, in order: <c>new Pause(16)</c></summary>
    public List<string> Affects { get; } = [];

    public List<DialogueChoice> Choices { get; } = [];

    /// <summary>The job this line's two choices hand in ("here you go" / "not yet"). See the game's <c>Dialogue.HandIn</c>.</summary>
    public string? HandIn { get; set; }

    /// <summary>Notes that were written just above this line.</summary>
    public List<string> Comments { get; } = [];
}

/// <summary>A conversation: a <c># heading</c> in the document, a named sequence in the game.</summary>
public sealed class DialogueConversation
{
    public string Name { get; set; } = "";
    public List<string> Comments { get; } = [];
    public List<DialogueLine> Lines { get; } = [];

    /// <summary>What happens once the conversation has played through, as C# chained onto the definition.</summary>
    public List<string> Ending { get; } = [];

    /// <summary>
    /// Its flow stays in the mod's own C# (<c>// hand-written</c>): only the words are exported.
    /// </summary>
    public bool HandWritten { get; set; }

    /// <summary>The document line of its heading.</summary>
    public int SourceLine { get; set; }
}

/// <summary>Something in a document that won't export the way it reads.</summary>
public sealed record DialogueIssue(int Line, string Message);

/// <summary>
/// A character's dialogue document, understood. Every document is one character, and it holds their whole script:
/// <code>
///   // speed: 1.1                                the character's profile, at the top (all of it optional)
///   // tone worried: emote 3, speed 0.9
///
///   # Greeting                                   a conversation
///   SILVIAN: Well,[] howdy![30] Name's Silvian.  a line (or just the words, for the document's own character)
///   SILVIAN (worried): You [shake: alright]?     a tone for the whole line, an effect on some of its words
///   - I'm fine. -> Greeting_True                 a choice, leading to another conversation
///   - Who's asking?                              a choice whose response is written right under it…
///       Just a neighbor.                         …indented. It gets a conversation of its own automatically
///   - ... -> Greeting_Silent (dead)              (dead) means the game's "dead" answer
///   // unlock: HerbalForager                     what happens at the end of the conversation
/// </code>
/// </summary>
public sealed partial class DialogueBook
{
    /// <summary>How fast a line types unless it says otherwise. Most of Trelamium's lines use this.</summary>
    public const double DefaultSpeed = 1.1;

    // Notes that are instructions rather than notes. The first group is only for the top of the document.
    [GeneratedRegex(@"^(?<word>speaker|key|speed|pauses|using|color|tone\s+[\p{L}\p{N} _\-]+|unlock|handin|complete)\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex(@"^hand-?written\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex HandWrittenRegex();

    // "(worried, speed 0.9) Words." at the start of a line with no speaker's name.
    [GeneratedRegex(@"^\((?<tone>[^)]*)\)\s+(?<words>.*)$")]
    private static partial Regex LeadingToneRegex();

    // Where a pause goes in a line written with "pauses: auto": after a comma or sentence end, with more to come ("Mr." isn't one).
    [GeneratedRegex(@"(?<!\b(?:Mr|Mrs|Ms|Dr|St))(?:\.{3}|…|[.!?,;:—])(?=\s+\S)")]
    private static partial Regex AutoPauseRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}_]+")]
    private static partial Regex NotInKeyRegex();

    [GeneratedRegex(@"\s*\(run:\s*(?<code>.*)\)\s*$")]
    private static partial Regex RunRegex();

    [GeneratedRegex(@"\s*\(dead\)\s*$")]
    private static partial Regex DeadRegex();

    [GeneratedRegex(@"(?:^|\s)(?:->|→)\s*(?<branch>[\p{L}\p{N}_.]+)\s*$")]
    private static partial Regex BranchRegex();

    private readonly Dictionary<string, (int? Emote, double? Speed)> _tones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slow"] = (null, 0.85),
        ["fast"] = (null, 1.3),
    };

    private readonly HashSet<string> _styleWords = new(StringComparer.OrdinalIgnoreCase);
    private double _defaultSpeed = DefaultSpeed;
    private bool _autoPauses;

    private DialogueBook(string title)
    {
        Group = KeyFrom(title);
        Speaker = title.Trim();
    }

    /// <summary>The part of the game's keys this character's conversations go under: <c>DialogueLibrary.Silvian</c>.</summary>
    public string Group { get; private set; }

    /// <summary>Who speaks a line that doesn't name anyone.</summary>
    public string Speaker { get; private set; }

    public List<DialogueConversation> Conversations { get; } = [];
    public List<DialogueIssue> Issues { get; } = [];

    /// <summary>Namespaces the definition needs for the code in its <c>complete:</c> and <c>run:</c> notes.</summary>
    public List<string> Usings { get; } = [];

    /// <summary>A panel color for a character the mod has no speaker profile for yet: "255 110 80" or "#FF6E50".</summary>
    public string? Color { get; private set; }

    /// <summary>Everyone who speaks in this document.</summary>
    public IEnumerable<string> Speakers =>
        Conversations.SelectMany(c => c.Lines).Select(l => l.Speaker).Distinct(StringComparer.OrdinalIgnoreCase);

    public static string KeyFrom(string text) => NotInKeyRegex().Replace(text.Trim(), "_").Trim('_');

    /// <summary>Reads a document's text with its project's characters, keywords and modifiers.</summary>
    public static DialogueBook Parse(string title, string text, Project? project = null)
    {
        // The document's own character counts as known, so "Silvian: Hi" works before they're added as a character.
        var characters = (project?.Characters ?? []).Append(new Character { Name = title.Trim() });
        var parser = new ScriptParser(new CharacterRegistry(characters), project?.Keywords)
        {
            CustomStyles = project?.StylesFor(title) ?? [],
            DialogueMarkup = true,
        };
        var book = new DialogueBook(title);
        foreach (var rule in project?.Styles.Modifiers ?? []) book._styleWords.Add(rule.Keyword);
        book.Read(text, parser);
        return book;
    }

    // ── Reading the document ────────────────────────────────────────────────

    /// <summary>A conversation being read, and how far in its lines are indented. Responses nest these.</summary>
    private sealed class Frame(DialogueConversation conversation, int indent)
    {
        public DialogueConversation Conversation { get; } = conversation;
        public int Indent { get; } = indent;
        public DialogueChoice? LastChoice { get; set; }
        public int ChoiceCount { get; set; }
        public string? LastSpeaker { get; set; }
    }

    private void Read(string text, ScriptParser parser)
    {
        var frames = new Stack<Frame>();
        var pendingNotes = new List<string>();
        int lineNumber = 0;

        foreach (string raw in ScriptParser.SplitLines(text))
        {
            lineNumber++;
            var line = parser.ParseLine(raw);
            switch (line.Type)
            {
                case ElementType.Blank:
                    continue;

                case ElementType.Note:
                    ReadNote(NoteText(raw, line), frames, pendingNotes, lineNumber);
                    continue;

                case ElementType.Heading:
                    frames.Clear();
                    var conversation = new DialogueConversation { Name = ConversationName(line.Body.Of(raw), lineNumber), SourceLine = lineNumber };
                    conversation.Comments.AddRange(pendingNotes);
                    pendingNotes.Clear();
                    AddConversation(conversation, lineNumber);
                    frames.Push(new Frame(conversation, 0));
                    continue;

                case ElementType.Dialogue or ElementType.Action or ElementType.ListItem:
                    break;

                default:
                    Issues.Add(new DialogueIssue(lineNumber, $"A {StyleSheet.DisplayNameOf(line.Type)} line isn't dialogue, so it's left out."));
                    continue;
            }

            if (frames.Count == 0)
            {
                Issues.Add(new DialogueIssue(lineNumber, "This isn't under a conversation, so it's left out. Put a # heading above it."));
                continue;
            }

            var frame = FrameFor(frames, line.IndentLevel);
            if (line.Type == ElementType.ListItem)
            {
                ReadChoice(line.Body.Of(raw).Trim(), frame, lineNumber);
                continue;
            }

            var added = ReadLine(raw, line, frame, lineNumber);
            added.Comments.AddRange(pendingNotes);
            pendingNotes.Clear();
        }

        foreach (var empty in Conversations.Where(c => c.Lines.Count == 0 && !c.HandWritten))
            Issues.Add(new DialogueIssue(empty.SourceLine, $"{empty.Name} has no lines yet."));
    }

    /// <summary>The conversation a line at this indent belongs to, opening a response if it's indented under a choice.</summary>
    private Frame FrameFor(Stack<Frame> frames, int indent)
    {
        while (frames.Count > 1 && indent < frames.Peek().Indent) frames.Pop();
        var top = frames.Peek();
        if (indent <= top.Indent || top.LastChoice is not { } choice) return top;

        // A response written under its choice. It uses the choice's "-> Name" if it has one, or gets a name of its own.
        string name = choice.Branch != null && choice.Branch.StartsWith(Group + ".", StringComparison.Ordinal)
            ? choice.Branch[(Group.Length + 1)..]
            : $"{top.Conversation.Name}_Choice{top.ChoiceCount}";
        var response = new DialogueConversation { Name = name, SourceLine = top.Conversation.SourceLine };
        choice.Branch = $"{Group}.{name}";
        top.LastChoice = null;
        AddConversation(response, top.Conversation.SourceLine);
        var frame = new Frame(response, indent) { LastSpeaker = top.LastSpeaker };
        frames.Push(frame);
        return frame;
    }

    private void AddConversation(DialogueConversation conversation, int lineNumber)
    {
        if (Conversations.Any(c => string.Equals(c.Name, conversation.Name, StringComparison.OrdinalIgnoreCase)))
            Issues.Add(new DialogueIssue(lineNumber, $"There are two conversations called {conversation.Name}. The game only keeps one."));
        Conversations.Add(conversation);
    }

    private DialogueLine ReadLine(string raw, ScriptLine line, Frame frame, int lineNumber)
    {
        string words = line.Body.Of(raw).Trim();
        string? tone = line.Modifier;
        string speaker = line.Type == ElementType.Dialogue
            ? SpeakerName(line)
            : frame.LastSpeaker ?? Speaker;

        // "(slow) Words." on a line without a name. If it isn't a tone, it's just words in parentheses.
        if (line.Type == ElementType.Action && LeadingToneRegex().Match(words) is { Success: true } leading && IsTone(leading.Groups["tone"].Value))
        {
            tone = leading.Groups["tone"].Value;
            words = leading.Groups["words"].Value;
        }

        var result = new DialogueLine { Speaker = speaker, Speed = _defaultSpeed };
        ApplyTone(result, tone, lineNumber);
        Lower(words, result, lineNumber);

        frame.Conversation.Lines.Add(result);
        frame.LastChoice = null;
        frame.ChoiceCount = 0;
        frame.LastSpeaker = speaker;
        return result;
    }

    private void ReadChoice(string body, Frame frame, int lineNumber)
    {
        if (frame.Conversation.Lines.Count == 0)
        {
            Issues.Add(new DialogueIssue(lineNumber, "A choice needs a line of dialogue above it."));
            return;
        }

        var choice = new DialogueChoice();
        for (bool found = true; found;)
        {
            found = false;
            if (RunRegex().Match(body) is { Success: true } run)
            {
                choice.OnSelected = run.Groups["code"].Value.Trim();
                body = body[..run.Index];
                found = true;
            }
            if (DeadRegex().Match(body) is { Success: true } dead)
            {
                choice.Dead = true;
                body = body[..dead.Index];
                found = true;
            }
        }
        if (BranchRegex().Match(body) is { Success: true } branch)
        {
            choice.Branch = FullKey(branch.Groups["branch"].Value);
            body = body[..branch.Index];
        }

        var scratch = new DialogueLine();
        Lower(body.Trim(), scratch, lineNumber);
        choice.Text = scratch.Text;

        frame.Conversation.Lines[^1].Choices.Add(choice);
        frame.LastChoice = choice;
        frame.ChoiceCount++;
    }

    private void ReadNote(string note, Stack<Frame> frames, List<string> pending, int lineNumber)
    {
        var directive = DirectiveRegex().Match(note);
        string word = directive.Success ? directive.Groups["word"].Value.ToLowerInvariant() : "";
        string value = directive.Groups["value"].Value.Trim();
        var conversation = frames.Count > 0 ? frames.Peek().Conversation : null;

        if (conversation == null && directive.Success)
        {
            switch (word)
            {
                case "speaker":
                    Speaker = value;
                    return;
                case "key":
                    Group = KeyFrom(value);
                    return;
                case "speed" when TryNumber(value, out double speed):
                    _defaultSpeed = speed;
                    return;
                case "pauses":
                    _autoPauses = value.Equals("auto", StringComparison.OrdinalIgnoreCase);
                    return;
                case "using":
                    Usings.Add(value.TrimEnd(';'));
                    return;
                case "color":
                    Color = value;
                    return;
                default:
                    if (word.StartsWith("tone", StringComparison.Ordinal))
                    {
                        ReadToneDefinition(directive.Groups["word"].Value["tone".Length..].Trim(), value, lineNumber);
                        return;
                    }
                    break;
            }
        }

        if (conversation != null)
        {
            if (HandWrittenRegex().IsMatch(note))
            {
                conversation.HandWritten = true;
                return;
            }
            switch (word)
            {
                case "unlock" when IsIdentifier(value):
                    conversation.Ending.Add($".UnlockJob(JobID.{value})");
                    return;
                case "complete" when value.Length > 0:
                    conversation.Ending.Add($".OnComplete({AsCallback(value)})");
                    return;
                case "handin" when IsIdentifier(value):
                    if (conversation.Lines.Count == 0 || conversation.Lines[^1].Choices.Count != 2)
                        Issues.Add(new DialogueIssue(lineNumber, "A hand-in needs a line with two choices above it: the first hands in, the second is \"not yet\"."));
                    else
                        conversation.Lines[^1].HandIn = value;
                    return;
            }
        }

        pending.Add(note);
    }

    private void ReadToneDefinition(string name, string value, int lineNumber)
    {
        var tone = new DialogueLine();
        var parts = new List<string>();
        if (!TryReadSettings(value, tone, ref parts) || parts.Count > 0)
        {
            Issues.Add(new DialogueIssue(lineNumber, $"The tone {name} should look like: emote 3, speed 0.9"));
            return;
        }
        bool hasEmote = value.Contains("emote", StringComparison.OrdinalIgnoreCase);
        bool hasSpeed = value.Contains("speed", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(value, @"(^|,)\s*[\d.]+\s*(,|$)");
        _tones[name] = (hasEmote ? tone.Emote : null, hasSpeed ? tone.Speed : null);
    }

    /// <summary>Applies a line's "(worried, speed 0.9)". Words that are style rules, like (thought), only affect how the page looks.</summary>
    private void ApplyTone(DialogueLine line, string? tone, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(tone)) return;
        var unknown = new List<string>();
        TryReadSettings(tone, line, ref unknown);
        foreach (string word in unknown)
            Issues.Add(new DialogueIssue(lineNumber, $"\"{word}\" isn't a tone. Add \"// tone {word}: emote 1, speed 1\" at the top of the document."));
    }

    private bool IsTone(string text)
    {
        var unknown = new List<string>();
        return TryReadSettings(text, new DialogueLine(), ref unknown) && unknown.Count == 0;
    }

    /// <summary>Reads "speed 0.9, emote 3, worried, 0.8" onto a line. Words it doesn't know are handed back.</summary>
    private bool TryReadSettings(string text, DialogueLine line, ref List<string> unknown)
    {
        bool any = false;
        foreach (string part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && words[0].Equals("speed", StringComparison.OrdinalIgnoreCase) && TryNumber(words[1], out double speed))
                line.Speed = speed;
            else if (words.Length == 2 && words[0].Equals("emote", StringComparison.OrdinalIgnoreCase) && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int emote))
                line.Emote = emote;
            else if (words.Length == 1 && TryNumber(words[0], out double bare))
                line.Speed = bare;
            else if (_tones.TryGetValue(part, out var tone))
            {
                if (tone.Emote is { } toneEmote) line.Emote = toneEmote;
                if (tone.Speed is { } toneSpeed) line.Speed = toneSpeed;
            }
            else if (!_styleWords.Contains(part))
            {
                unknown.Add(part);
                continue;
            }
            any = true;
        }
        return any;
    }

    private string SpeakerName(ScriptLine line)
    {
        string name = line.Character?.Name ?? line.Speaker ?? Speaker;
        if (name.Equals(Speaker, StringComparison.OrdinalIgnoreCase)) return Speaker;
        if (name.Equals("you", StringComparison.OrdinalIgnoreCase) || name.Equals("player", StringComparison.OrdinalIgnoreCase)) return "Player";
        if (name.Any(char.IsLower)) return name;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()); // written in capitals in the script: SILVIAN → Silvian
    }

    private string ConversationName(string heading, int lineNumber)
    {
        heading = heading.Trim();
        // "Silvian.Greeting" is fine in Silvian's document; "Argie.Greeting" belongs in Argie's.
        int dot = heading.IndexOf('.');
        if (dot > 0)
        {
            string prefix = KeyFrom(heading[..dot]);
            if (!prefix.Equals(Group, StringComparison.OrdinalIgnoreCase))
                Issues.Add(new DialogueIssue(lineNumber, $"\"{heading}\" names another character's conversation, but this document is {Group}'s, so it goes under {Group}."));
            heading = heading[(dot + 1)..];
        }
        string name = KeyFrom(heading);
        if (name.Length == 0)
        {
            name = $"Conversation{lineNumber}";
            Issues.Add(new DialogueIssue(lineNumber, "This heading has no name the game can use."));
        }
        return name;
    }

    private string FullKey(string branch) => branch.Contains('.') ? branch : $"{Group}.{branch}";

    private static string NoteText(string raw, ScriptLine line) => line.Body.Of(raw).Trim().TrimStart('/').Trim();

    private static bool IsIdentifier(string text) => text.Length > 0 && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary><c>Argie.TakeLetter</c> can be passed as it is; <c>Give(Main.LocalPlayer)</c> needs wrapping.</summary>
    private static string AsCallback(string code) => code.Contains('(') && !code.Contains("=>") ? $"() => {code}" : code;

    // ── Lowering: from what's written to what the game reads ────────────────

    /// <summary>Turns the document's marks into the game's anchors, collecting the effect for each one.</summary>
    private void Lower(string words, DialogueLine line, int lineNumber)
    {
        words = LineEditing.ClearEmphasis(words); // the game's text has no bold or italic
        if (_autoPauses && !DialogueSyntax.FindMarks(words, 0, words.Length).Any())
            words = AutoPauseRegex().Replace(words, m => m.Value + "[]");

        var text = new StringBuilder();
        int at = 0;
        foreach (var mark in DialogueSyntax.FindMarks(words, 0, words.Length))
        {
            text.Append(words, at, mark.Start - at);
            at = mark.Start + mark.Length;
            string inner = words.Substring(mark.WordsStart, mark.WordsLength);

            if (mark.IsToken)
            {
                text.Append(inner.Equals(DialogueSyntax.PlayerToken, StringComparison.OrdinalIgnoreCase)
                    ? $"[{DialogueSyntax.SpeakerNamePrefix}player]"
                    : $"[{inner}]");
                continue;
            }

            string? affect;
            if (mark.Effect == null)
            {
                // [] is a pause that fits its punctuation; [words] are stressed words.
                affect = inner.Length == 0 ? $"new Pause({PauseAfter(text)})" : "Inflection.Emphasis()";
            }
            else
            {
                affect = DialogueEffects.ToExpression(mark.Effect, out string? problem);
                if (problem != null) Issues.Add(new DialogueIssue(lineNumber, $"[{mark.Effect}: …]: {problem}."));
                if (affect == null)
                {
                    text.Append(inner); // [none: words], or an effect that couldn't be read: plain words
                    continue;
                }
            }
            line.Affects.Add(affect);
            text.Append('[').Append(inner).Append(']');
        }
        text.Append(words, at, words.Length - at);
        line.Text = text.ToString().Trim();
    }

    /// <summary>How long a [] waits, by what comes before it, in the range the hand-written lines use.</summary>
    private static int PauseAfter(StringBuilder text)
    {
        if (text.Length == 0) return 18;
        char last = text[^1];
        if (last == '…' || text.Length >= 3 && text[^1] == '.' && text[^2] == '.' && text[^3] == '.') return 28;
        return last switch
        {
            ',' => 14,
            '.' or '!' or '?' => 18,
            ';' or ':' or '—' or '–' or '-' => 18,
            _ => 16,
        };
    }
}
