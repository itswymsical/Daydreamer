using System.Text.RegularExpressions;

namespace Daydreamer.Core.Game;

/// <summary>A line as a definition in the mod's code has it.</summary>
public sealed record DefinedLine(string Speaker, int Emote, double Speed, IReadOnlyList<string> Affects, IReadOnlyList<DefinedChoice> Choices, string? HandIn);

/// <summary>A choice as a definition has it.</summary>
public sealed record DefinedChoice(string? Branch, bool Dead, string? OnSelected);

/// <summary>A conversation as a definition has it. <see cref="Ending"/> holds the chained calls after its lines.</summary>
public sealed record DefinedConversation(string Key, IReadOnlyList<DefinedLine> Lines, IReadOnlyList<string> Ending, string SourceFile);

/// <summary>
/// What the mod's code already defines, read from the text of its C# files. It isn't a compiler: it follows the
/// shape definitions are written in (<c>Dialogue.Define(key).Line(…).Choice(…).Register();</c>). Anything built
/// another way, such as a helper method, is only known by its key, in <see cref="OtherKeys"/>.
/// </summary>
public sealed partial class GameDefinitions
{
    [GeneratedRegex(@"\bDialogue\s*\.\s*Define\s*\(")]
    private static partial Regex DefineCallRegex();

    // A helper's first argument, when it looks like a dialogue key: DefineLost("Silvian.Lost_Exploring", …).
    [GeneratedRegex(@"\bDefine[A-Z]\w*\(\s*""(?<key>[\w.]+)""")]
    private static partial Regex HelperCallRegex();

