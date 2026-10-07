using System.Text;
using Daydreamer.Core.Music;
using Daydreamer.Core.Storage;

namespace Daydreamer.Core.Tests;

/// <summary>
/// The music channel's decisions (what plays next, crossfades, cutting in line), checked with pretend
/// players: each "deck" just remembers what it was told to do.
/// </summary>
public class MusicChannelTests
{
    private sealed class FakeDeck : IAudioPlayer
    {
        public string? File { get; private set; }
        public bool Playing { get; private set; }
        public double Volume { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan? Duration { get; private set; }

        public event EventHandler? Ended;
        public event EventHandler? Failed;

        public void Play(string path)
        {
            File = path;
            Playing = true;
            Position = TimeSpan.Zero;
            Duration = TimeSpan.FromMinutes(3);
        }

        public void Pause() => Playing = false;
        public void Resume() => Playing = File != null;

        public void Stop()
        {
            File = null;
            Playing = false;
            Duration = null;
            Position = TimeSpan.Zero;
        }

        public void End() => Ended?.Invoke(this, EventArgs.Empty);
        public void Fail() => Failed?.Invoke(this, EventArgs.Empty);
    }

    private readonly FakeDeck _a = new(), _b = new();
    private readonly MusicChannel _channel;
    private readonly List<Track> _songs = [Song("One"), Song("Two"), Song("Three")];

    public MusicChannelTests()
    {
        // Each song's "file" is just its title, so the decks show what they're playing.
        _channel = new MusicChannel(_a, _b, track => track.Title) { Volume = 0.8 };
    }

    private static Track Song(string title) => new() { Title = title, FileName = title + ".mp3" };

    /// <summary>The deck playing the current song.</summary>
    private FakeDeck Live => _a.File == _channel.Current?.Title ? _a : _b;
    private FakeDeck Other => Live == _a ? _b : _a;

    [Fact]
    public void Plays_the_chosen_song_at_full_volume()
    {
        _channel.PlayPlaylist(_songs, 1);

        Assert.Equal("Two", _channel.Current?.Title);
        Assert.True(_channel.IsPlaying);
        Assert.True(Live.Playing);
        Assert.Equal(0.8, Live.Volume);
        Assert.Null(Other.File);
    }

    [Fact]
    public void Crossfades_into_the_next_song_near_the_end()
    {
        _channel.PlayPlaylist(_songs, 0);
        var first = Live;
        first.Position = first.Duration!.Value - TimeSpan.FromSeconds(2); // inside the 3-second crossfade

        _channel.Tick(TimeSpan.FromMilliseconds(50));
        var second = Live;
        Assert.Equal("Two", second.File);
        Assert.NotSame(first, second);
        Assert.True(first.Playing); // both play while they fade

        _channel.Tick(TimeSpan.FromSeconds(1.5)); // halfway: both at about 70% ("equal power")
        Assert.Equal(0.8 * Math.Sqrt(0.5), first.Volume, precision: 2);
        Assert.Equal(0.8 * Math.Sqrt(0.5), second.Volume, precision: 2);

        _channel.Tick(TimeSpan.FromSeconds(1.6));
        Assert.Null(first.File); // the old song has stopped
        Assert.Equal(0.8, second.Volume, precision: 6);
    }

    [Fact]
    public void Plays_on_through_the_playlist_and_round_again()
    {
        _channel.CrossfadeLength = TimeSpan.Zero;
        _channel.PlayPlaylist(_songs, 1);

        Live.End();
        Assert.Equal("Three", _channel.Current?.Title);
        Live.End();
        Assert.Equal("One", _channel.Current?.Title);
    }

    [Fact]
    public void A_song_can_cut_in_line_and_the_playlist_carries_on_after_it()
    {
        _channel.PlayPlaylist(_songs, 0);
        var cue = Song("Scene 7 theme");

        _channel.PlayNow(cue);
        Assert.Equal("Scene 7 theme", _channel.Current?.Title);
        Assert.Equal(0, Live.Volume); // it fades in over the song that was playing...
        Assert.Equal("One", Other.File);

        _channel.Tick(TimeSpan.FromSeconds(3.1));
        Assert.Null(Other.File); // ...which has faded out

        Live.End();
        Assert.Equal("Two", _channel.Current?.Title);
    }

