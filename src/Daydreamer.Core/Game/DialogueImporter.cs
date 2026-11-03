using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Game;

/// <summary>A character's existing dialogue, written out as a document.</summary>
/// <param name="Title">The document's title: the character's group.</param>
/// <param name="Text">The document.</param>
/// <param name="Speakers">Who speaks in it, to add as characters.</param>
public sealed record ImportedDialogue(string Title, string Text, IReadOnlyList<string> Speakers);

/// <summary>
/// Brings dialogue that's already in the game into Daydreamer: a localization file's words, joined up with what
/// the mod's C# says about them (speed, emotes, the effect on each anchor, where choices lead). A conversation whose
/// code can't be followed is marked <c>// hand-written</c>, so its words are exported but its code is left alone.
/// </summary>
public static partial class DialogueImporter
{
    [GeneratedRegex(@"^Line(?<n>\d+)$")]
    private static partial Regex LineKeyRegex();

    [GeneratedRegex(@"^Choice(?<n>\d+)$")]
    private static partial Regex ChoiceKeyRegex();

    /// <summary>One document per character in a localization file. Files without dialogue give none.</summary>
    public static List<ImportedDialogue> Read(string hjson, GameDefinitions? definitions = null)
    {
        var results = new List<ImportedDialogue>();
        var root = HjsonReader.Parse(hjson);
        if (root.Find(DialogueExporter.LibraryKey) is not { } library) return results;
        foreach (var group in library.Children.Where(g => g.Value == null))
            results.Add(ReadGroup(group, definitions));
        return results;
    }

    private static ImportedDialogue ReadGroup(HjsonNode group, GameDefinitions? definitions)
    {
        var conversations = group.Children.Select(c => (Node: c, Code: definitions?.Conversations.GetValueOrDefault($"{group.Key}.{c.Key}"))).ToList();
        var lines = conversations.SelectMany(c => c.Code?.Lines ?? []).ToList();

        // The character is whoever speaks most, and the document's default speed is the one they use most.
        string speaker = lines.GroupBy(l => l.Speaker).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? group.Key;
        double speed = lines.Count == 0
            ? DialogueBook.DefaultSpeed
            : lines.GroupBy(l => l.Speed).OrderByDescending(g => g.Count()).First().Key;

        var body = new StringBuilder();
        var speakers = new List<string> { speaker };
        var usings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (node, code) in conversations)
        {
            WriteConversation(body, group.Key, node, code, speaker, speed, speakers);
            if (code != null && definitions != null && UsesCode(code))
                usings.UnionWith(definitions.UsingsOf(code));
        }

        var profile = new StringBuilder();
        if (speaker != group.Key) profile.Append("// speaker: ").Append(speaker).Append("\r\n");
        if (Math.Abs(speed - DialogueBook.DefaultSpeed) > 0.0001)
            profile.Append("// speed: ").Append(speed.ToString("0.###", CultureInfo.InvariantCulture)).Append("\r\n");
        foreach (string name in usings.Where(NeededUsing).Order(StringComparer.Ordinal))
            profile.Append("// using: ").Append(name).Append("\r\n");
        if (profile.Length > 0) profile.Append("\r\n");