    [GeneratedRegex(@"\bconst\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex ConstantRegex();

    [GeneratedRegex(@"\b(?:class|struct|record)\s+(?<name>\w+)")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"^\s*using\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"\bId\s*=\s*""(?<id>[^""]+)""")]
    private static partial Regex ProfileIdRegex();

    private readonly Dictionary<string, DefinedConversation> _conversations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sourceText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Conversations the mod's code defines that could be read in full, by key.</summary>
    public IReadOnlyDictionary<string, DefinedConversation> Conversations => _conversations;

    /// <summary>Keys defined in a way that couldn't be followed, like through a helper method.</summary>
    public HashSet<string> OtherKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Speakers with a profile in the mod's SpeakerRegistry.</summary>
    public HashSet<string> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Defines(string key) => _conversations.ContainsKey(key) || OtherKeys.Contains(key);

    /// <summary>The using lines of the file a conversation came from, for copying into a generated definition.</summary>
    public IEnumerable<string> UsingsOf(DefinedConversation conversation) =>
        _sourceText.TryGetValue(conversation.SourceFile, out string? text)
            ? UsingRegex().Matches(text).Select(m => m.Groups["name"].Value)
            : [];

    /// <summary>Reads a mod's definitions. Files Daydreamer generated are left out unless <paramref name="includeGenerated"/> says otherwise.</summary>
    public static GameDefinitions Read(GameFolder folder, bool includeGenerated = false)
    {
        var definitions = new GameDefinitions();
        if (!Directory.Exists(folder.CodeRoot)) return definitions;

        var files = Directory.EnumerateFiles(folder.CodeRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Select(f => (Path: f, Text: ReadQuietly(f)))
            .ToList();

        // Constants first: Define(ArgieNPC.GreetingKey) means whatever ArgieNPC says it is.
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, text) in files)
        {
            if (!text.Contains("const string")) continue;
            foreach (Match match in ConstantRegex().Matches(text))
            {
                string type = TypeRegex().Matches(text[..match.Index]).LastOrDefault()?.Groups["name"].Value ?? "";
                constants[$"{type}.{match.Groups["name"].Value}"] = match.Groups["value"].Value;
                constants.TryAdd(match.Groups["name"].Value, match.Groups["value"].Value);
            }
        }

        foreach (var (path, text) in files)
        {
            if (path.EndsWith("SpeakerRegistry.cs", StringComparison.OrdinalIgnoreCase))
                foreach (Match match in ProfileIdRegex().Matches(text)) definitions.Profiles.Add(match.Groups["id"].Value);

            if (!text.Contains("Define")) continue;
            if (!includeGenerated && path.Contains(GameFolder.GeneratedFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

            definitions._sourceText[path] = text;
            string? localType = null;
            foreach (Match call in DefineCallRegex().Matches(text))
            {
                localType = TypeRegex().Matches(text[..call.Index]).LastOrDefault()?.Groups["name"].Value;
                definitions.ReadChain(text, call.Index + call.Length - 1, path, constants, localType);
            }
            foreach (Match helper in HelperCallRegex().Matches(text))
                definitions.OtherKeys.Add(helper.Groups["key"].Value);
        }
        return definitions;
    }

    /// <summary>The bin and obj folders hold copies that aren't part of the code.</summary>
    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string ReadQuietly(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void ReadChain(string text, int openParen, string file, Dictionary<string, string> constants, string? localType)
    {
        var calls = ReadCalls(text, openParen);
        if (calls.Count == 0) return;
        string? key = Resolve(calls[0].Args, constants, localType);
        if (key == null) return; // a helper's parameter, like Define(key): its callers are counted as helpers

        // Only a chain that ends in Register() defines something on its own.
        if (calls[^1].Name != "Register" && calls[^1].Name != "Build") return;

        var lines = new List<DefinedLine>();
        var ending = new List<string>();
        string? speaker = null;
        int emote = 0;
        double speed = 1;
        List<string> affects = [];
        List<DefinedChoice> choices = [];
        string? handIn = null;
        bool open = false;

        void Finish()
        {
            if (open) lines.Add(new DefinedLine(speaker ?? "", emote, speed, affects, choices, handIn));
            open = false;
        }

        foreach (var (name, args) in calls.Skip(1))
        {
            switch (name)
            {
                case "Line":
                    Finish();
                    (speaker, emote, speed, affects) = ReadLineArguments(args, constants, localType);
                    choices = [];
                    handIn = null;
                    open = true;
                    break;
                case "Choice":
                    choices.Add(ReadChoiceArguments(args, constants, localType));
                    break;
                case "HandIn":
                    handIn = args.Trim().Replace("JobID.", "");
                    break;
                case "UnlockJob":
                case "OnComplete":
                    ending.Add($".{name}({args.Trim()})");
                    break;
            }
        }
        Finish();
        _conversations[key] = new DefinedConversation(key, lines, ending, file);
    }

    private static (string Speaker, int Emote, double Speed, List<string> Affects) ReadLineArguments(string args, Dictionary<string, string> constants, string? localType)
    {
        string speaker = "";
        int emote = 0;
        double speed = 1;
        var affects = new List<string>();
        var parts = CodeText.SplitArguments(args);
        for (int i = 0; i < parts.Count; i++)
        {
            var (name, value) = CodeText.SplitNamed(parts[i]);
            name ??= i switch { 0 => "speaker", 1 => "emote", 2 => "speed", _ => "affects" };
            switch (name)
            {
                case "speaker":
                    speaker = Resolve(value, constants, localType) ?? value;
                    break;
                case "emote":
                    _ = int.TryParse(value, out emote);
                    break;
                case "speed":
                    _ = double.TryParse(value.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed);
                    break;
                default:
                    if (value.StartsWith('[') && value.EndsWith(']'))
                        affects.AddRange(CodeText.SplitArguments(value[1..^1]));
                    else
                        affects.Add(value);
                    break;
            }
        }
        return (speaker, emote, speed, affects);
    }

    private static DefinedChoice ReadChoiceArguments(string args, Dictionary<string, string> constants, string? localType)
    {
        string? branch = null, onSelected = null;
        bool dead = false;
        var parts = CodeText.SplitArguments(args);
        for (int i = 0; i < parts.Count; i++)
        {
            var (name, value) = CodeText.SplitNamed(parts[i]);
            name ??= i switch { 0 => "branch", 1 => "onSelected", _ => "dead" };
            switch (name)
            {
                case "branch":
                    branch = Resolve(value, constants, localType) ?? value;
                    break;
                case "onSelected":
                    onSelected = value.StartsWith("() =>", StringComparison.Ordinal) ? value["() =>".Length..].Trim() : $"{value}()";
                    break;
                case "dead":
                    dead = value == "true";
                    break;
            }
        }
        return new DefinedChoice(branch, dead, onSelected);
    }

    /// <summary>A string in code: a literal, or a constant by name. Null if it's anything else.</summary>
    private static string? Resolve(string code, Dictionary<string, string> constants, string? localType)
    {
        code = code.Trim();
        if (code.Length >= 2 && code[0] == '"' && code[^1] == '"') return code[1..^1];
        if (localType != null && constants.TryGetValue($"{localType}.{code}", out string? local)) return local;
        return constants.GetValueOrDefault(code);
    }

    /// <summary>
    /// The calls in a chain, from the "(" of its first: <c>("X") .Line(a) .Choice(b) .Register()</c> gives
    /// ("", "X"), ("Line", "a"), ("Choice", "b"), ("Register", ""). Comments between calls are skipped.
    /// </summary>
    private static List<(string Name, string Args)> ReadCalls(string text, int openParen)
    {
        var calls = new List<(string, string)>();
        string name = "";
        int at = openParen;
        while (at < text.Length)
        {
            int close = MatchingParen(text, at);
            if (close < 0) break;
            calls.Add((name, text[(at + 1)..close]));

            at = SkipSpaceAndComments(text, close + 1);
            if (at >= text.Length || text[at] != '.') break;
            int start = SkipSpaceAndComments(text, at + 1);
            int end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            name = text[start..end];
            at = SkipSpaceAndComments(text, end);
            if (at >= text.Length || text[at] != '(') break;
        }
        return calls;
    }

    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') i = CodeText.SkipString(text, i);
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') i = LineEnd(text, i);
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static int SkipSpaceAndComments(string text, int at)
    {
        while (at < text.Length)
        {
            if (char.IsWhiteSpace(text[at])) at++;
            else if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '/') at = LineEnd(text, at) + 1;
            else break;
        }
        return at;
    }

    private static int LineEnd(string text, int at)
    {
        int end = text.IndexOf('\n', at);
        return end < 0 ? text.Length - 1 : end;
    }
}
