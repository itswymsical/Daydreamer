using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daydreamer.Core.Music;

/// <summary>What happened when songs were added: the ones that made it, and the files that couldn't be copied.</summary>
public sealed record ImportResult(List<Track> Added, List<string> Failed);

/// <summary>
/// The library's Music folder: a copy of every song you've added, plus music.json, which lists
/// them and your playlists. Songs are copied in rather than linked, so the music stays with your
/// stories even if the original files are moved or deleted.
/// </summary>
public sealed class MusicStore
{
    public const string ListFileName = "music.json";
    public const string TrashFolderName = ".trash";

    /// <summary>The kinds of audio file Windows can play without any extra software.</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MusicStore(string folder)
    {
        Folder = folder;
        Library = Load(Path.Combine(folder, ListFileName));
    }

    public string Folder { get; }
    public MusicLibrary Library { get; }

    /// <summary>
    /// True if music.json was there but couldn't be read (OneDrive had it locked, say). It's then never
    /// saved over, so your playlists aren't replaced by the empty list the app started with.
    /// </summary>
    public bool CouldNotLoad { get; private set; }

    public static bool CanPlay(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public string PathOf(Track track) => Path.Combine(Folder, track.FileName);

    public Track? Find(string id) => Library.Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>A playlist's songs, in order (any that were deleted from the library are skipped).</summary>
    public List<Track> TracksOf(Playlist playlist) => playlist.TrackIds.Select(Find).OfType<Track>().ToList();

    // ── Adding songs ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies songs into the Music folder and adds them to the library. The copying happens in the
    /// background so the window stays responsive; files the library already has aren't copied twice.
    /// </summary>
    public async Task<ImportResult> ImportAsync(IEnumerable<string> paths)
    {
        var result = new ImportResult([], []);
        foreach (string path in paths)
        {
            if (!CanPlay(path))
            {
                result.Failed.Add(path);
                continue;
            }
            try
            {
                var known = Library.Tracks.ToList();
                var track = await Task.Run(() => CopyIn(path, known));
                if (!Library.Tracks.Contains(track)) Library.Tracks.Add(track); // back on the window's thread here
                result.Added.Add(track);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed.Add(path);
            }
        }
        return result;
    }

    /// <summary>
    /// Copies one file in and reads its title and artist. Returns the existing song instead if the
    /// library already has this exact file. Doesn't change the library itself (so it can run in the background).
    /// </summary>
    private Track CopyIn(string sourcePath, IReadOnlyList<Track> known)
    {
        var source = new FileInfo(sourcePath);
        foreach (var track in known)
        {
            var copy = new FileInfo(PathOf(track));
            if (copy.Exists && copy.Length == source.Length && SameContents(copy.FullName, source.FullName))
                return track;
        }

        Directory.CreateDirectory(Folder);
        string target;
        if (string.Equals(source.DirectoryName, Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
        {
            target = source.FullName; // already in the Music folder, just not listed yet
        }
        else
        {
            target = UniquePath(Folder, Path.GetFileNameWithoutExtension(sourcePath), source.Extension.ToLowerInvariant());
            File.Copy(sourcePath, target);
        }

        var (title, artist) = AudioTags.Read(target);
        return new Track
        {
            FileName = Path.GetFileName(target),
            Title = title ?? Path.GetFileNameWithoutExtension(sourcePath),
            Artist = artist,
            Added = DateTime.Now,
        };
    }

    private static bool SameContents(string a, string b)
    {
        if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
        using var first = File.OpenRead(a);
        using var second = File.OpenRead(b);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }

    // ── Playlists ───────────────────────────────────────────────────────────

    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = name };
        Library.Playlists.Add(playlist);
        return playlist;
    }

    public void DeletePlaylist(Playlist playlist) => Library.Playlists.Remove(playlist);

    /// <summary>Adds a song to the end of a playlist (a song is only in each playlist once).</summary>
    public void AddToPlaylist(Playlist playlist, Track track)
    {
        if (!playlist.TrackIds.Contains(track.Id)) playlist.TrackIds.Add(track.Id);
    }

    public void RemoveFromPlaylist(Playlist playlist, Track track) => playlist.TrackIds.Remove(track.Id);

    /// <summary>
    /// Takes a song out of the library and every playlist. Its file goes into Music\.trash rather
    /// than being deleted, so it can be recovered.
    /// </summary>
    public void Delete(Track track)
    {
        foreach (var playlist in Library.Playlists) playlist.TrackIds.Remove(track.Id);
        Library.Tracks.Remove(track);

        string file = PathOf(track);
        if (!File.Exists(file)) return;
        try
        {
            string trash = Path.Combine(Folder, TrashFolderName);
            Directory.CreateDirectory(trash);
            File.Move(file, UniquePath(trash, Path.GetFileNameWithoutExtension(file), Path.GetExtension(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use (playing) or locked by OneDrive: the file just stays in the folder, unlisted.
        }
    }

    // ── Saving ──────────────────────────────────────────────────────────────

    public void Save()
    {
        if (CouldNotLoad) return;
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, ListFileName);
        string temp = path + ".saving";
        File.WriteAllText(temp, JsonSerializer.Serialize(Library, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private MusicLibrary Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<MusicLibrary>(File.ReadAllText(path), JsonOptions) ?? new MusicLibrary();
        }
        catch (JsonException)
        {
            // A damaged list shouldn't stop the app (the song files are all still in the folder).
            // Keep a copy of it before it gets saved over, in case it can be fixed by hand.
            try
            {
                File.Copy(path, path + ".damaged", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Couldn't keep a copy; the app still starts.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or unreadable for now (OneDrive syncing, say): start with an empty list this time.
            CouldNotLoad = true;
        }
        return new MusicLibrary();
    }

    /// <summary>"Theme.mp3" → "Theme (2).mp3" if the name is taken.</summary>
    private static string UniquePath(string folder, string name, string extension)
    {
        string candidate = Path.Combine(folder, name + extension);
        for (int i = 2; File.Exists(candidate); i++)
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        return candidate;
    }
}
