namespace Daydreamer.Core.Game;

/// <summary>Where things go in a mod's folder.</summary>
public sealed class GameFolder
{
    /// <summary>Generated definitions go in this folder under <see cref="DefinitionsFolder"/>, apart from hand-written ones.</summary>
    public const string GeneratedFolderName = "Written";

    private GameFolder(string root, string modName)
    {
        Root = root;
        ModName = modName;
    }

    /// <summary>The folder with <c>Localization</c> in it: <c>…\ModSources\TrelamiumIII\src\TrelamiumIII</c>.</summary>
    public string Root { get; }

    /// <summary>The mod's name, from its localization files: "TrelamiumIII".</summary>
    public string ModName { get; }

    public string LocalizationRoot => Path.Combine(Root, "Localization");
    public string LocalizationFileName => $"en-US_Mods.{ModName}.hjson";
    public string CodeRoot => Root;
    public string DefinitionsFolder => Path.Combine(Root, "Core", "Dialogue", "Definitions");

    /// <summary>
    /// Finds the mod in a folder you picked: that folder, or one just inside it (the mod's repository
    /// has it at <c>src\TrelamiumIII</c>). Null if there's no Localization folder to be found.
    /// </summary>
    public static GameFolder? Locate(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        if (TryOpen(folder) is { } direct) return direct;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            if (depth >= 3) continue;
            foreach (string child in Directory.EnumerateDirectories(path))
            {
                string name = Path.GetFileName(child);
                if (name.StartsWith('.') || name is "bin" or "obj") continue;
                if (TryOpen(child) is { } found) return found;
                queue.Enqueue((child, depth + 1));
            }
        }
        return null;
    }

    /// <summary>The mod a localization file belongs to, from the file's own path.</summary>
    public static GameFolder? ForLocalizationFile(string file)
    {
        for (var folder = Path.GetDirectoryName(file); folder != null; folder = Path.GetDirectoryName(folder))
        {
            if (Path.GetFileName(folder).Equals("Localization", StringComparison.OrdinalIgnoreCase))
                return TryOpen(Path.GetDirectoryName(folder)!);
        }
        return null;
    }

    private static GameFolder? TryOpen(string root)
    {
        string localization = Path.Combine(root, "Localization");
        if (!Directory.Exists(localization)) return null;

        // tModLoader's file names say whose they are: en-US_Mods.TrelamiumIII.hjson.
        string? file = Directory.EnumerateFiles(localization, "en-US_Mods.*.hjson", SearchOption.AllDirectories).FirstOrDefault();
        string name = file != null ? Path.GetFileNameWithoutExtension(file)["en-US_Mods.".Length..] : Path.GetFileName(root);
        return new GameFolder(root, name);
    }

    /// <summary>The mod's book classes. Generated ones go in <see cref="GeneratedFolderName"/> here too.</summary>
    public string BookDefinitionsFolder => Path.Combine(Root, "Core", "Books", "Definitions");

    public string BookDefinitionFile(BookDocument book) =>
        Path.Combine(BookDefinitionsFolder, GeneratedFolderName, BookExporter.ClassName(book) + ".cs");

    /// <summary>Where a new book's text goes: <c>Localization\BookLibrary\en-US_Mods.TrelamiumIII.hjson</c>.</summary>
    public string BookLocalizationFile => Path.Combine(LocalizationRoot, BookExporter.LibraryKey, LocalizationFileName);

    public string DefinitionFile(DialogueBook book) =>
        Path.Combine(DefinitionsFolder, GeneratedFolderName, DialogueExporter.ClassName(book) + ".cs");

    /// <summary>The localization file a new character gets: <c>Localization\DialogueLibrary\Silvian\en-US_Mods.TrelamiumIII.hjson</c>.</summary>
    public string NewLocalizationFile(string group) =>
        Path.Combine(LocalizationRoot, DialogueExporter.LibraryKey, group, LocalizationFileName);
}

/// <summary>A file that will change, with what it will say.</summary>
public sealed record GameFileChange(string Path, string Text, bool IsNew);

/// <summary>
/// Everything exporting a document would do, worked out before anything is touched, so it can be shown first.
/// </summary>
public sealed class GameExportPlan
{
    public List<GameFileChange> Changes { get; } = [];

    /// <summary>A generated definition that's no longer needed, because every conversation is hand-written now.</summary>
    public string? DefinitionToRemove { get; set; }

    /// <summary>Conversations the game's text has for this character, but this document doesn't.</summary>
    public List<string> NotInDocument { get; } = [];

    public List<string> Added { get; } = [];
    public List<string> Updated { get; } = [];

