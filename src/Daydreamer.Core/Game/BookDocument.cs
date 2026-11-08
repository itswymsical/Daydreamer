using System.Text;
using System.Text.RegularExpressions;

namespace Daydreamer.Core.Game;

/// <summary>
/// A book's document, understood. <c>// book:</c> at the top is what makes a document a book, and says which one:
/// <code>
///   // book: IlluminanceSeries.IlluminanceI     the book's key in the mod (empty: made from the document's title)
///
///   # Illuminance                               the book's title
///
///   ## Ch1: Ch. 1 | False Light                 a chapter: its key and its heading (the key is optional: Ch1, Ch2…)
///   My name is Laine L.                         words right under a heading are its "Opening"
///
///   A blank line starts a new paragraph.        [p] in the game's text. A single line break is a line break
///   ### Excerpt                                 a block with a name of its own, under the chapter: Ch1.Excerpt
///   I ask that you believe me for forty pages.
///   ### /ContentsHeader                         a leading slash means the top of the book, not the chapter
///   Contents
///   // a note                                   notes aren't part of the words. Above a heading, they go in the HJSON as a # comment
///   \# Not a heading                            a backslash at the start of a line means "take this literally"
///   \                                           …and alone, it's an empty line that isn't a new paragraph
/// </code>
/// Keys are what the mod's C# asks for, so importing a book keeps them exactly, and names them all.
/// </summary>
public sealed partial class BookDocument
{
    /// <summary>What a block's words are called if nothing names them.</summary>
    public const string DefaultBlockKey = "Opening";

    public const string TitleKey = "Title";
    public const string ParagraphToken = "[p]";

    [GeneratedRegex(@"^//\s*book\s*:\s*(?<key>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex BookDirectiveRegex();

    // "#", "##" or "###", then a space. "#hashtag" and "####" are just words.
    [GeneratedRegex(@"^(?<level>#{1,3})(?:\s+(?<text>.*))?$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?<key>[\p{L}\p{N}_]+):\s*(?<title>.*)$")]
    private static partial Regex KeyedChapterRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}_]+(?:\.[\p{L}\p{N}_]+)*$")]
    private static partial Regex BookKeyRegex();

    private BookDocument(string key)
    {
        Key = key;
        Root = new HjsonNode(key.Split('.')[^1]);
    }

    /// <summary>The key the mod's definition registers it under, and where it goes in <c>BookLibrary</c>: "Misc.Diary".</summary>
    public string Key { get; }

    /// <summary>The book's entries, in the document's order, as they'd go in the localization file.</summary>
    public HjsonNode Root { get; }

    public List<DialogueIssue> Issues { get; } = [];

    /// <summary>The mod reads its keys as one word per part: <c>/lorebook</c> takes the key with no spaces.</summary>
    public bool HasValidKey => BookKeyRegex().IsMatch(Key);

