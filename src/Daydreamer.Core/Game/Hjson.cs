using System.Globalization;
using System.Text;

namespace Daydreamer.Core.Game;

/// <summary>
/// One entry of an HJSON file: a key with either a piece of text, or entries of its own. Dotted keys are
/// taken apart, so <c>Line1.Text: Hi</c> reads as an entry Line1 holding an entry Text.
/// </summary>
public sealed class HjsonNode
{
    public HjsonNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>The text, or null if this entry holds other entries.</summary>
    public string? Value { get; set; }

    public List<HjsonNode> Children { get; } = [];

    /// <summary>Comment lines above the entry, as written (with their # or //).</summary>
    public List<string> Comments { get; } = [];

    /// <summary>Comment lines at the end of an entry's block, with nothing after them.</summary>
    public List<string> TrailingComments { get; } = [];

    public HjsonNode? Find(string key) => Children.FirstOrDefault(c => c.Key == key);

    /// <summary>The entry at a path of keys, or null.</summary>
    public HjsonNode? FindPath(params string[] path)
    {
        var node = this;
        foreach (string key in path)
        {
            node = node.Find(key);
            if (node == null) return null;
        }
        return node;
    }

    /// <summary>The child with this key, added if it isn't there.</summary>
    public HjsonNode Ensure(string key)
    {
        var child = Find(key);
        if (child == null)
        {
            child = new HjsonNode(key);
            Children.Add(child);
        }
        return child;
    }

    /// <summary>Every piece of text under this entry, as dotted key → text.</summary>
    public IEnumerable<KeyValuePair<string, string>> Flatten(string prefix = "")
    {
        foreach (var child in Children)
        {
            string key = prefix.Length == 0 ? child.Key : $"{prefix}.{child.Key}";
            if (child.Value != null) yield return new(key, child.Value);
            else foreach (var pair in child.Flatten(key)) yield return pair;
        }
    }
}

/// <summary>The HJSON file doesn't make sense at some point.</summary>
public sealed class HjsonException(string message) : Exception(message);

/// <summary>Reads HJSON, keeping the order of the entries and their comments so a file can be written back as it was.</summary>
public sealed class HjsonReader
{
    private readonly string _text;
    private int _at;

    private HjsonReader(string text)
    {
        _text = text.Replace("\r\n", "\n").TrimStart('﻿');
    }

    /// <summary>Everything in the file, as the entries of a nameless root.</summary>
    public static HjsonNode Parse(string text)
    {
        var reader = new HjsonReader(text);
        var root = new HjsonNode("");
        var comments = reader.SkipToToken();
        bool braces = reader.Peek() == '{'; // the outermost braces are optional
        if (braces) reader._at++;
        reader.ReadMembers(root, comments, braces);
        return root;
    }

    private char Peek() => _at < _text.Length ? _text[_at] : '\0';

    private void ReadMembers(HjsonNode parent, List<string> comments, bool closedByBrace)
    {
        while (true)
        {
            comments.AddRange(SkipToToken());
            if (_at >= _text.Length)
            {
                if (closedByBrace) throw Problem("a closing } is missing");
                parent.TrailingComments.AddRange(comments);
                return;
            }
            if (Peek() == '}')
            {
                if (!closedByBrace) throw Problem("there's a } with nothing to close");
                _at++;
                parent.TrailingComments.AddRange(comments);
                return;
            }

            string key = ReadKey();
            comments.AddRange(SkipToToken());

            // "Line1.Text" is an entry Line1 holding an entry Text. Comments stay with the outermost new entry.
            var node = parent;
            HjsonNode? firstNew = null;
            foreach (string part in key.Split('.'))
            {
                var existing = node.Find(part);
                if (existing == null)
                {
                    existing = new HjsonNode(part);
                    node.Children.Add(existing);
                    firstNew ??= existing;
                }
                node = existing;
            }
            (firstNew ?? node).Comments.AddRange(comments);
            comments = [];

            if (Peek() == '{')
            {
                _at++;
                ReadMembers(node, [], closedByBrace: true);
            }
            else
            {
                node.Value = ReadValue();
            }
        }
    }