    /// <summary>Conversations whose hand-written definition will be replaced by this document's.</summary>
    public List<string> TakesOverFrom { get; } = [];

    /// <summary>Speakers who'll use the plain gray look, with no portrait or voice, because the mod has no profile for them.</summary>
    public List<string> MissingProfiles { get; } = [];

    /// <summary>Choices that lead to a conversation neither the document nor the mod has.</summary>
    public List<string> BrokenBranches { get; } = [];

    /// <summary>Hand-written conversations whose words no longer match their code: the game binds anchors to effects in order.</summary>
    public List<string> MismatchedAnchors { get; } = [];

    /// <summary>A book's words that its hand-written class doesn't ask for, so they'd never reach the page.</summary>
    public List<string> UnreadWords { get; } = [];

    /// <summary>The mod has no class for this book, so this export adds one. Without it the book isn't in the game.</summary>
    public bool NewDefinition { get; set; }

    /// <summary>Why this can't be sent at all. The rest of the plan is empty if so.</summary>
    public string? Refused { get; set; }

    public bool HasChanges => Changes.Count > 0 || DefinitionToRemove != null;
}

/// <summary>
/// Sends a character's document to the game. The words go into whichever localization file already holds that
/// character (or a new one in the usual place), replacing just the conversations the document has, and everything
/// else in that file is left as it is. The flow goes in a generated definition file beside the hand-written ones.
/// </summary>
public static class GameExporter
{
    /// <param name="removeOthers">Also take out conversations the game's text has but the document doesn't.</param>
    public static GameExportPlan Plan(DialogueBook book, GameFolder folder, bool removeOthers = false)
    {
        var plan = new GameExportPlan();
        var definitions = GameDefinitions.Read(folder);
        PlanLocalization(book, folder, removeOthers, plan);
        PlanDefinition(book, folder, definitions, plan);
        CheckBranches(book, folder, definitions, plan);
        CheckHandWritten(book, definitions, plan);
        return plan;
    }

