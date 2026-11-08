using System.Globalization;
using System.Text.RegularExpressions;

namespace Daydreamer.Core.Game;

/// <summary>
/// The effects you can put on words in game dialogue, and the C# each one becomes in a definition:
/// <code>
///   [pause 30:]            new Pause(30)                    ([30] for short; [] picks a length from the punctuation)
///   [shake 0.6: words]     new Shake(0.6f)                  also wave
///   [slow 2: words]        new Slow(2f)                     also fast, and pitch 1.2
///   [up 0.3: words]        Inflection.Up(0.3f)              also down, question, emphasis. All take "lift 2" too
///   [waver: words]         Inflection.Waver()               takes "rate 0.5" too
///   [inflect to 1.4 steps 4 lift 3: words]  new Inflection(1.4f, steps: 4, lift: 3f)
///   [sound MenuTick:]      new Sound(SoundID.MenuTick)
///   [none: words]          (nothing: plain words)
///   [color: color]           (not an effect, but a color change)
/// </code>
/// </summary>
public static partial class DialogueEffects
{
    /// <summary>Every effect, for menus and messages.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["pause", "shake", "wave", "slow", "fast", "pitch", "up", "down", "question", "emphasis", "waver", "inflect", "sound", "color", "none"];

    // Inflection's constructor, in order: doc name → C# parameter.
    private static readonly (string Word, string Parameter)[] InflectParameters =
    [
        ("to", "to"), ("from", "from"), ("shape", "shape"), ("ease", "ease"), ("vibrato", "vibrato"),
        ("rate", "vibratoRate"), ("steps", "steps"), ("jitter", "jitter"), ("speed", "speed"), ("lift", "lift"),
    ];

    [GeneratedRegex(@"^new\s+(?<name>Pause|Slow|Fast|Shake|Wave|Pitch|Sound|Inflection)\((?<args>.*)\)$", RegexOptions.Singleline)]
    private static partial Regex NewRegex();

    [GeneratedRegex(@"^Inflection\.(?<name>Up|Down|Question|Emphasis|Waver)\((?<args>.*)\)$", RegexOptions.Singleline)]
    private static partial Regex InflectionRegex();