        return new ImportedDialogue(group.Key, profile.Append(body).ToString().TrimEnd() + "\r\n", speakers);
    }

    private static bool UsesCode(DefinedConversation code) =>
        code.Ending.Any(e => e.StartsWith(".OnComplete", StringComparison.Ordinal))
        || code.Lines.Any(l => l.Choices.Any(c => c.OnSelected != null));

    /// <summary>Terraria's and the system's are global usings in the mod, and the job IDs are added when they're used.</summary>
    private static bool NeededUsing(string name) =>
        !name.StartsWith("System", StringComparison.Ordinal) && !name.StartsWith("Terraria", StringComparison.Ordinal)
        && !name.EndsWith(".Core.Jobs", StringComparison.Ordinal);

    private static void WriteConversation(StringBuilder output, string group, HjsonNode node, DefinedConversation? code, string character, double defaultSpeed, List<string> speakers)
    {
        const string newline = "\r\n";
        foreach (string comment in node.Comments) output.Append("// ").Append(CommentText(comment)).Append(newline);
        output.Append("# ").Append(node.Key).Append(newline);

        // Code that points at something only the hand-written class can see, like its own private method, has to stay there.
        bool handWritten = code == null || code.Ending.Any(ReachesIntoItsClass) || code.Lines.Any(l => l.Affects.Any(a => DialogueEffects.FromExpression(a) == null));
        if (handWritten) output.Append("// hand-written").Append(newline);

        var lineNodes = node.Children
            .Select(c => (Node: c, Match: LineKeyRegex().Match(c.Key)))
            .Where(c => c.Match.Success)
            .OrderBy(c => int.Parse(c.Match.Groups["n"].Value, CultureInfo.InvariantCulture))
            .ToList();
        for (int i = 0; i < lineNodes.Count; i++)
        {
            var lineNode = lineNodes[i].Node;
            foreach (string comment in lineNode.Comments) output.Append("// ").Append(CommentText(comment)).Append(newline);

            var defined = !handWritten && i < code!.Lines.Count ? code.Lines[i] : null;
            string speaker = defined?.Speaker is { Length: > 0 } named ? named : character;
            if (!speakers.Contains(speaker, StringComparer.OrdinalIgnoreCase)) speakers.Add(speaker);

            string text = lineNode.Find("Text")?.Value ?? "";
            output.Append(speaker.ToUpper(CultureInfo.InvariantCulture)).Append(Tone(defined, defaultSpeed)).Append(": ")
                  .Append(Marked(text, defined)).Append(newline);

            var choiceNodes = lineNode.Children
                .Select(c => (Node: c, Match: ChoiceKeyRegex().Match(c.Key)))
                .Where(c => c.Match.Success)
                .OrderBy(c => int.Parse(c.Match.Groups["n"].Value, CultureInfo.InvariantCulture))
                .ToList();
            for (int c = 0; c < choiceNodes.Count; c++)
            {
                var definedChoice = defined != null && c < defined.Choices.Count ? defined.Choices[c] : null;
                output.Append("- ").Append(Marked(choiceNodes[c].Node.Find("Text")?.Value ?? "", null)).Append(Flow(definedChoice, group)).Append(newline);
            }
            if (defined?.HandIn is { } job) output.Append("// handin: ").Append(job).Append(newline);
        }

        if (!handWritten)
        {
            foreach (string ending in code!.Ending)
                output.Append("// ").Append(EndingNote(ending)).Append(newline);
        }
        output.Append(newline);
    }

    /// <summary><c>.OnComplete(GiveLetter)</c> names a method of the hand-written class; a generated class can't call it.</summary>
    private static bool ReachesIntoItsClass(string ending)
    {
        if (!ending.StartsWith(".OnComplete(", StringComparison.Ordinal)) return false;
        string target = ending[".OnComplete(".Length..^1].Trim();
        if (target.StartsWith("() =>", StringComparison.Ordinal)) target = target["() =>".Length..].Trim();
        return !target.Contains('.');
    }

    private static string EndingNote(string ending)
    {
        if (ending.StartsWith(".UnlockJob(", StringComparison.Ordinal))
            return "unlock: " + ending[".UnlockJob(".Length..^1].Replace("JobID.", "").Trim();
        string target = ending[".OnComplete(".Length..^1].Trim();
        if (target.StartsWith("() =>", StringComparison.Ordinal)) target = target["() =>".Length..].Trim();
        return "complete: " + target;
    }

    private static string CommentText(string comment)
    {
        if (comment.StartsWith('#')) return comment[1..].Trim();
        return comment.StartsWith("//", StringComparison.Ordinal) ? comment[2..].Trim() : comment.Trim();
    }

    private static string Tone(DefinedLine? line, double defaultSpeed)
    {
        if (line == null) return "";
        var parts = new List<string>();
        if (Math.Abs(line.Speed - defaultSpeed) > 0.0001) parts.Add("speed " + line.Speed.ToString("0.###", CultureInfo.InvariantCulture));
        if (line.Emote != 0) parts.Add("emote " + line.Emote.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    private static string Flow(DefinedChoice? choice, string group)
    {
        if (choice == null) return "";
        var flow = new StringBuilder();
        if (choice.Branch is { } branch)
            flow.Append(" -> ").Append(branch.StartsWith(group + ".", StringComparison.Ordinal) ? branch[(group.Length + 1)..] : branch);
        if (choice.Dead) flow.Append(" (dead)");
        if (choice.OnSelected is { } code) flow.Append(" (run: ").Append(code).Append(')');
        return flow.ToString();
    }

    /// <summary>The game's text in the document's form: each anchor with its effect, or as it was if the code isn't known.</summary>
    private static string Marked(string text, DefinedLine? line)
    {
        var output = new StringBuilder();
        int at = 0, anchor = 0;
        foreach (var mark in GameAnchors(text))
        {
            output.Append(text, at, mark.Start - at);
            at = mark.Start + mark.Length;
            string inner = text.Substring(mark.Start + 1, mark.Length - 2);

            if (inner.StartsWith(DialogueSyntax.SpeakerNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string who = inner[DialogueSyntax.SpeakerNamePrefix.Length..].Trim();
                output.Append(who.Equals(DialogueSyntax.PlayerToken, StringComparison.OrdinalIgnoreCase) ? "[player]" : $"[{inner}]");
                continue;
            }

            if (line == null)
            {
                output.Append('[').Append(inner).Append(']');
                continue;
            }

            string? effect = anchor < line.Affects.Count ? DialogueEffects.FromExpression(line.Affects[anchor]) : "none";
            anchor++;
            if (effect != null && effect.StartsWith("pause ", StringComparison.Ordinal) && inner.Length == 0)
                output.Append('[').Append(effect["pause ".Length..]).Append(']');
            else if (inner.Length == 0 && effect == "none")
                continue; // a pause with no effect in the code does nothing in the game
            else
                output.Append('[').Append(effect ?? "none").Append(':').Append(inner.Length > 0 ? " " : "").Append(inner).Append(']');
        }
        output.Append(text, at, text.Length - at);
        return output.ToString();
    }

    /// <summary>The game's anchors: any <c>[…]</c>, the way its dialogue builder finds them.</summary>
    private static IEnumerable<(int Start, int Length)> GameAnchors(string text)
    {
        for (int i = text.IndexOf('['); i >= 0;)
        {
            int close = text.IndexOf(']', i + 1);
            if (close < 0) yield break;
            yield return (i, close - i + 1);
            i = text.IndexOf('[', close + 1);
        }
    }
}
