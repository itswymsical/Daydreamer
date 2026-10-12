using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Storage;

/// <summary>A document (chapter) inside a project. The file name is the title.</summary>
public sealed class DocumentInfo
{
    public DocumentInfo(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
    public string Title => Path.GetFileNameWithoutExtension(FilePath);
    public DateTime Modified => File.Exists(FilePath) ? File.GetLastWriteTime(FilePath) : DateTime.MinValue;

    public override string ToString() => Title;
}

/// <summary>
/// Everything on disk lives under one root folder:
/// <code>
///   Daydreamer/
///     My Manga/               ← a project
///       project.json          ← characters + formatting rules
///       Chapter 1.dream       ← documents are plain UTF-8 text
///       .trash/               ← deleted documents go here, never straight to oblivion
///     Music/                  ← the music library: copies of your songs, and music.json (not a project)
/// </code>
/// Documents being plain text means your writing is always readable, even without this app.
/// </summary>
public sealed class Library
{
    public const string DocumentExtension = ".dream";
    public const string ProjectFileName = "project.json";
    public const string TrashFolderName = ".trash";

    /// <summary>
    /// The music library (songs and playlists, see Music/MusicStore.cs) lives in this folder beside the
    /// projects. It isn't a project, so it's left out of the project list and no project can take its name.
    /// </summary>
    public const string MusicFolderName = "Music";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public Library(string rootPath)
    {
        RootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public string RootPath { get; }

    public string MusicFolder => Path.Combine(RootPath, MusicFolderName);

    // ── Projects ────────────────────────────────────────────────────────────

    public List<string> ListProjectFolders() =>
        Directory.EnumerateDirectories(RootPath)
            .Where(d => !Path.GetFileName(d).StartsWith('.') && !IsMusicFolderName(Path.GetFileName(d)))
            .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public Project LoadProject(string folder)
    {
        string file = Path.Combine(folder, ProjectFileName);
        Project project;
        if (File.Exists(file))
        {
            project = JsonSerializer.Deserialize<Project>(File.ReadAllText(file), JsonOptions) ?? new Project();
        }
        else
        {
            project = new Project();
        }
        project.FolderPath = folder;
        project.Styles.EnsureAllElements();
        ProjectUpgrades.Apply(project);
        return project;
    }

    public void SaveProject(Project project)
    {
        string json = JsonSerializer.Serialize(project, JsonOptions);
        WriteAtomically(Path.Combine(project.FolderPath, ProjectFileName), json);
    }

    public Project CreateProject(string name)
    {
        string folder = UniquePath(RootPath, ProjectFolderName(name, "Untitled project"), extension: "");
        Directory.CreateDirectory(folder);
        var project = new Project { FolderPath = folder };
        SaveProject(project);
        return project;
    }

    /// <summary>Renames the project folder. Returns the new folder path.</summary>
    public string RenameProject(Project project, string newName)
    {
        string safe = ProjectFolderName(newName, project.Name);
        if (safe == project.Name) return project.FolderPath;

        string target = UniquePath(RootPath, safe, extension: "");
        Directory.Move(project.FolderPath, target);
        project.FolderPath = target;
        return target;
    }

    // ── Documents ───────────────────────────────────────────────────────────

    public List<DocumentInfo> ListDocuments(Project project) =>
        Directory.EnumerateFiles(project.FolderPath, "*" + DocumentExtension)
            .Select(f => new DocumentInfo(f))
            .OrderBy(d => d.Title, NaturalStringComparer.Instance)
            .ToList();

    public DocumentInfo CreateDocument(Project project, string title, string initialText = "")
    {
        string path = UniquePath(project.FolderPath, SafeName(title, "Untitled document"), DocumentExtension);
        WriteAtomically(path, initialText);
        return new DocumentInfo(path);
    }

    public DocumentInfo RenameDocument(DocumentInfo document, string newTitle)
    {
        string safe = SafeName(newTitle, document.Title);
        if (safe == document.Title) return document;

        string folder = Path.GetDirectoryName(document.FilePath)!;
        string target = UniquePath(folder, safe, DocumentExtension);
        File.Move(document.FilePath, target);
        return new DocumentInfo(target);
    }

    /// <summary>Moves a document into the project's .trash folder (it can be recovered from there).</summary>
    public void TrashDocument(DocumentInfo document)
    {
        string trash = Path.Combine(Path.GetDirectoryName(document.FilePath)!, TrashFolderName);
        Directory.CreateDirectory(trash);
        string stamped = $"{document.Title} (deleted {DateTime.Now:yyyy-MM-dd HHmm})";
        File.Move(document.FilePath, UniquePath(trash, stamped, DocumentExtension));
    }

    public string ReadDocument(DocumentInfo document) =>
        File.Exists(document.FilePath) ? File.ReadAllText(document.FilePath, Encoding.UTF8) : "";

    // ── Fonts ───────────────────────────────────────────────────────────────

    public const string FontsFolderName = "fonts";

    /// <summary>Where a project keeps fonts that came with imported documents (so they work without installing them).</summary>
    public static string FontsFolder(Project project) => Path.Combine(project.FolderPath, FontsFolderName);

    /// <summary>Saves font files into the project's fonts folder. Files that are already there are kept.</summary>
    public void SaveFonts(Project project, IEnumerable<(string FileName, byte[] Data)> fonts)
    {
        string folder = FontsFolder(project);
        foreach (var (fileName, data) in fonts)
        {
            string path = Path.Combine(folder, SafeName(fileName, "font.ttf"));
            if (File.Exists(path)) continue;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, data);
        }
    }

    public void WriteDocument(DocumentInfo document, string text) => WriteAtomically(document.FilePath, text);

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes to a temporary file first and then swaps it in, so a crash or power cut
    /// mid-save can never leave you with a half-written document.
    /// </summary>
    private static void WriteAtomically(string path, string contents)
    {
        string temp = path + ".saving";
        File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Strips characters Windows doesn't allow in file names.</summary>
    public static string SafeName(string? name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    /// <summary>A safe folder name for a project. "Music" belongs to the music library, so a project called that gets "Music (2)".</summary>
    private static string ProjectFolderName(string? name, string fallback)
    {
        string safe = SafeName(name, fallback);
        return IsMusicFolderName(safe) ? $"{safe} (2)" : safe;
    }

    private static bool IsMusicFolderName(string? name) => string.Equals(name, MusicFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Chapter 1" → "Chapter 1 (2)" if the name is already taken.</summary>
    private static string UniquePath(string folder, string name, string extension)
    {
        string candidate = Path.Combine(folder, name + extension);
        for (int i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        return candidate;
    }
}

/// <summary>Sorts "Chapter 2" before "Chapter 10" (plain alphabetical sorting gets this wrong).</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                string nx = x[si..i].TrimStart('0'), ny = y[sj..j].TrimStart('0');
                int byLength = nx.Length.CompareTo(ny.Length);
                if (byLength != 0) return byLength;
                int byDigits = string.CompareOrdinal(nx, ny);
                if (byDigits != 0) return byDigits;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