    [Fact]
    public void Play_next_waits_for_the_current_song()
    {
        _channel.PlayPlaylist(_songs, 0);
        _channel.PlayNext(Song("Interlude"));
        Assert.Equal("One", _channel.Current?.Title);

        Live.End();
        Assert.Equal("Interlude", _channel.Current?.Title);
        Live.End();
        Assert.Equal("Two", _channel.Current?.Title);
    }

    [Fact]
    public void A_song_that_will_not_play_is_skipped()
    {
        _channel.PlayPlaylist(_songs, 0);

        Live.Fail();

        Assert.Equal("Two", _channel.Current?.Title);
        Assert.Equal("Couldn't play \"One\".", _channel.Problem);
    }

    [Fact]
    public void Stops_rather_than_trying_forever_when_nothing_will_play()
    {
        _channel.PlayPlaylist(_songs, 0);

        for (int i = 0; i < 10 && _channel.Current != null; i++) Live.Fail();

        Assert.Null(_channel.Current);
        Assert.False(_channel.IsPlaying);
        Assert.NotNull(_channel.Problem);
    }

    [Fact]
    public void Pausing_holds_a_crossfade_where_it_is()
    {
        _channel.PlayPlaylist(_songs, 0);
        _channel.PlayNow(Song("Cue"));
        _channel.Tick(TimeSpan.FromSeconds(1));
        double volume = Live.Volume;

        _channel.Pause();
        _channel.Tick(TimeSpan.FromSeconds(5));
        Assert.False(_a.Playing || _b.Playing);
        Assert.Equal(volume, Live.Volume);

        _channel.Resume();
        Assert.True(_a.Playing && _b.Playing); // still mid-fade
    }

    [Fact]
    public void Previous_restarts_the_song_unless_it_has_only_just_begun()
    {
        _channel.PlayPlaylist(_songs, 1);
        Live.Position = TimeSpan.FromSeconds(40);

        _channel.Previous();
        Assert.Equal("Two", _channel.Current?.Title);
        Assert.Equal(TimeSpan.Zero, Live.Position);

        _channel.Previous();
        Assert.Equal("One", _channel.Current?.Title);
    }

    [Fact]
    public void Editing_the_playlist_does_not_interrupt_the_song()
    {
        _channel.CrossfadeLength = TimeSpan.Zero;
        _channel.PlayPlaylist(_songs, 1); // Two

        _channel.UpdatePlaylist([_songs[2], _songs[1], _songs[0]]); // Three, Two, One
        Assert.Equal("Two", _channel.Current?.Title);
        Live.End();
        Assert.Equal("One", _channel.Current?.Title);
    }

    [Fact]
    public void Removing_the_playing_song_carries_on_from_the_one_after_it()
    {
        _channel.CrossfadeLength = TimeSpan.Zero;
        _channel.PlayPlaylist(_songs, 1); // Two

        _channel.UpdatePlaylist([_songs[0], _songs[2]]); // Two removed
        Live.End();
        Assert.Equal("Three", _channel.Current?.Title);
    }

    [Fact]
    public void Volume_changes_the_song_that_is_playing()
    {
        _channel.PlayPlaylist(_songs, 0);
        _channel.Volume = 0.3;
        Assert.Equal(0.3, Live.Volume);
    }
}

/// <summary>The Music folder: copying songs in, playlists, deleting, and saving music.json.</summary>
public sealed class MusicStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daydreamer-music-" + Guid.NewGuid().ToString("N"));
    private readonly string _musicFolder;
    private readonly string _elsewhere; // where the "original" files are, like a Downloads folder