    /// <summary>Whether this document's text is a book's, rather than a character's.</summary>
    public static bool IsBook(string text)
    {
        foreach (string line in SplitLines(text))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("//", StringComparison.Ordinal)) return false;
            if (BookDirectiveRegex().IsMatch(line.TrimEnd())) return true;
        }
        return false;
    }

    public static BookDocument Parse(string title, string text)
    {
        var lines = SplitLines(text);
        string key = "";
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var directive = BookDirectiveRegex().Match(line.TrimEnd());
            if (directive.Success)
            {
                key = directive.Groups["key"].Value.Trim();
                break;
            }
            if (!line.StartsWith("//", StringComparison.Ordinal)) break;
        }
        if (key.Length == 0) key = DialogueBook.KeyFrom(title);

        var book = new BookDocument(key);
        if (!book.HasValidKey)
            book.Issues.Add(new DialogueIssue(1, $"\"{key}\" can't be a book's key: use letters, numbers and _, with dots between the parts."));
        book.Read(lines);
        return book;
    }

    private void Read(string[] lines)
    {
        var scope = Root; // where a block's name is looked up: the chapter we're in, or the book
        HjsonNode? block = null;
        var body = new List<string>();
        var notes = new List<string>();
        bool directiveSeen = false;
        int chapters = 0;

        void Close()
        {
            if (block != null) block.Value = ReadBody(body);
            block = null;
            body.Clear();
        }

        void Open(string key, int line)
        {
            Close();
            block = Put(key, line, notes);
            notes = [];
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int number = i + 1;

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                if (!directiveSeen && BookDirectiveRegex().IsMatch(line.TrimEnd())) directiveSeen = true;
                else notes.Add(line[2..].Trim());
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (!heading.Success)
            {
                if (block == null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Open(DefaultBlockKey, number);
                }
                body.Add(line);
                continue;
            }

            string text = heading.Groups["text"].Value.Trim();
            if (text.Length == 0)
            {
                Issues.Add(new DialogueIssue(number, $"\"{line.TrimEnd()}\" needs a name after it."));
                continue;
            }

            switch (heading.Groups["level"].Length)
            {
                case 1:
                    Open("/" + TitleKey, number);
                    Close(); // the title is on this line: what's under it is its own block
                    Root.Find(TitleKey)!.Value = text;
                    scope = Root;
                    break;

                case 2:
                    Close();
                    chapters++;
                    var keyed = KeyedChapterRegex().Match(text);
                    string chapterKey = keyed.Success ? keyed.Groups["key"].Value : NextChapterKey();
                    string chapterTitle = keyed.Success ? keyed.Groups["title"].Value.Trim() : text;

                    if (Root.Find(chapterKey) is { Value: null })
                        Issues.Add(new DialogueIssue(number, $"There are two chapters called {chapterKey}; their words will run together."));
                    else if (Root.Find(chapterKey) != null)
                        Issues.Add(new DialogueIssue(number, $"{chapterKey} is already a block, so this chapter replaces its words."));

                    var existing = Root.Find(chapterKey);
                    if (existing is { Value: not null }) Root.Children.Remove(existing);
                    scope = Root.Ensure(chapterKey);
                    if (scope.Comments.Count == 0) scope.Comments.AddRange(notes.Select(n => "# " + n));
                    notes = [];
                    if (chapterTitle.Length > 0) scope.Ensure(TitleKey).Value = chapterTitle;
                    break;

                default:
                    Open(text, number);
                    break;
            }
        }
        Close();

        HjsonNode Put(string key, int line, List<string> comments)
        {
            var node = key.StartsWith('/') ? Root : scope;
            string[] path = key.TrimStart('/').Split('.', StringSplitOptions.TrimEntries);
            for (int p = 0; p < path.Length; p++)
            {
                var next = node.Find(path[p]);
                bool last = p == path.Length - 1;
                if (next == null)
                {
                    next = new HjsonNode(path[p]);
                    node.Children.Add(next);
                }
                else if (last)
                {
                    Issues.Add(new DialogueIssue(line, next.Value != null
                        ? $"There are two blocks called {key.TrimStart('/')}. Only the last one's words will reach the game."
                        : $"{key.TrimStart('/')} is already a chapter, so this block replaces what's in it."));
                    next.Children.Clear();
                }
                else if (next.Value != null)
                {
                    Issues.Add(new DialogueIssue(line, $"{path[p]} is already a block, so this replaces its words."));
                    next.Value = null;
                }
                node = next;
            }
            node.Value = "";
            node.Comments.AddRange(comments.Select(c => "# " + c));
            return node;
        }

        string NextChapterKey()
        {
            while (Root.Find($"Ch{chapters}") != null) chapters++;
            return $"Ch{chapters}";
        }
    }

    // ── A block's words ─────────────────────────────────────────────────────

    /// <summary>The game's text for the lines under a heading. See the class's summary for what they mean.</summary>
    internal static string ReadBody(IReadOnlyList<string> lines)
    {
        var text = new StringBuilder();
        bool first = true, gap = false;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                gap = !first; // blank lines at either end aren't part of the words
                continue;
            }
            if (!first) text.Append(gap ? ParagraphToken : "\n");
            text.Append(line.StartsWith('\\') ? line[1..] : line);
            first = false;
            gap = false;
        }
        return text.ToString();
    }

    /// <summary>
    /// The lines that read back as exactly this text. A <c>[p]</c> becomes a blank line wherever that's
    /// unambiguous; anything else stays written as it is.
    /// </summary>
    public static List<string> WriteBody(string value)
    {
        var pretty = new List<string>();
        foreach (string line in value.Split('\n'))
        {
            var parts = line.Split(ParagraphToken);
            bool open = parts.All(p => !string.IsNullOrWhiteSpace(p));
            if (!open)
            {
                pretty.Add(Escape(line));
                continue;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) pretty.Add("");
                pretty.Add(Escape(parts[i]));
            }
        }
        if (ReadBody(pretty) == value) return pretty;

        // A value like "[p]\n": take no chances, and spell it out.
        return value.Split('\n').Select(Escape).ToList();
    }

    private static string Escape(string line)
    {
        bool literal = string.IsNullOrWhiteSpace(line) || line.StartsWith('\\') || line.StartsWith('#')
                       || line.StartsWith("//", StringComparison.Ordinal);
        return literal ? "\\" + line : line;
    }

    private static string[] SplitLines(string text) => text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');

    // ── Writing an existing book out ────────────────────────────────────────

    /// <summary>The document for a book's entries in a localization file. Parsing it gives those entries back, in that order.</summary>
    public static string Write(string key, HjsonNode book)
    {
        const string newline = "\r\n";
        var output = new StringBuilder();
        output.Append("// book: ").Append(key).Append(newline);

        void Notes(HjsonNode node)
        {
            foreach (string comment in node.Comments) output.Append("// ").Append(DialogueImporter.CommentText(comment)).Append(newline);
        }

        void Block(string name, HjsonNode node)
        {
            output.Append(newline);
            Notes(node);
            output.Append("### ").Append(name).Append(newline);
            foreach (string line in WriteBody(node.Value!)) output.Append(line).Append(newline);
        }

        // Blocks at the top of the book only need their slash once a chapter has started.
        bool inChapter = false;
        for (int i = 0; i < book.Children.Count; i++)
        {
            var node = book.Children[i];
            if (node.Value != null)
            {
                if (i == 0 && node.Key == TitleKey && IsOneLine(node.Value))
                {
                    output.Append(newline);
                    Notes(node);
                    output.Append("# ").Append(node.Value).Append(newline);
                }
                else
                {
                    Block((inChapter ? "/" : "") + node.Key, node);
                }
                continue;
            }

            if (!KeyedChapterRegex().IsMatch(node.Key + ":"))
            {
                foreach (var (path, leaf) in Leaves(node, node.Key)) Block("/" + path, leaf);
                continue;
            }

            inChapter = true;
            var title = node.Children.FirstOrDefault();
            bool hasHeading = title is { Key: TitleKey, Value: not null } && IsOneLine(title.Value);

            output.Append(newline);
            Notes(node);
            if (hasHeading) Notes(title!);
            output.Append("## ").Append(node.Key).Append(':');
            if (hasHeading) output.Append(' ').Append(title!.Value);
            output.Append(newline);

            foreach (var child in node.Children.Skip(hasHeading ? 1 : 0))
            {
                if (child.Value != null) Block(child.Key, child);
                else foreach (var (path, leaf) in Leaves(child, child.Key)) Block(path, leaf);
            }
        }
        return output.ToString();
    }

    private static bool IsOneLine(string value) => value.Length > 0 && !value.Contains('\n') && value == value.Trim();

    private static IEnumerable<(string Path, HjsonNode Leaf)> Leaves(HjsonNode node, string path)
    {
        foreach (var child in node.Children)
        {
            string key = $"{path}.{child.Key}";
            if (child.Value != null) yield return (key, child);
            else foreach (var leaf in Leaves(child, key)) yield return leaf;
        }
    }
}

/// <summary>A book that's already in the game, written out as a document.</summary>
public sealed record ImportedBook(string Key, string Title, string Text);

/// <summary>Brings a localization file's books into Daydreamer: one document per book.</summary>
public static class BookImporter
{
    /// <summary>Every book under <c>BookLibrary</c> in a localization file. Files without books give none.</summary>
    public static List<ImportedBook> Read(string hjson)
    {
        var results = new List<ImportedBook>();
        if (HjsonReader.Parse(hjson).Find(BookExporter.LibraryKey) is not { } library) return results;
        foreach (var node in library.Children.Where(c => c.Value == null)) Collect(node, node.Key, results);
        return results;
    }

    /// <summary>A book has words of its own, and a group of books (<c>Misc</c>) only has more entries.</summary>
    private static void Collect(HjsonNode node, string key, List<ImportedBook> results)
    {
        if (node.Children.Any(c => c.Value != null))
        {
            string title = node.Find(BookDocument.TitleKey)?.Value is { Length: > 0 } named ? named.Split('\n')[0] : node.Key;
            results.Add(new ImportedBook(key, title, BookDocument.Write(key, node)));
            return;
        }
        foreach (var child in node.Children) Collect(child, $"{key}.{child.Key}", results);
    }
}