    /// <summary>
    /// The C# for an effect, like "shake 0.6" → <c>new Shake(0.6f)</c>. Null with a reason if it doesn't make
    /// sense, and null with no reason for "none".
    /// </summary>
    public static string? ToExpression(string effect, out string? problem)
    {
        problem = null;
        var words = effect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string name = words[0].ToLowerInvariant();
        var settings = words.Skip(1).ToList();
        try
        {
            switch (name)
            {
                case "none":
                    return null;
                case "pause":
                    return settings.Count == 0 ? "new Pause()" : $"new Pause({Whole(settings[0])})";
                case "shake" or "wave" or "slow" or "fast":
                    string type = char.ToUpperInvariant(name[0]) + name[1..];
                    return settings.Count == 0 ? $"new {type}()" : $"new {type}({Float(settings[0])})";
                case "color": //LETS GO BABY
                    if (settings.Count == 0) throw new FormatException("color needs rgb values, like [color 255 255 255:]");
                    return $"new Colorize(new Color({settings[0]}, {settings[1]}, {settings[2]}))";
                case "size":
                    if (settings.Count == 0) throw new FormatException("size needs a value, like [size 1.2: words]");
                    return $"new DialogueScale({Float(settings[0])})";
                case "pitch":
                    if (settings.Count == 0) throw new FormatException("pitch needs a value, like [pitch 1.2: words]");
                    return $"new Pitch({Float(settings[0])})";
                case "sound":
                    if (settings.Count == 0) throw new FormatException("sound needs a name, like [sound MenuTick:]");
                    return $"new Sound(SoundID.{settings[0]})";
                case "up" or "down" or "question" or "emphasis":
                    return Named($"Inflection.{char.ToUpperInvariant(name[0])}{name[1..]}", settings, "lift", "shape");
                case "waver":
                    return Named("Inflection.Waver", settings, "rate");
                case "inflect":
                    return Inflect(settings);
                default:
                    problem = $"there's no effect called \"{words[0]}\" (try {string.Join(", ", Names)})";
                    return null;
            }
        }
        catch (FormatException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    /// <summary>Back the other way, for reading hand-written definitions: <c>new Shake(0.6f)</c> → "shake 0.6". Null for anything unusual.</summary>
    public static string? FromExpression(string expression)
    {
        expression = expression.Trim();
        var plain = NewRegex().Match(expression);
        if (plain.Success)
        {
            string name = plain.Groups["name"].Value;
            var args = CodeText.SplitArguments(plain.Groups["args"].Value);
            if (name == "Inflection") return FromInflect(args);
            if (name == "Sound")
                return args.Count == 1 && args[0].StartsWith("SoundID.", StringComparison.Ordinal) ? $"sound {args[0]["SoundID.".Length..]}" : null;
            if (args.Count == 0) return name.ToLowerInvariant();
            return args.Count == 1 && IsNumber(args[0]) ? $"{name.ToLowerInvariant()} {NumberWord(args[0])}" : null;
        }

        var shortcut = InflectionRegex().Match(expression);
        if (!shortcut.Success) return null;
        return FromShortcut(shortcut.Groups["name"].Value, CodeText.SplitArguments(shortcut.Groups["args"].Value));
    }

    private static string FromShortcut(string name, List<string> args)
    {
        // Their first setting comes first with no name: amount, or depth for Waver. The next is lift, or rate for Waver.
        string second = name == "Waver" ? "rate" : "lift";
        var words = new List<string> { name.ToLowerInvariant() };
        for (int i = 0; i < args.Count; i++)
        {
            var (parameter, value) = CodeText.SplitNamed(args[i]);
            parameter ??= i == 0 ? "" : second;
            if (parameter is "amount" or "depth") parameter = "";
            if (parameter == "shape") value = value.Replace("InflectionShape.", "").ToLowerInvariant();
            if (parameter.Length > 0) words.Add(parameter);
            words.Add(IsNumber(value) ? NumberWord(value) : value);
        }
        return string.Join(' ', words);
    }

    private static string? FromInflect(List<string> args)
    {
        var words = new List<string> { "inflect" };
        for (int i = 0; i < args.Count; i++)
        {
            var (parameter, value) = CodeText.SplitNamed(args[i]);
            parameter ??= i < InflectParameters.Length ? InflectParameters[i].Parameter : null;
            int index = Array.FindIndex(InflectParameters, p => p.Parameter == parameter);
            if (index < 0) return null;
            if (parameter == "shape") value = value.Replace("InflectionShape.", "").ToLowerInvariant();
            if (parameter == "ease") value = value.Replace("EaseFunction.", "");
            if (value.Contains(' ')) return null;
            words.Add(InflectParameters[index].Word);
            words.Add(IsNumber(value) ? NumberWord(value) : value);
        }
        return string.Join(' ', words);
    }

    /// <summary>"Inflection.Up", "0.3 lift 2" → <c>Inflection.Up(0.3f, lift: 2f)</c>.</summary>
    private static string Named(string method, List<string> settings, params string[] named)
    {
        var args = new List<string>();
        int i = 0;
        if (i < settings.Count && IsNumber(settings[i])) args.Add(Float(settings[i++]));
        while (i < settings.Count)
        {
            string parameter = settings[i].ToLowerInvariant();
            if (!named.Contains(parameter) || i + 1 >= settings.Count)
                throw new FormatException($"\"{settings[i]}\" doesn't fit there (it can take {string.Join(", ", named)})");
            args.Add($"{parameter}: {SettingValue(parameter, settings[i + 1])}");
            i += 2;
        }
        return $"{method}({string.Join(", ", args)})";
    }

    private static string Inflect(List<string> settings)
    {
        var values = new Dictionary<string, string>();
        for (int i = 0; i < settings.Count; i += 2)
        {
            string word = settings[i].ToLowerInvariant();
            if (Array.FindIndex(InflectParameters, p => p.Word == word) < 0 || i + 1 >= settings.Count)
                throw new FormatException($"\"{settings[i]}\" doesn't fit there (inflect can take {string.Join(", ", InflectParameters.Select(p => p.Word))})");
            values[word] = settings[i + 1];
        }

        var args = new List<string>();
        foreach (var (word, parameter) in InflectParameters)
        {
            if (!values.TryGetValue(word, out string? value)) continue;
            // "to" comes first with no name, as these are usually written.
            args.Add(word == "to" ? Float(value) : $"{parameter}: {SettingValue(word, value)}");
        }
        return $"new Inflection({string.Join(", ", args)})";
    }

    private static string SettingValue(string word, string value) => word switch
    {
        "shape" => "InflectionShape." + char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant(),
        "ease" => "EaseFunction." + value,
        "steps" => Whole(value),
        _ => Float(value),
    };

    private static bool IsNumber(string word) =>
        double.TryParse(word.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static string NumberWord(string code) => code.TrimEnd('f', 'F');

    private static string Float(string word)
    {
        if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException($"\"{word}\" isn't a number");
        return FloatCode(value);
    }

    private static string Whole(string word)
    {
        if (!int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new FormatException($"\"{word}\" isn't a whole number");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>1.1 → "1.1f", the way a float is written in C#.</summary>
    public static string FloatCode(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "f";
}

/// <summary>Small helpers for reading C# without a compiler: it only has to follow the shape definitions are written in.</summary>
internal static class CodeText
{
    /// <summary>Splits an argument list at its top-level commas, leaving strings and anything in brackets alone.</summary>
    public static List<string> SplitArguments(string text)
    {
        var args = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') i = SkipString(text, i);
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        string last = text[start..].Trim();
        if (last.Length > 0) args.Add(last);
        return args;
    }

    /// <summary>"lift: 2f" → ("lift", "2f"); "2f" → (null, "2f").</summary>
    public static (string? Name, string Value) SplitNamed(string argument)
    {
        int colon = argument.IndexOf(':');
        if (colon <= 0 || !argument[..colon].All(c => char.IsLetterOrDigit(c) || c == '_')) return (null, argument);
        return (argument[..colon], argument[(colon + 1)..].Trim());
    }

    /// <summary>The index of the closing quote of the string starting at <paramref name="start"/>.</summary>
    public static int SkipString(string text, int start)
    {
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == '"') return i;
        }
        return text.Length - 1;
    }
}
