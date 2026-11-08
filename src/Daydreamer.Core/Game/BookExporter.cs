using System.Text;
using System.Text.RegularExpressions;

namespace Daydreamer.Core.Game;

/// <summary>What the mod's own <c>ModBook</c> classes ask for, read from their source.</summary>
public sealed partial class BookDefinitions
{
    // Any call with a string first: .Prose("Body"), .Chapter(chapter) is a helper's, .Quote($"{chapter}.Excerpt").
    [GeneratedRegex(@"\.\w+\(\s*(?<template>\$?)""(?<key>[^""]+)""")]
    private static partial Regex KeyArgumentRegex();

    // A helper called for each chapter: Chapter(book, "Ch1").
    [GeneratedRegex(@"\b\w+\(\s*\w+\s*,\s*""(?<name>[^""]+)""")]
    private static partial Regex HelperCallRegex();

    [GeneratedRegex(@"\bclass\s+\w+\s*:\s*ModBook\b")]
    private static partial Regex BookClassRegex();

    [GeneratedRegex(@"\bclass\s+\w+")]
    private static partial Regex AnyClassRegex();

    [GeneratedRegex(@"Key\s*=>\s*""(?<key>[^""]+)""")]
    private static partial Regex KeyPropertyRegex();

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline)]
    private static partial Regex CommentLineRegex();

    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex HoleRegex();

    private readonly Dictionary<string, HashSet<string>?> _books = new(StringComparer.OrdinalIgnoreCase);

    public static BookDefinitions Read(GameFolder folder)
    {
        var result = new BookDefinitions();
        if (!Directory.Exists(folder.BookDefinitionsFolder)) return result;
        foreach (string path in Directory.EnumerateFiles(folder.BookDefinitionsFolder, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(Path.GetDirectoryName(path)) == GameFolder.GeneratedFolderName) continue;
            result.ReadFile(File.ReadAllText(path));
        }
        return result;
    }

    /// <summary>Takes in one file's source. It can hold more than one book.</summary>
    public void ReadFile(string source)
    {
        string code = CommentLineRegex().Replace(source, "");
        var classes = AnyClassRegex().Matches(code);
        for (int i = 0; i < classes.Count; i++)
        {
            int start = classes[i].Index, end = i + 1 < classes.Count ? classes[i + 1].Index : code.Length;
            string body = code[start..end];
            if (!BookClassRegex().IsMatch(body)) continue;
            if (KeyPropertyRegex().Match(body) is { Success: true } key) _books[key.Groups["key"].Value] = ReadsOf(body);
        }
    }

    /// <summary>The keys a class reads, or null if they're worked out in a way this can't follow (a loop, say).</summary>
    private static HashSet<string>? ReadsOf(string body)
    {
        var names = HelperCallRegex().Matches(body).Select(m => m.Groups["name"].Value).ToList();
        var reads = new HashSet<string>(StringComparer.Ordinal) { BookDocument.TitleKey };
        foreach (Match call in KeyArgumentRegex().Matches(body))
        {
            string key = call.Groups["key"].Value;
            if (call.Groups["template"].Length == 0 || !HoleRegex().IsMatch(key))
            {
                reads.Add(key);
                continue;
            }
            if (names.Count == 0) return null;
            foreach (string name in names) reads.Add(HoleRegex().Replace(key, name));
        }
        // A chapter reads {key}.Title, and falls back to {key}.
        foreach (string key in reads.ToList()) reads.Add($"{key}.{BookDocument.TitleKey}");

        // A chapter whose key is passed through a helper's parameter: .Chapter(chapter).
        foreach (string name in names) reads.Add($"{name}.{BookDocument.TitleKey}");
        return reads;
    }

    public bool Defines(string key) => _books.ContainsKey(key);

    /// <summary>Whether the book's code reads this entry. True when the code can't be followed, so it's never a false alarm.</summary>
    public bool Reads(string book, string entry) =>
        !_books.TryGetValue(book, out var reads) || reads == null || reads.Contains(entry);
}