    public MusicStoreTests()
    {
        _musicFolder = Path.Combine(_root, "Music");
        _elsewhere = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(_elsewhere);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string MakeFile(string name, byte[] contents)
    {
        string path = Path.Combine(_elsewhere, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static byte[] FakeAudio(int seed) => Enumerable.Range(0, 2000).Select(i => (byte)(i * seed)).ToArray();

    [Fact]
    public async Task Adding_a_song_copies_it_into_the_music_folder_with_its_tags()
    {
        string original = MakeFile("track01.mp3", [.. AudioTagsTests.Id3v23("Reverie Theme", "Sig"), .. FakeAudio(3)]);
        var store = new MusicStore(_musicFolder);

        var result = await store.ImportAsync([original]);

        var track = Assert.Single(result.Added);
        Assert.Empty(result.Failed);
        Assert.Equal("Reverie Theme", track.Title);
        Assert.Equal("Sig", track.Artist);
        Assert.True(File.Exists(store.PathOf(track)));
        Assert.Equal(_musicFolder, Path.GetDirectoryName(store.PathOf(track)));
        Assert.Contains(track, store.Library.Tracks);

        File.Delete(original); // the copy doesn't depend on the original
        Assert.True(File.Exists(store.PathOf(track)));
    }

    [Fact]
    public async Task A_song_without_tags_is_named_after_its_file()
    {
        var store = new MusicStore(_musicFolder);
        var result = await store.ImportAsync([MakeFile("Rainy Rooftop.mp3", FakeAudio(5))]);
        Assert.Equal("Rainy Rooftop", Assert.Single(result.Added).Title);
    }

    [Fact]
    public async Task Adding_the_same_song_twice_keeps_one_copy()
    {
        string path = MakeFile("Theme.mp3", FakeAudio(7));
        var store = new MusicStore(_musicFolder);

        var first = await store.ImportAsync([path]);
        var second = await store.ImportAsync([path]);

        Assert.Same(first.Added[0], second.Added[0]);
        Assert.Single(store.Library.Tracks);
        Assert.Single(Directory.GetFiles(_musicFolder, "*.mp3"));
    }

    [Fact]
    public async Task Different_songs_with_the_same_file_name_are_both_kept()
    {
        var store = new MusicStore(_musicFolder);
        await store.ImportAsync([MakeFile("Theme.mp3", FakeAudio(7))]);
        File.Delete(Path.Combine(_elsewhere, "Theme.mp3"));
        await store.ImportAsync([MakeFile("Theme.mp3", FakeAudio(11))]);

        Assert.Equal(2, store.Library.Tracks.Count);
        Assert.True(File.Exists(Path.Combine(_musicFolder, "Theme.mp3")));
        Assert.True(File.Exists(Path.Combine(_musicFolder, "Theme (2).mp3")));
    }

    [Fact]
    public async Task Files_that_are_not_music_are_reported_and_not_copied()
    {
        string notes = MakeFile("notes.txt", Encoding.UTF8.GetBytes("not a song"));
        var store = new MusicStore(_musicFolder);

        var result = await store.ImportAsync([notes]);

        Assert.Empty(result.Added);
        Assert.Equal(notes, Assert.Single(result.Failed));
        Assert.Empty(store.Library.Tracks);
    }

    [Fact]
    public async Task Songs_and_playlists_are_saved_and_loaded()
    {
        var store = new MusicStore(_musicFolder);
        var added = (await store.ImportAsync([MakeFile("a.mp3", FakeAudio(2)), MakeFile("b.mp3", FakeAudio(3))])).Added;
        added[0].Seconds = 125;
        var battle = store.CreatePlaylist("Battle");
        store.AddToPlaylist(battle, added[1]);
        store.AddToPlaylist(battle, added[0]);
        store.AddToPlaylist(battle, added[1]); // already there: not added twice
        store.Save();

        var reloaded = new MusicStore(_musicFolder);

        Assert.Equal(["a", "b"], reloaded.Library.Tracks.Select(t => t.Title));
        Assert.Equal(125, reloaded.Library.Tracks[0].Seconds);
        Assert.Equal("2:05", reloaded.Library.Tracks[0].Length);
        var playlist = Assert.Single(reloaded.Library.Playlists);
        Assert.Equal("Battle", playlist.Name);
        Assert.Equal(["b", "a"], reloaded.TracksOf(playlist).Select(t => t.Title));
    }

    [Fact]
    public async Task Deleting_a_song_takes_it_out_of_every_playlist_and_into_the_trash()
    {
        var store = new MusicStore(_musicFolder);
        var track = (await store.ImportAsync([MakeFile("Gone.mp3", FakeAudio(4))])).Added[0];
        var one = store.CreatePlaylist("One");
        var two = store.CreatePlaylist("Two");
        store.AddToPlaylist(one, track);
        store.AddToPlaylist(two, track);

        store.Delete(track);

        Assert.Empty(store.Library.Tracks);
        Assert.Empty(one.TrackIds);
        Assert.Empty(two.TrackIds);
        Assert.False(File.Exists(Path.Combine(_musicFolder, "Gone.mp3")));
        Assert.True(File.Exists(Path.Combine(_musicFolder, MusicStore.TrashFolderName, "Gone.mp3")));
    }

    [Fact]
    public void A_damaged_list_is_put_aside_and_the_app_starts_empty()
    {
        Directory.CreateDirectory(_musicFolder);
        string list = Path.Combine(_musicFolder, MusicStore.ListFileName);
        File.WriteAllText(list, "{ this isn't json");

        var store = new MusicStore(_musicFolder);

        Assert.Empty(store.Library.Tracks);
        Assert.True(File.Exists(list + ".damaged"));
    }

    [Fact]
    public void A_list_that_cannot_be_read_is_never_saved_over()
    {
        Directory.CreateDirectory(_musicFolder);
        string list = Path.Combine(_musicFolder, MusicStore.ListFileName);
        File.WriteAllText(list, """{ "Playlists": [ { "Name": "Keep me" } ] }""");

        MusicStore store;
        using (new FileStream(list, FileMode.Open, FileAccess.Read, FileShare.None)) // locked, like OneDrive mid-sync
            store = new MusicStore(_musicFolder);
        store.CreatePlaylist("New");
        store.Save();

        Assert.True(store.CouldNotLoad);
        Assert.Contains("Keep me", File.ReadAllText(list));
    }

    [Fact]
    public void The_music_folder_is_not_a_project()
    {
        var library = new Library(Path.Combine(_root, "Library"));
        library.CreateProject("Reverie");
        Directory.CreateDirectory(library.MusicFolder);

        Assert.Equal(["Reverie"], library.ListProjectFolders().Select(Path.GetFileName));
    }

    [Fact]
    public void A_project_called_Music_gets_a_folder_of_its_own()
    {
        var library = new Library(Path.Combine(_root, "Library"));
        var project = library.CreateProject("Music");

        Assert.NotEqual(library.MusicFolder, project.FolderPath);
        Assert.Contains(project.FolderPath, library.ListProjectFolders());
    }
}

/// <summary>Reading a song's title and artist from the ID3 tags inside an MP3.</summary>
public class AudioTagsTests
{
    /// <summary>An ID3 version 2.3 tag with a title and artist in UTF-16, the way most music software writes them.</summary>
    public static byte[] Id3v23(string title, string artist)
    {
        var frames = new List<byte>();
        foreach (var (id, text) in new[] { ("TIT2", title), ("TPE1", artist) })
        {
            byte[] data = [1, .. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];
            frames.AddRange(Encoding.ASCII.GetBytes(id));
            frames.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, 0, 0]);
            frames.AddRange(data);
        }
        frames.AddRange(new byte[64]); // padding
        return [.. Header(3, frames.Count), .. frames];
    }

    private static byte[] Header(int version, int size) =>
        [(byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0,
         (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)];

    [Fact]
    public void Reads_a_version_2_3_tag()
    {
        using var stream = new MemoryStream(Id3v23("Sakura Drift", "Mika"));
        var tags = AudioTags.ReadId3v2(stream);
        Assert.Equal("Sakura Drift", tags?.Title);
        Assert.Equal("Mika", tags?.Artist);
    }

    [Fact]
    public void Reads_a_version_2_4_tag_in_UTF8()
    {
        // Version 2.4 writes frame sizes the "synchsafe" way and usually uses UTF-8.
        byte[] text = [3, .. Encoding.UTF8.GetBytes("夜の街")];
        byte[] frame = [.. Encoding.ASCII.GetBytes("TIT2"), 0, 0, 0, (byte)text.Length, 0, 0, .. text];
        using var stream = new MemoryStream([.. Header(4, frame.Length), .. frame]);

        var tags = AudioTags.ReadId3v2(stream);
        Assert.Equal("夜の街", tags?.Title);
        Assert.Null(tags?.Artist);
    }

    [Fact]
    public void Reads_the_old_version_1_tag_at_the_end_of_the_file()
    {
        var tag = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(tag, 0);
        Encoding.Latin1.GetBytes("Old Song").CopyTo(tag, 3);
        Encoding.Latin1.GetBytes("Old Band").CopyTo(tag, 33);
        using var stream = new MemoryStream([.. new byte[500], .. tag]);

        Assert.Null(AudioTags.ReadId3v2(stream));
        var tags = AudioTags.ReadId3v1(stream);
        Assert.Equal("Old Song", tags?.Title);
        Assert.Equal("Old Band", tags?.Artist);
    }

    [Fact]
    public void A_file_without_tags_has_no_title()
    {
        using var stream = new MemoryStream(new byte[1000]);
        Assert.Null(AudioTags.ReadId3v2(stream));
        Assert.Null(AudioTags.ReadId3v1(stream));
    }
}
