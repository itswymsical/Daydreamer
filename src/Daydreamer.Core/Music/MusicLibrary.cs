using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Music;

/// <summary>A song in the music library. The file itself is a copy kept in the library's Music folder.</summary>
public sealed class Track : ObservableObject
{
    private string _title = "";
    private string? _artist;
    private double? _seconds;
    private bool _isPlaying;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The file's name inside the Music folder, e.g. "Reverie Theme.mp3".</summary>
    public string FileName { get; set; } = "";

    public string Title { get => _title; set => SetField(ref _title, value); }

    public string? Artist { get => _artist; set => SetField(ref _artist, string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }

    /// <summary>How long the song is, once known (it's measured after the song is added).</summary>
    public double? Seconds
    {
        get => _seconds;
        set
        {
            if (SetField(ref _seconds, value)) OnPropertyChanged(nameof(Length));
        }
    }

    public DateTime Added { get; set; }

    /// <summary>"3:45" (or "1:02:03" for something an hour long). Empty until the length is known.</summary>
    [JsonIgnore]
    public string Length => Seconds is { } s ? FormatTime(TimeSpan.FromSeconds(s)) : "";

    /// <summary>True while this is the song playing (for the lists to show it). Not saved.</summary>
    [JsonIgnore]
    public bool IsPlaying { get => _isPlaying; set => SetField(ref _isPlaying, value); }

    public static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    public override string ToString() => Artist is { Length: > 0 } ? $"{Title} — {Artist}" : Title;
}

/// <summary>A named list of songs. The same song can be in many playlists; each just points at it.</summary>
public sealed class Playlist : ObservableObject
{
    private string _name = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Untitled playlist" : value.Trim());
    }

    /// <summary>The songs, in order, by <see cref="Track.Id"/>.</summary>
    public ObservableCollection<string> TrackIds { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>Everything in Music\music.json: every song that's been added, and the playlists.</summary>
public sealed class MusicLibrary
{
    public int Version { get; set; } = 1;
    public ObservableCollection<Track> Tracks { get; set; } = [];
    public ObservableCollection<Playlist> Playlists { get; set; } = [];
}