/// <summary>
/// Sends a book's document to the game. Its words go in the file under <c>Localization\BookLibrary</c> that holds the
/// book (or the usual one, which also keeps the glossary and the book UI's text): only the document's own entries
/// change, and everything else stays. A book the mod has no definition for can have one generated.
/// </summary>
public static class BookExporter
{
    public const string LibraryKey = "BookLibrary";

    /// <param name="removeOthers">Also take out entries the book has in the game but not in the document.</param>
    /// <param name="createDefinition">Generate a definition if the mod has none. Without one the book's words are never shown.</param>
    public static GameExportPlan Plan(BookDocument book, GameFolder folder, bool removeOthers = false, bool createDefinition = true)
    {
        var plan = new GameExportPlan();
        var definitions = BookDefinitions.Read(folder);
        string[] path = book.Key.Split('.');

        var files = ReadFiles(folder);
        if (Refusal(book, path, files, definitions) is { } refusal)
        {
            plan.Refused = refusal;
            return plan;
        }

        var holders = files.Where(f => f.Root.Find(LibraryKey)?.FindPath(path) != null).ToList();
        bool isNew = false;
        if (holders.Count == 0)
        {
            string target = folder.BookLocalizationFile;
            var file = files.FirstOrDefault(f => PathsMatch(f.Path, target));
            if (file.Root == null) file = files.FirstOrDefault(f => f.Root.Find(LibraryKey) != null);
            if (file.Root == null)
            {
                isNew = !File.Exists(target);
                file = (target, isNew ? "" : File.ReadAllText(target), isNew ? new HjsonNode("") : HjsonReader.Parse(File.ReadAllText(target)));
            }
            var node = file.Root.Ensure(LibraryKey);
            foreach (string part in path) node = node.Ensure(part);
            holders.Add(file);
        }

        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in holders)
            foreach (var pair in file.Root.Find(LibraryKey)!.FindPath(path)!.Flatten()) before.TryAdd(pair.Key, pair.Value);

        foreach (var pair in book.Root.Flatten())
        {
            if (!before.TryGetValue(pair.Key, out string? was)) plan.Added.Add($"{book.Key}.{pair.Key}");
            else if (was != pair.Value) plan.Updated.Add($"{book.Key}.{pair.Key}");
        }

