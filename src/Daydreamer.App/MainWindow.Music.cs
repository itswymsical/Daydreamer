using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Music;
using Daydreamer.Core.Music;
using Microsoft.Win32;
using Track = Daydreamer.Core.Music.Track; // WPF has a Track too (the inside of a slider)

namespace Daydreamer.App;

// The music player: the Music tab in the side panel (songs and playlists) and the small player in the
// title bar. The thinking happens in Daydreamer.Core.Music: MusicStore keeps the songs and playlists,
// MusicChannel decides what plays when and does the crossfades. (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private MusicStore _music = null!;
    private MusicChannel _channel = null!;
    private TrackMeasurer _measurer = null!;

    // While music plays, this moves fades along and keeps the position bar up to date.
    private readonly DispatcherTimer _musicTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _musicClock = new();
    private TimeSpan _lastMusicTick;

    private readonly DispatcherTimer _musicSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /// <summary>"All songs": the whole library, first in the playlist picker. It isn't saved as a playlist.</summary>
    private readonly Playlist _allSongs = new() { Name = "All songs" };

    private Playlist _shownPlaylist = null!;  // the list the Music tab is showing
    private Playlist? _playingPlaylist;       // the list the music is coming from
    private Track? _markedPlaying;            // the song shown as playing in the lists
    private bool _updatingSeekBar;            // true while the code (not the mouse) moves the position bar
    private bool _seeking;                    // true while the position bar is being dragged
    private bool _addingSongs;
    private double _volumeBeforeMute = 0.7;

    // Dragging a song to a new place in the list.
    private Point _dragStart;
    private Track? _dragTrack;

    /// <summary>The choices in the Crossfade box.</summary>
    private sealed record CrossfadeChoice(double Seconds)
    {
        public override string ToString() => Seconds == 0 ? "Off" : $"{Seconds:0.#} s";
    }

    private static readonly CrossfadeChoice[] CrossfadeChoices = [new(0), new(1), new(2), new(3), new(5), new(8), new(12)];

    // ═══════════════════════════════════════════════════════════════════════
    //  Start-up
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Called once the library is open: the music lives in its Music folder.</summary>
    private void SetUpMusic()
    {
        _music = new MusicStore(_library.MusicFolder);
        _channel = new MusicChannel(new WpfAudioPlayer(), new WpfAudioPlayer(), _music.PathOf)
        {
            Volume = _settings.MusicVolume,
            CrossfadeLength = TimeSpan.FromSeconds(_settings.CrossfadeSeconds),
        };
        _channel.Changed += (_, _) => OnMusicChanged();

        _measurer = new TrackMeasurer();
        _measurer.Measured += (_, _) =>
        {
            UpdateSongCount();
            SaveMusicSoon();
        };
        foreach (var track in _music.Library.Tracks.Where(t => t.Seconds == null))
            _measurer.Measure(track, _music.PathOf(track));

        _musicTimer.Tick += (_, _) => MusicTick();
        _musicSaveTimer.Tick += (_, _) => SaveMusic();

        // Keep the lists (and the music that's playing) in step with changes to the songs and playlists.
        _music.Library.Tracks.CollectionChanged += (_, _) => OnSongsChanged(_allSongs);
        _music.Library.Playlists.CollectionChanged += Playlists_CollectionChanged;
        foreach (var playlist in _music.Library.Playlists) WatchPlaylist(playlist);

        PlaylistPicker.ItemsSource = new CompositeCollection
        {
            _allSongs,
            new CollectionContainer { Collection = _music.Library.Playlists },
        };
        ShowPlaylist(_music.Library.Playlists.FirstOrDefault(p => p.Id == _settings.MusicPlaylist) ?? _allSongs);

        CrossfadePicker.ItemsSource = CrossfadeChoices;
        CrossfadePicker.SelectedItem = CrossfadeChoices.MinBy(c => Math.Abs(c.Seconds - _settings.CrossfadeSeconds));
        VolumeSlider.Value = _channel.Volume;

        // The media keys on the keyboard (and headphones) work while Daydreamer is in front, but only once
        // something is playing here; otherwise they're left for other music apps.
        CommandBindings.Add(new CommandBinding(MediaCommands.TogglePlayPause, (_, _) => _channel.TogglePause(), CanUseMediaKeys));
        CommandBindings.Add(new CommandBinding(MediaCommands.Play, (_, _) => _channel.Resume(), CanUseMediaKeys));
        CommandBindings.Add(new CommandBinding(MediaCommands.Pause, (_, _) => _channel.Pause(), CanUseMediaKeys));
        CommandBindings.Add(new CommandBinding(MediaCommands.NextTrack, (_, _) => _channel.Next(), CanUseMediaKeys));
        CommandBindings.Add(new CommandBinding(MediaCommands.PreviousTrack, (_, _) => _channel.Previous(), CanUseMediaKeys));
        CommandBindings.Add(new CommandBinding(MediaCommands.Stop, (_, _) => _channel.Stop(), CanUseMediaKeys));

        OnMusicChanged();
    }

    private void CanUseMediaKeys(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _channel.Current != null;

    // ═══════════════════════════════════════════════════════════════════════
    //  Playing
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Plays the list on screen, starting from this song.</summary>
    private void PlayFromList(Track track)
    {
        var tracks = TracksOf(_shownPlaylist);
        int index = tracks.IndexOf(track);
        if (index < 0) return;
        _playingPlaylist = _shownPlaylist;
        _channel.PlayPlaylist(tracks, index);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_channel.Current != null)
        {
            _channel.TogglePause();
            return;
        }
        // Nothing loaded yet: play the list on screen, from the selected song (or the top).
        var start = SongList.SelectedItem as Track ?? TracksOf(_shownPlaylist).FirstOrDefault();
        if (start != null) PlayFromList(start);
    }

    private void PreviousSong_Click(object sender, RoutedEventArgs e) => _channel.Previous();
    private void NextSong_Click(object sender, RoutedEventArgs e) => _channel.Next();

    private void ShowMusic_Click(object sender, RoutedEventArgs e)
    {
        ShowWriting(focusEditor: false);
        ShowSidePanelTab(MusicTab);
    }

    /// <summary>The song changed, or was paused or resumed: bring everything on screen up to date.</summary>
    private void OnMusicChanged()
    {
        var current = _channel.Current;
        if (_markedPlaying != current)
        {
            if (_markedPlaying != null) _markedPlaying.IsPlaying = false;
            if (current != null) current.IsPlaying = true;
            _markedPlaying = current;
        }

        NowPlayingTitle.Text = current?.Title ?? "Nothing playing";
        NowPlayingArtist.Text = current == null ? "Double-click a song to start." : current.Artist ?? "";
        MiniTitle.Text = current?.ToString() ?? "";
        MiniPlayer.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        MusicButton.Visibility = current != null ? Visibility.Collapsed : Visibility.Visible;

        string icon = _channel.IsPlaying ? "" : ""; // pause / play
        PlayPauseIcon.Text = MiniPlayPauseIcon.Text = icon;
        PlayPauseButton.ToolTip = MiniPlayPause.ToolTip = _channel.IsPlaying ? "Pause" : "Play";

        MusicProblem.Text = _channel.Problem ?? "";
        MusicProblem.Visibility = _channel.Problem != null ? Visibility.Visible : Visibility.Collapsed;

        // The timer only runs while there's something to move along.
        if (_channel.IsPlaying && !_musicTimer.IsEnabled)
        {
            _musicClock.Restart();
            _lastMusicTick = TimeSpan.Zero;
            _musicTimer.Start();
        }
        else if (!_channel.IsPlaying)
        {
            _musicTimer.Stop();
        }
        UpdateSeekBar();
    }

    private void MusicTick()
    {
        var now = _musicClock.Elapsed;
        _channel.Tick(now - _lastMusicTick);
        _lastMusicTick = now;
        UpdateSeekBar();
    }

    private void UpdateSeekBar()
    {
        var duration = _channel.Duration;
        var position = _channel.Position;
        SeekBar.IsEnabled = duration != null;
        DurationText.Text = duration is { } length ? Track.FormatTime(length) : "";
        if (_seeking) return; // the bar is being dragged: leave it where the mouse put it

        _updatingSeekBar = true;
        SeekBar.Maximum = Math.Max(1, duration?.TotalSeconds ?? 1);
        SeekBar.Value = Math.Min(position.TotalSeconds, SeekBar.Maximum);
        _updatingSeekBar = false;
        PositionText.Text = _channel.Current != null ? Track.FormatTime(position) : "";
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeekBar || _channel == null) return;
        if (_seeking)
            PositionText.Text = Track.FormatTime(TimeSpan.FromSeconds(e.NewValue)); // show where it'll jump to
        else
            _channel.Seek(TimeSpan.FromSeconds(e.NewValue)); // a click on the bar
    }

    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e) => _seeking = true;

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _seeking = false;
        _channel.Seek(TimeSpan.FromSeconds(SeekBar.Value));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_channel == null) return;
        _channel.Volume = e.NewValue;
        _settings.MusicVolume = e.NewValue; // saved with the other settings when the app closes
        VolumeIcon.Text = e.NewValue <= 0 ? "" : ""; // muted / speaker
        MuteButton.ToolTip = e.NewValue <= 0 ? "Unmute" : "Mute";
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (VolumeSlider.Value > 0)
        {
            _volumeBeforeMute = VolumeSlider.Value;
            VolumeSlider.Value = 0;
        }
        else
        {
            VolumeSlider.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 0.7;
        }
    }

    private void CrossfadePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_channel == null || CrossfadePicker.SelectedItem is not CrossfadeChoice choice) return;
        _channel.CrossfadeLength = TimeSpan.FromSeconds(choice.Seconds);
        _settings.CrossfadeSeconds = choice.Seconds;
        SaveSettings();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Playlists
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>A list's songs, in order. All songs is everything in the library.</summary>
    private List<Track> TracksOf(Playlist playlist) =>
        playlist == _allSongs ? [.. _music.Library.Tracks] : _music.TracksOf(playlist);

    private bool IsRealPlaylist(Playlist playlist) => playlist != _allSongs;

    private void ShowPlaylist(Playlist playlist)
    {
        _shownPlaylist = playlist;
        if (PlaylistPicker.SelectedItem != playlist) PlaylistPicker.SelectedItem = playlist;
        RenamePlaylistButton.IsEnabled = DeletePlaylistButton.IsEnabled = IsRealPlaylist(playlist);

        _settings.MusicPlaylist = IsRealPlaylist(playlist) ? playlist.Id : null;
        SaveSettings();
        RefreshSongList();
    }

    private void PlaylistPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_music != null && PlaylistPicker.SelectedItem is Playlist playlist && playlist != _shownPlaylist)
            ShowPlaylist(playlist);
    }

    private void RefreshSongList()
    {
        var selected = SongList.SelectedItem;
        var tracks = TracksOf(_shownPlaylist);
        SongList.ItemsSource = tracks;
        if (selected is Track track && tracks.Contains(track)) SongList.SelectedItem = track;
        UpdateSongCount();

        SongsEmpty.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SongsEmptyText.Text = IsRealPlaylist(_shownPlaylist)
            ? "This playlist is empty.\nAdd songs here, or right-click a song in All songs and pick Add to playlist."
            : "No music yet.\nClick Add songs, or drag music files (or a whole folder) here from File Explorer. Daydreamer keeps its own copy of each one.";
    }

    /// <summary>"12 SONGS · 41 MIN" above the list.</summary>
    private void UpdateSongCount()
    {
        if (_addingSongs || SongList.ItemsSource is not List<Track> tracks) return;
        double seconds = tracks.Sum(t => t.Seconds ?? 0);
        string count = tracks.Count == 1 ? "1 SONG" : $"{tracks.Count} SONGS";
        SongCount.Text = seconds >= 60 ? $"{count}  ·  {Math.Round(seconds / 60)} MIN" : count;
    }

    private void WatchPlaylist(Playlist playlist) => playlist.TrackIds.CollectionChanged += PlaylistSongs_CollectionChanged;

    private void PlaylistSongs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var playlist = _music.Library.Playlists.FirstOrDefault(p => p.TrackIds == sender);
        if (playlist != null) OnSongsChanged(playlist);
    }

    private void Playlists_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (Playlist added in e.NewItems ?? Array.Empty<Playlist>()) WatchPlaylist(added);
        foreach (Playlist removed in e.OldItems ?? Array.Empty<Playlist>())
        {
            removed.TrackIds.CollectionChanged -= PlaylistSongs_CollectionChanged;
            if (removed == _playingPlaylist) _playingPlaylist = null; // the music carries on, but it's on its own now
            if (removed == _shownPlaylist) ShowPlaylist(_allSongs);
        }
        SaveMusicSoon();
    }

    /// <summary>Songs were added, removed or moved in a list.</summary>
    private void OnSongsChanged(Playlist playlist)
    {
        if (playlist == _shownPlaylist) RefreshSongList();
        if (playlist == _playingPlaylist) _channel.UpdatePlaylist(TracksOf(playlist));
        SaveMusicSoon();
    }

    private void NewPlaylist_Click(object sender, RoutedEventArgs e) => NewPlaylist();

    /// <summary>Asks for a name and makes the playlist. Null if cancelled.</summary>
    private Playlist? NewPlaylist(Track? firstSong = null)
    {
        string? name = PromptDialog.Ask(this, "New playlist", "What should the playlist be called? (Battle themes, Scene 7, Rainy days…)", "", "Create");
        if (name == null) return null;
        var playlist = _music.CreatePlaylist(name);
        if (firstSong != null) _music.AddToPlaylist(playlist, firstSong);
        ShowPlaylist(playlist);
        return playlist;
    }

    private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRealPlaylist(_shownPlaylist)) return;
        string? name = PromptDialog.Ask(this, "Rename playlist", "New name:", _shownPlaylist.Name, "Rename");
        if (name == null) return;
        _shownPlaylist.Name = name;
        SaveMusicSoon();
    }

    private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRealPlaylist(_shownPlaylist)) return;
        bool confirmed = PromptDialog.Confirm(this, "Delete playlist",
            $"Delete the playlist \"{_shownPlaylist.Name}\"?\n\nIts songs stay in All songs; only the list goes.", "Delete");
        if (confirmed) _music.DeletePlaylist(_shownPlaylist);
    }

    private void OpenMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_music.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_music.Folder}\"") { UseShellExecute = true });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Adding and removing songs
    // ═══════════════════════════════════════════════════════════════════════

    private async void AddSongs_Click(object sender, RoutedEventArgs e)
    {
        string extensions = string.Join(";", MusicStore.SupportedExtensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog
        {
            Title = "Add songs",
            Multiselect = true,
            Filter = $"Music ({extensions.Replace(";", ", ")})|{extensions}|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true) await AddSongs(dialog.FileNames);
    }

    /// <summary>
    /// Copies songs into the Music folder (folders are searched for music) and adds them to the playlist
    /// on screen. The copying happens in the background, so writing carries on meanwhile.
    /// </summary>
    private async Task AddSongs(IEnumerable<string> paths)
    {
        if (_addingSongs) return;
        var files = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(MusicStore.CanPlay).Order(StringComparer.CurrentCultureIgnoreCase));
            else
                files.Add(path);
        }
        if (files.Count == 0) return;

        var playlist = _shownPlaylist;
        _addingSongs = true;
        AddSongsButton.IsEnabled = false;
        SongCount.Text = files.Count == 1 ? "ADDING 1 SONG…" : $"ADDING {files.Count} SONGS…";
        ImportResult result;
        try
        {
            result = await _music.ImportAsync(files);
        }
        finally
        {
            _addingSongs = false;
            AddSongsButton.IsEnabled = true;
            UpdateSongCount();
        }

        if (IsRealPlaylist(playlist))
            foreach (var track in result.Added) _music.AddToPlaylist(playlist, track);
        foreach (var track in result.Added.Where(t => t.Seconds == null))
            _measurer.Measure(track, _music.PathOf(track));
        RefreshSongList();
        SaveMusic();

        if (result.Added.Count > 0)
        {
            SongList.SelectedItem = result.Added[0];
            SongList.ScrollIntoView(result.Added[0]);
        }
        if (result.Failed.Count > 0)
        {
            string names = string.Join("\n", result.Failed.Take(8).Select(Path.GetFileName));
            if (result.Failed.Count > 8) names += $"\n…and {result.Failed.Count - 8} more";
            PromptDialog.Inform(this, "Add songs",
                $"Couldn't add {(result.Failed.Count == 1 ? "this file" : "these files")}:\n\n{names}\n\n" +
                "Daydreamer plays MP3, M4A, AAC, WAV, WMA and FLAC files.");
        }
    }

    /// <summary>Takes a song out of this playlist (it stays in All songs and other playlists).</summary>
    private void RemoveFromPlaylist(Track track)
    {
        if (IsRealPlaylist(_shownPlaylist)) _music.RemoveFromPlaylist(_shownPlaylist, track);
    }

    /// <summary>Takes a song out of the library altogether. Its file goes to Music\.trash, so it can be recovered.</summary>
    private async void DeleteSong(Track track)
    {
        bool confirmed = PromptDialog.Confirm(this, "Delete song",
            $"Delete \"{track.Title}\" from your music?\n\nIt comes out of every playlist. The file goes into the Music folder's .trash folder, so you can get it back.",
            "Delete");
        if (!confirmed) return;

        if (_channel.Current == track)
        {
            // A file can't be moved while it's playing, so move on (or stop) and give the player a moment to let go.
            if (_channel.Playlist.Any(t => t != track)) _channel.Next();
            else _channel.Stop();
            await Task.Delay(_channel.SkipFadeLength + TimeSpan.FromMilliseconds(300));
        }
        _music.Delete(track);
        SaveMusicSoon();
    }

    private void RenameSong(Track track)
    {
        string? title = PromptDialog.Ask(this, "Rename song", "Song title:", track.Title, "Rename");
        if (title == null) return;
        track.Title = title;
        if (track == _channel.Current) OnMusicChanged();
        SaveMusicSoon();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  The song list: double-click, keys, right-click menu, drag and drop
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>The song under the mouse (or keyboard focus), if any.</summary>
    private static Track? TrackAt(object source) => (source as FrameworkElement)?.DataContext as Track
        ?? (source as FrameworkContentElement)?.DataContext as Track;

    private void SongList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackAt(e.OriginalSource) is { } track) PlayFromList(track);
    }

    private void SongList_KeyDown(object sender, KeyEventArgs e)
    {
        if (SongList.SelectedItem is not Track track) return;
        if (e.Key == Key.Enter) PlayFromList(track);
        else if (e.Key == Key.Delete && IsRealPlaylist(_shownPlaylist)) RemoveFromPlaylist(track);
        else if (e.Key == Key.Delete) DeleteSong(track);
        else return;
        e.Handled = true;
    }

    private void SongList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var track = TrackAt(e.OriginalSource);
        if (track == null)
        {
            e.Handled = true; // right-clicked empty space: no menu
            return;
        }
        SongList.SelectedItem = track;
        var menu = SongList.ContextMenu;
        menu.Items.Clear();

        menu.Items.Add(MenuItem("Play", () => PlayFromList(track), bold: true));
        if (_channel.Current != null && _channel.Current != track)
        {
            // Cutting in line: the song plays now (or next), and then the playlist carries on where it was.
            menu.Items.Add(MenuItem("Cut in line now", () => _channel.PlayNow(track),
                "Fades into this song straight away. The playlist carries on after it."));
            menu.Items.Add(MenuItem("Play next", () => _channel.PlayNext(track), "Plays this song when the current one ends."));
        }
        menu.Items.Add(new Separator());

        var addTo = new MenuItem { Header = "Add to playlist" };
        foreach (var playlist in _music.Library.Playlists)
        {
            var item = MenuItem(playlist.Name, () => _music.AddToPlaylist(playlist, track));
            item.IsEnabled = !playlist.TrackIds.Contains(track.Id); // already in it
            addTo.Items.Add(item);
        }
        if (addTo.Items.Count > 0) addTo.Items.Add(new Separator());
        addTo.Items.Add(MenuItem("New playlist…", () => NewPlaylist(track)));
        menu.Items.Add(addTo);

        if (IsRealPlaylist(_shownPlaylist))
            menu.Items.Add(MenuItem($"Remove from \"{_shownPlaylist.Name}\"", () => RemoveFromPlaylist(track)));
        menu.Items.Add(MenuItem("Rename…", () => RenameSong(track)));
        menu.Items.Add(new Separator());
        var delete = MenuItem("Delete from music…", () => DeleteSong(track));
        delete.Foreground = (Brush)FindResource("DangerTextBrush");
        menu.Items.Add(delete);
    }

    private static MenuItem MenuItem(string header, Action click, string? toolTip = null, bool bold = false)
    {
        var item = new MenuItem { Header = header, ToolTip = toolTip };
        if (bold) item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) => click();
        return item;
    }

    private void SongList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(SongList);
        _dragTrack = TrackAt(e.OriginalSource);
    }

    private void SongList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTrack == null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(SongList) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var track = _dragTrack;
        _dragTrack = null;
        DragDrop.DoDragDrop(SongList, new DataObject(typeof(Track), track), DragDropEffects.Move);
    }

    private void SongList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
            : e.Data.GetDataPresent(typeof(Track)) ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void SongList_Drop(object sender, DragEventArgs e)
    {
        // Files from File Explorer: add them.
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await AddSongs(files);
            return;
        }

        // A song dragged within the list: it takes the place of the song it was dropped on.
        if (e.Data.GetData(typeof(Track)) is not Track dragged) return;
        var target = TrackAt(e.OriginalSource);
        var tracks = TracksOf(_shownPlaylist);
        int to = target != null ? tracks.IndexOf(target) : tracks.Count - 1; // dropped below the songs: to the end
        int from = tracks.IndexOf(dragged);
        if (from < 0 || to < 0 || from == to) return;

        if (IsRealPlaylist(_shownPlaylist))
        {
            var ids = _shownPlaylist.TrackIds;
            ids.Move(ids.IndexOf(dragged.Id), ids.IndexOf(tracks[to].Id));
        }
        else
        {
            var all = _music.Library.Tracks;
            all.Move(all.IndexOf(dragged), all.IndexOf(tracks[to]));
        }
        SongList.SelectedItem = dragged;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Saving
    // ═══════════════════════════════════════════════════════════════════════

    private void SaveMusicSoon()
    {
        _musicSaveTimer.Stop();
        _musicSaveTimer.Start();
    }

    /// <summary>Writes Music\music.json. Returns false if it couldn't (it tries again shortly).</summary>
    private bool SaveMusic()
    {
        _musicSaveTimer.Stop();
        if (_music == null) return true;
        try
        {
            _music.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _musicSaveTimer.Start(); // OneDrive may have it locked for a moment
            return false;
        }
    }
}