    private static void PlanLocalization(DialogueBook book, GameFolder folder, bool removeOthers, GameExportPlan plan)
    {
        // Every file that has this character's group, so a character kept in a shared file stays there.
        var files = new List<(string Path, string Text, HjsonNode Root, HjsonNode Group)>();
        if (Directory.Exists(folder.LocalizationRoot))
        {
            foreach (string path in Directory.EnumerateFiles(folder.LocalizationRoot, folder.LocalizationFileName, SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                if (!text.Contains(DialogueExporter.LibraryKey, StringComparison.Ordinal)) continue;
                var root = HjsonReader.Parse(text);
                if (root.FindPath(DialogueExporter.LibraryKey, book.Group) is { } group)
                    files.Add((path, text, root, group));
            }
        }

        bool isNew = files.Count == 0;
        if (isNew)
        {
            var root = new HjsonNode("");
            files.Add((folder.NewLocalizationFile(book.Group), "", root, root.Ensure(DialogueExporter.LibraryKey).Ensure(book.Group)));
        }

        foreach (var conversation in book.Conversations)
        {
            var entry = DialogueExporter.ToLocalization(conversation);
            string key = $"{book.Group}.{conversation.Name}";

            var holders = files.Where(f => f.Group.Find(conversation.Name) != null).Select(f => f.Group).ToList();
            if (holders.Count == 0)
            {
                files[0].Group.Children.Add(entry);
                plan.Added.Add(key);
                continue;
            }

            bool changed = false;
            foreach (var holder in holders)
            {
                var existing = holder.Find(conversation.Name)!;
                if (entry.Comments.Count == 0) entry.Comments.AddRange(existing.Comments); // don't lose a note written in the file itself
                changed |= HjsonWriter.Write(Wrap(existing)) != HjsonWriter.Write(Wrap(entry));
                holder.Children[holder.Children.IndexOf(existing)] = entry;
            }
            if (changed) plan.Updated.Add(key);
        }

        var written = new HashSet<string>(book.Conversations.Select(c => c.Name), StringComparer.Ordinal);
        foreach (var (_, _, _, group) in files)
        {
            foreach (var other in group.Children.Where(c => !written.Contains(c.Key)).ToList())
            {
                if (removeOthers) group.Children.Remove(other);
                else plan.NotInDocument.Add($"{book.Group}.{other.Key}");
            }
        }

        foreach (var (path, text, root, _) in files)
        {
            string newline = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
            string updated = HjsonWriter.Write(root, newline);
            if (updated != text) plan.Changes.Add(new GameFileChange(path, updated, isNew));
        }
    }

    private static HjsonNode Wrap(HjsonNode node)
    {
        var root = new HjsonNode("");
        root.Children.Add(node);
        return root;
    }

    private static void PlanDefinition(DialogueBook book, GameFolder folder, GameDefinitions definitions, GameExportPlan plan)
    {
        var newProfiles = new List<string>();
        foreach (string speaker in book.Speakers)
        {
            if (definitions.Profiles.Contains(speaker) || speaker.Equals("Player", StringComparison.OrdinalIgnoreCase)) continue;
            if (book.Color != null && speaker.Equals(book.Speaker, StringComparison.OrdinalIgnoreCase)) newProfiles.Add(speaker);
            else plan.MissingProfiles.Add(speaker);
        }

        foreach (var conversation in book.Conversations.Where(c => !c.HandWritten))
        {
            string key = $"{book.Group}.{conversation.Name}";
            if (definitions.Defines(key)) plan.TakesOverFrom.Add(key);
        }

        string path = folder.DefinitionFile(book);
        string? code = DialogueExporter.ToDefinition(book, folder.ModName, newProfiles);
        bool exists = File.Exists(path);
        if (code == null)
        {
            if (exists) plan.DefinitionToRemove = path;
        }
        else if (!exists || File.ReadAllText(path) != code)
        {
            plan.Changes.Add(new GameFileChange(path, code, !exists));
        }
    }

    private static void CheckBranches(DialogueBook book, GameFolder folder, GameDefinitions definitions, GameExportPlan plan)
    {
        // Other generated files count too: a choice can lead into another character's document.
        var everything = GameDefinitions.Read(folder, includeGenerated: true);
        var inDocument = new HashSet<string>(book.Conversations.Select(c => $"{book.Group}.{c.Name}"), StringComparer.OrdinalIgnoreCase);
        foreach (var conversation in book.Conversations)
        {
            foreach (var choice in conversation.Lines.SelectMany(l => l.Choices))
            {
                if (choice.Branch is { } branch && !inDocument.Contains(branch) && !definitions.Defines(branch) && !everything.Defines(branch))
                    plan.BrokenBranches.Add($"{conversation.Name} → {branch}");
            }
        }
    }

    /// <summary>The words of a hand-written conversation still have to fit its code, which the document can't change.</summary>
    private static void CheckHandWritten(DialogueBook book, GameDefinitions definitions, GameExportPlan plan)
    {
        foreach (var conversation in book.Conversations.Where(c => c.HandWritten))
        {
            string key = $"{book.Group}.{conversation.Name}";
            if (!definitions.Conversations.TryGetValue(key, out var code)) continue;
            if (code.Lines.Count != conversation.Lines.Count)
            {
                plan.MismatchedAnchors.Add($"{conversation.Name} has {conversation.Lines.Count} lines, but its hand-written code has {code.Lines.Count}.");
                continue;
            }
            for (int i = 0; i < code.Lines.Count; i++)
            {
                if (conversation.Lines[i].Affects.Count != code.Lines[i].Affects.Count)
                    plan.MismatchedAnchors.Add($"{conversation.Name} line {i + 1} has {conversation.Lines[i].Affects.Count} pauses or effects, but its hand-written code has {code.Lines[i].Affects.Count}.");
                if (conversation.Lines[i].Choices.Count != code.Lines[i].Choices.Count && code.Lines[i].HandIn == null)
                    plan.MismatchedAnchors.Add($"{conversation.Name} line {i + 1} has {conversation.Lines[i].Choices.Count} choices, but its hand-written code has {code.Lines[i].Choices.Count}.");
            }
        }
    }

    /// <summary>
    /// Writes the plan's files. Anything being replaced gets copied into <paramref name="backupFolder"/> first
    /// (the project's .trash, say), so an export can always be undone by hand.
    /// </summary>
    public static void Apply(GameExportPlan plan, string? backupFolder = null)
    {
        foreach (var change in plan.Changes)
        {
            if (File.Exists(change.Path)) BackUp(change.Path, backupFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
            Storage.Library.WriteAtomically(change.Path, change.Text);
        }

        if (plan.DefinitionToRemove is { } path && File.Exists(path))
        {
            BackUp(path, backupFolder);
            File.Delete(path);
        }
    }

    private static void BackUp(string path, string? backupFolder)
    {
        if (backupFolder == null) return;
        Directory.CreateDirectory(backupFolder);
        // Localization files all share a name, so the folder they're in goes in the copy's name.
        string parent = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        string name = $"{parent} - {Path.GetFileNameWithoutExtension(path)} (replaced {DateTime.Now:yyyy-MM-dd HHmmss}){Path.GetExtension(path)}";
        File.Copy(path, Path.Combine(backupFolder, name), overwrite: true);
    }
}