    /// <summary>Skips spaces, line breaks, commas and comments, and returns the comments.</summary>
    private List<string> SkipToToken()
    {
        var comments = new List<string>();
        while (_at < _text.Length)
        {
            char c = _text[_at];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                _at++;
            }
            else if (c == '#' || c == '/' && _at + 1 < _text.Length && _text[_at + 1] == '/')
            {
                int end = _text.IndexOf('\n', _at);
                if (end < 0) end = _text.Length;
                comments.Add(_text[_at..end].TrimEnd());
                _at = end;
            }
            else if (c == '/' && _at + 1 < _text.Length && _text[_at + 1] == '*')
            {
                int end = _text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
                if (end < 0) throw Problem("a /* comment never ends");
                comments.AddRange(_text[_at..(end + 2)].Split('\n').Select(l => l.Trim()));
                _at = end + 2;
            }
            else
            {
                break;
            }
        }
        return comments;
    }

    private string ReadKey()
    {
        string key;
        if (Peek() == '"' || Peek() == '\'')
        {
            key = ReadQuoted(Peek());
        }
        else
        {
            int start = _at;
            while (_at < _text.Length && _text[_at] != ':' && _text[_at] != '\n') _at++;
            if (Peek() != ':') throw Problem("a key has no colon after it");
            key = _text[start.._at].Trim();
        }
        while (char.IsWhiteSpace(Peek()) && Peek() != '\0') _at++;
        if (Peek() != ':') throw Problem($"the key \"{key}\" has no colon after it");
        _at++;
        return key;
    }

    private string ReadValue()
    {
        char c = Peek();
        if (c == '\'' && string.CompareOrdinal(_text, _at, "'''", 0, 3) == 0) return ReadMultiline();
        if (c is '"' or '\'') return ReadQuoted(c);
        if (c == '[') throw Problem("lists aren't used in localization files");

        // Without quotes, a value runs to the end of the line.
        int end = _text.IndexOf('\n', _at);
        if (end < 0) end = _text.Length;
        string value = _text[_at..end].TrimEnd();
        _at = end;
        return value;
    }

    private string ReadQuoted(char quote)
    {
        var value = new StringBuilder();
        for (_at++; _at < _text.Length; _at++)
        {
            char c = _text[_at];
            if (c == quote)
            {
                _at++;
                return value.ToString();
            }
            if (c == '\n') throw Problem("a quoted string runs past the end of its line");
            if (c != '\\')
            {
                value.Append(c);
                continue;
            }
            _at++;
            char escaped = Peek();
            value.Append(escaped switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                'b' => '\b',
                'f' => '\f',
                'u' when _at + 4 < _text.Length => (char)int.Parse(_text.AsSpan(_at + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                _ => escaped,
            });
            if (escaped == 'u') _at += 4;
        }
        throw Problem("a quoted string never ends");
    }

    /// <summary>A ''' string: its lines lose the indent of the opening ''', and the line break before the closing ''' goes too.</summary>
    private string ReadMultiline()
    {
        int lineStart = _text.LastIndexOf('\n', _at) + 1;
        int indent = _at - lineStart;
        _at += 3;
        int end = _text.IndexOf("'''", _at, StringComparison.Ordinal);
        if (end < 0) throw Problem("a ''' string never ends");
        string raw = _text[_at..end];
        _at = end + 3;

        var lines = raw.Split('\n').ToList();
        if (lines.Count == 1) return raw;
        if (string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        if (string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines.Select(line =>
        {
            int strip = 0;
            while (strip < indent && strip < line.Length && (line[strip] == ' ' || line[strip] == '\t')) strip++;
            return line[strip..];
        }));
    }

    private HjsonException Problem(string what)
    {
        int line = 1 + _text.AsSpan(0, Math.Min(_at, _text.Length)).Count('\n');
        return new HjsonException($"Line {line}: {what}.");
    }
}

/// <summary>
/// Writes HJSON the way tModLoader does (tabs, quotes only where needed, <c>Line1.Text: Hi</c> instead of a block
/// for a single text, an empty line around blocks), so what Daydreamer writes matches the files beside it and a
/// version-control diff only shows the words that changed.
/// </summary>
public static class HjsonWriter
{
    public static string Write(HjsonNode root, string newline = "\r\n")
    {
        var output = new StringBuilder();
        WriteMembers(output, root, 0, newline);
        return output.ToString();
    }

    private static void WriteMembers(StringBuilder output, HjsonNode parent, int depth, string newline)
    {
        bool first = true, previousWasBlock = false;
        foreach (var child in parent.Children)
        {
            var (key, end, comments) = Collapse(child);
            bool isBlock = end.Value == null;
            if (!first && (isBlock || previousWasBlock)) output.Append(newline);
            first = false;
            previousWasBlock = isBlock;

            string indent = new('\t', depth);
            foreach (string comment in comments) output.Append(indent).Append(comment).Append(newline);

            if (isBlock)
            {
                output.Append(indent).Append(key).Append(": {").Append(newline);
                WriteMembers(output, end, depth + 1, newline);
                output.Append(indent).Append('}').Append(newline);
            }
            else
            {
                output.Append(indent).Append(key).Append(':');
                WriteValue(output, end.Value!, depth, newline);
                output.Append(newline);
            }
        }

        string innerIndent = new('\t', depth);
        foreach (string comment in parent.TrailingComments) output.Append(innerIndent).Append(comment).Append(newline);
    }

    /// <summary>A run of entries with one child each that ends in a text is written as one dotted key.</summary>
    private static (string Key, HjsonNode End, List<string> Comments) Collapse(HjsonNode node)
    {
        var path = new List<string> { node.Key };
        var comments = new List<string>(node.Comments);
        var current = node;
        while (current.Value == null && current.Children.Count == 1 && current.TrailingComments.Count == 0)
        {
            current = current.Children[0];
            path.Add(current.Key);
            comments.AddRange(current.Comments);
        }
        return current.Value != null
            ? (KeyText(path), current, comments)
            : (KeyText([node.Key]), node, [.. node.Comments]);
    }

    private static string KeyText(IEnumerable<string> path)
    {
        string key = string.Join('.', path);
        bool needsQuotes = key.Length == 0 || key.Any(c => char.IsWhiteSpace(c) || c is ',' or ':' or '[' or ']' or '{' or '}' or '"' or '\'' or '#');
        return needsQuotes ? $"\"{Escape(key)}\"" : key;
    }

    private static void WriteValue(StringBuilder output, string value, int depth, string newline)
    {
        const string separator = " ";
        if (value.Length == 0)
        {
            output.Append(separator).Append("\"\"");
            return;
        }

        char left = value[0], right = value[^1];
        bool hasControl = value.Any(c => c is '\t' or '\f' or '\b' or '\n' or '\r');
        bool quotes = hasControl || char.IsWhiteSpace(left) || char.IsWhiteSpace(right)
            || left is '"' or '\'' or '#' or '{' or '}' or '[' or ']' or ',' or ':'
            || left == '/' && value.Length > 1 && value[1] is '*' or '/'
            || LooksLikeNumber(value) || StartsWithKeyword(value);
        if (!quotes)
        {
            output.Append(separator).Append(value);
            return;
        }

        // Plain quotes if nothing in it would need escaping; then ''' if that can hold it; otherwise escapes.
        if (!value.Any(c => c is '\t' or '\f' or '\b' or '\n' or '\r' or '"' or '\\'))
        {
            output.Append(separator).Append('"').Append(value).Append('"');
        }
        else if (!value.Any(c => c is '\f' or '\b') && !value.Contains("'''") && !string.IsNullOrWhiteSpace(value))
        {
            WriteMultiline(output, value, depth, newline, separator);
        }
        else
        {
            output.Append(separator).Append('"').Append(Escape(value)).Append('"');
        }
    }

    private static void WriteMultiline(StringBuilder output, string value, int depth, string newline, string separator)
    {
        var lines = value.Replace("\r", "").Split('\n');
        if (lines.Length == 1)
        {
            output.Append(separator).Append("'''").Append(value).Append("'''");
            return;
        }

        string indent = new('\t', depth + 1);
        output.Append(newline).Append(indent).Append("'''");
        foreach (string line in lines)
        {
            output.Append(newline);
            if (line.Length > 0) output.Append(indent).Append(line);
        }
        output.Append(newline).Append(indent).Append("'''");
    }

    /// <summary>"12" and "12, then" would read as a number; "12 monkeys" wouldn't.</summary>
    private static bool LooksLikeNumber(string value)
    {
        int i = 0;
        if (value[i] == '-') i++;
        int digits = i;
        while (i < value.Length && char.IsAsciiDigit(value[i])) i++;
        if (i == digits) return false;
        if (i < value.Length && value[i] == '.')
        {
            i++;
            while (i < value.Length && char.IsAsciiDigit(value[i])) i++;
        }
        if (i < value.Length && value[i] is 'e' or 'E')
        {
            i++;
            if (i < value.Length && value[i] is '+' or '-') i++;
            while (i < value.Length && char.IsAsciiDigit(value[i])) i++;
        }
        while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
        return i == value.Length || value[i] is ',' or '}' or ']' or '#' or '/';
    }

    private static bool StartsWithKeyword(string value)
    {
        foreach (string keyword in new[] { "true", "false", "null" })
        {
            if (!value.StartsWith(keyword, StringComparison.Ordinal)) continue;
            int i = keyword.Length;
            while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
            if (i == value.Length || value[i] is ',' or '}' or ']' or '#' or '/' or ':') return true;
        }
        return false;
    }

    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            escaped.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ => c.ToString(),
            });
        }
        return escaped.ToString();
    }
}