        var notInDocument = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in holders)
            Merge(file.Root.Find(LibraryKey)!.FindPath(path)!, book.Root, book.Key, removeOthers, notInDocument);
        plan.NotInDocument.AddRange(notInDocument);

        foreach (var (filePath, text, root) in holders)
        {
            string newline = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
            string updated = HjsonWriter.Write(root, newline);
            if (updated != text) plan.Changes.Add(new GameFileChange(filePath, updated, isNew));
        }

        PlanDefinition(book, folder, definitions, createDefinition, plan);
        return plan;
    }

    private static List<(string Path, string Text, HjsonNode Root)> ReadFiles(GameFolder folder)
    {
        var files = new List<(string, string, HjsonNode)>();
        if (!Directory.Exists(folder.LocalizationRoot)) return files;
        foreach (string path in Directory.EnumerateFiles(folder.LocalizationRoot, folder.LocalizationFileName, SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            if (text.Contains(LibraryKey, StringComparison.Ordinal)) files.Add((path, text, HjsonReader.Parse(text)));
        }
        return files;
    }

    private static bool PathsMatch(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A key that would put this document on top of other books.</summary>
    private static string? Refusal(BookDocument book, string[] path, List<(string Path, string Text, HjsonNode Root)> files, BookDefinitions definitions)
    {
        if (!book.HasValidKey) return $"\"{book.Key}\" can't be a book's key. Use letters, numbers and _, with dots between the parts.";

        foreach (var (_, _, root) in files)
        {
            var node = root.Find(LibraryKey);
            for (int i = 0; node != null && i < path.Length; i++)
            {
                node = node.Find(path[i]);
                if (node?.Value != null || i < path.Length - 1 && node != null && node.Children.Any(c => c.Value != null))
                    return $"{string.Join('.', path[..(i + 1)])} is a book already, and {book.Key} would go inside it.";
            }
            if (node == null || definitions.Defines(book.Key) || node.Children.Any(c => c.Value != null)) continue;

            var others = node.Children.Where(c => c.Children.Any(g => g.Value != null) && book.Root.Find(c.Key) == null).Select(c => c.Key).ToList();
            if (others.Count > 0) return $"{book.Key} is a group of books ({string.Join(", ", others)}), not a book. Give this document a key of its own.";
        }
        return null;
    }

    /// <summary>
    /// Puts the document's entries into the book's, in the document's order. An entry the document doesn't
    /// have stays after the one it followed, and a note written in the file itself survives.
    /// </summary>
    private static void Merge(HjsonNode into, HjsonNode from, string prefix, bool removeOthers, HashSet<string> notInDocument)
    {
        var old = into.Children.ToList();
        var merged = new List<HjsonNode>();
        foreach (var entry in from.Children)
        {
            var existing = into.Find(entry.Key);
            if (existing is { Value: null } && entry.Value == null)
            {
                Merge(existing, entry, $"{prefix}.{entry.Key}", removeOthers, notInDocument);
                if (entry.Comments.Count > 0)
                {
                    existing.Comments.Clear();
                    existing.Comments.AddRange(entry.Comments);
                }
                merged.Add(existing);
                continue;
            }

            var copy = Copy(entry);
            if (copy.Comments.Count == 0 && existing != null) copy.Comments.AddRange(existing.Comments);
            merged.Add(copy);
        }

        for (int i = 0; i < old.Count; i++)
        {
            if (from.Find(old[i].Key) != null) continue;
            if (removeOthers) continue;

            if (old[i].Value != null) notInDocument.Add($"{prefix}.{old[i].Key}");
            else foreach (var pair in old[i].Flatten($"{prefix}.{old[i].Key}")) notInDocument.Add(pair.Key);

            string? previous = i == 0 ? null : old[i - 1].Key;
            int at = previous == null ? 0 : merged.FindIndex(m => m.Key == previous) + 1;
            merged.Insert(at, old[i]);
        }

        into.Children.Clear();
        into.Children.AddRange(merged);
    }

    /// <summary>The document's own tree stays as it was, so one document can be merged into two files.</summary>
    private static HjsonNode Copy(HjsonNode node)
    {
        var copy = new HjsonNode(node.Key, node.Value);
        copy.Comments.AddRange(node.Comments);
        copy.Children.AddRange(node.Children.Select(Copy));
        return copy;
    }

    private static void PlanDefinition(BookDocument book, GameFolder folder, BookDefinitions definitions, bool createDefinition, GameExportPlan plan)
    {
        string path = folder.BookDefinitionFile(book);
        bool exists = File.Exists(path);

        if (definitions.Defines(book.Key))
        {
            // The mod's own class is in charge of the layout. Words it doesn't ask for never reach the page.
            foreach (var pair in book.Root.Flatten())
            {
                if (!definitions.Reads(book.Key, pair.Key)) plan.UnreadWords.Add($"{book.Key}.{pair.Key}");
            }
            if (exists) plan.DefinitionToRemove = path;
            return;
        }

        if (!exists && !createDefinition) return;
        if (!exists) plan.NewDefinition = true;

        string code = ToDefinition(book, folder.ModName);
        if (!exists || File.ReadAllText(path) != code)
            plan.Changes.Add(new GameFileChange(path, code, !exists));
    }

    public static string ClassName(BookDocument book)
    {
        string name = string.Concat(book.Key.Where(c => char.IsLetterOrDigit(c) || c == '_'));
        return char.IsAsciiDigit(name.FirstOrDefault('0')) ? $"Book{name}" : $"{name}Book";
    }

    /// <summary>
    /// The definition for a book the mod doesn't have one for: the title, then each entry in order. It follows the
    /// shape of the mod's own books (a title page with contents, a drop cap to open each chapter, justified prose).
    /// </summary>
    public static string ToDefinition(BookDocument book, string modName)
    {
        const string newline = "\r\n";
        var calls = new List<string>();
        bool hasContents = book.Root.Find("ContentsHeader")?.Value != null;

        foreach (var entry in book.Root.Children)
        {
            if (entry.Value == null)
            {
                calls.Add($".Chapter({Quoted(entry.Key)})");
                AddBlocks(entry.Flatten(entry.Key).Where(p => p.Key != $"{entry.Key}.{BookDocument.TitleKey}").Select(p => p.Key).ToList(), calls);
            }
            else if (entry.Key == BookDocument.TitleKey)
            {
                if (hasContents)
                {
                    calls.Add(".Space(32f)");
                    calls.Add($".Heading({Quoted(entry.Key)}, rule: false, BlockStyle.Heading with {{ Scale = 0.9f, Align = TextAlign.Center }})");
                    calls.Add(".Rule(span: 0.45f)");
                    calls.Add(".Space(12f)");
                }
                else
                {
                    calls.Add($".Heading({Quoted(entry.Key)})");
                }
            }
            else if (entry.Key == "ContentsHeader")
            {
                calls.Add($".Contents({Quoted(entry.Key)})");
            }
            else
            {
                AddBlocks([entry.Key], calls);
            }
        }
        if (calls.Count == 0) calls.Add(".Space(0f)");

        var code = new StringBuilder();
        void Line(string text = "") => code.Append(text).Append(newline);

        Line("// <auto-generated>");
        Line($"//   Written by Daydreamer from the book \"{book.Key}\". Change the document and export it again:");
        Line("//   anything edited here is replaced the next time it's exported. To take the layout over by hand,");
        Line("//   move this class out of the Written folder.");
        Line("// </auto-generated>");
        Line($"using {modName}.Core.Books.Layout;");
        Line($"using {modName}.Core.Books.Text;");
        Line();
        Line($"namespace {modName}.Core.Books.Definitions.{GameFolder.GeneratedFolderName};");
        Line();
        Line($"public sealed class {ClassName(book)} : ModBook");
        Line("{");
        Line($"    public override string Key => {Quoted(book.Key)};");
        Line();
        Line("    protected override void Build(BookBuilder book) => book");
        for (int i = 0; i < calls.Count; i++) Line($"        {calls[i]}{(i == calls.Count - 1 ? ";" : "")}");
        Line("}");
        return code.ToString();
    }

    /// <summary>The mod's naming tells what a block is: an Excerpt is a quote, and an AsideTitle and AsideBody make a panel.</summary>
    private static void AddBlocks(List<string> keys, List<string> calls)
    {
        bool opening = calls.LastOrDefault()?.StartsWith(".Chapter(", StringComparison.Ordinal) == true;
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i], name = key.Split('.')[^1];
            if (name.StartsWith("Excerpt", StringComparison.Ordinal))
            {
                calls.Add($".Quote({Quoted(key)})");
            }
            else if (name == "AsideTitle" && i + 1 < keys.Count && keys[i + 1].Split('.')[^1] == "AsideBody")
            {
                calls.Add($".Panel(panel => panel.Heading({Quoted(key)}, rule: false, BlockStyle.Heading with {{ Scale = 0.45f }}).Prose({Quoted(keys[++i])}, BlockStyle.Body with {{ Scale = 0.74f, SpaceAfter = 0f }}))");
            }
            else
            {
                calls.Add($".Prose({Quoted(key)}, BlockStyle.Body with {{ {(opening ? "DropCap = true, " : "")}Align = TextAlign.Justify }})");
                opening = false;
            }
        }
    }

    private static string Quoted(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
