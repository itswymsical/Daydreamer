namespace Daydreamer.Core.Music;

/// <summary>
/// Plays one audio file at a time. The app wraps WPF's MediaPlayer in this; the tests use a pretend
/// one, so the channel's logic can be checked without any sound.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>Loads a file and starts playing it (whatever was playing stops).</summary>
    void Play(string path);
    void Pause();
    void Resume();

    /// <summary>Stops, and lets go of the file.</summary>
    void Stop();

    /// <summary>0 (silent) to 1 (full).</summary>
    double Volume { get; set; }

    TimeSpan Position { get; set; }

    /// <summary>The song's length, once the file has been read (null until then).</summary>
    TimeSpan? Duration { get; }

    /// <summary>Raised when a song plays through to its end.</summary>
    event EventHandler? Ended;

    /// <summary>Raised when a file can't be played.</summary>
    event EventHandler? Failed;
}

/// <summary>
/// Daydreamer's music channel. It plays a playlist like a radio station, with smooth crossfades.
/// Like a DJ it has two "decks" (players): the next song starts quietly on the free deck and fades
/// up while the current one fades out. A song can also cut in line — "Play now" — fading in straight
/// away, after which the playlist carries on. (Music cues for scenes will use the same thing.)
/// The playlist repeats when it reaches the end.
/// </summary>
public sealed class MusicChannel
{
    private readonly IAudioPlayer[] _decks;
    private readonly Func<Track, string> _pathOf;

    private int _live;             // which deck plays the current song
    private Fade? _fade;           // the fade in progress, if any
    private readonly List<Track> _playlist = [];
    private int _index = -1;       // where we are in the playlist
    private bool _fromPlaylist;    // false while a song that cut in line is playing
    private readonly List<Track> _upNext = []; // songs waiting to cut in after this one
    private double _volume = 0.7;
    private int _failures;

    public MusicChannel(IAudioPlayer deckA, IAudioPlayer deckB, Func<Track, string> pathOf)
    {
        _decks = [deckA, deckB];
        _pathOf = pathOf;
        foreach (var deck in _decks)
        {
            deck.Ended += OnEnded;
            deck.Failed += OnFailed;
        }
    }

    /// <summary>The song playing (or paused). Null when nothing is.</summary>
    public Track? Current { get; private set; }

    public bool IsPlaying { get; private set; }

    /// <summary>Set when a song couldn't be played (cleared when the next one starts).</summary>
    public string? Problem { get; private set; }

    /// <summary>The playlist being played.</summary>
    public IReadOnlyList<Track> Playlist => _playlist;

    /// <summary>How long songs take to fade into each other (zero turns crossfading off).</summary>
    public TimeSpan CrossfadeLength { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>A quick fade when you skip, so a song never cuts off with a click.</summary>
    public TimeSpan SkipFadeLength { get; set; } = TimeSpan.FromSeconds(0.4);

    /// <summary>Overall loudness, 0 to 1.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_fade == null && Current != null) _decks[_live].Volume = _volume;
        }
    }

    public TimeSpan Position => Current == null ? TimeSpan.Zero : _decks[_live].Position;
    public TimeSpan? Duration => Current == null ? null : _decks[_live].Duration;

    /// <summary>Raised when the song changes, or it's paused or resumed.</summary>
    public event EventHandler? Changed;

    // ── What to play ────────────────────────────────────────────────────────

    /// <summary>Starts playing a playlist from one of its songs.</summary>
    public void PlayPlaylist(IReadOnlyList<Track> tracks, int startIndex)
    {
        _playlist.Clear();
        _playlist.AddRange(tracks);
        _upNext.Clear();
        if (_playlist.Count == 0)
        {
            Stop();
            return;
        }
        _index = Math.Clamp(startIndex, 0, _playlist.Count - 1);
        Start(_playlist[_index], SkipFadeLength, fromPlaylist: true);
    }

    /// <summary>
    /// Follows changes to the playlist being played (songs added, removed or moved) without
    /// interrupting the song that's on.
    /// </summary>
    public void UpdatePlaylist(IReadOnlyList<Track> tracks)
    {
        var current = _index >= 0 && _index < _playlist.Count ? _playlist[_index] : null;
        int oldIndex = _index;
        _playlist.Clear();
        _playlist.AddRange(tracks);
        int found = current == null ? -1 : _playlist.IndexOf(current);
        // If the song we were on is gone, carry on from the song that took its place.
        _index = found >= 0 ? found : Math.Min(oldIndex, _playlist.Count) - 1;
    }

    /// <summary>Cuts in line: fades into this song now. The playlist carries on after it.</summary>
    public void PlayNow(Track track) => Start(track, CrossfadeLength, fromPlaylist: false);

    /// <summary>Cuts in line after the current song.</summary>
    public void PlayNext(Track track) => _upNext.Insert(0, track);

    // ── The buttons ─────────────────────────────────────────────────────────

    public void TogglePause()
    {
        if (Current == null) return;
        if (IsPlaying) Pause();
        else Resume();
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        foreach (var deck in _decks) deck.Pause();
        IsPlaying = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (Current == null || IsPlaying) return;
        _decks[_live].Resume();
        if (_fade is { } fade) _decks[fade.From].Resume();
        IsPlaying = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        foreach (var deck in _decks) deck.Stop();
        _fade = null;
        Current = null;
        IsPlaying = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (Current == null) return;
        if (TakeNext() is { } next) Start(next.Track, SkipFadeLength, next.FromPlaylist);
        else Stop();
    }

    /// <summary>Back to the start of this song — or, if it's only just begun, to the previous one.</summary>
    public void Previous()
    {
        if (Current == null) return;
        if (Position > TimeSpan.FromSeconds(3) || _playlist.Count == 0)
        {
            Seek(TimeSpan.Zero);
            return;
        }
        // A song that cut in line goes back to the playlist song it interrupted.
        if (_fromPlaylist) _index = (_index - 1 + _playlist.Count) % _playlist.Count;
        Start(_playlist[Math.Max(0, _index)], SkipFadeLength, fromPlaylist: true);
    }

    public void Seek(TimeSpan position)
    {
        if (Current != null) _decks[_live].Position = position;
    }

    /// <summary>
    /// Call this regularly while music plays (the app does, about 30 times a second). It moves any
    /// fade along, and starts fading into the next song as the current one nears its end.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (!IsPlaying || Current == null) return;
        var deck = _decks[_live];
        if (deck.Duration != null) _failures = 0; // it opened fine

        if (_fade is { } fade)
        {
            fade.Elapsed += elapsed;
            double progress = fade.Length <= TimeSpan.Zero ? 1 : Math.Min(1, fade.Elapsed / fade.Length);
            // "Equal power" curves keep the overall loudness steady through the middle of the fade.
            _decks[fade.To].Volume = _volume * Math.Sin(progress * Math.PI / 2);
            _decks[fade.From].Volume = fade.FromVolume * Math.Cos(progress * Math.PI / 2);
            if (progress >= 1)
            {
                _decks[fade.From].Stop();
                _fade = null;
            }
            return;
        }

        // Nearly at the end: fade into whatever's next. (Very short songs just play out.)
        if (CrossfadeLength > TimeSpan.Zero && deck.Duration is { } length && length > CrossfadeLength * 2
            && length - deck.Position <= CrossfadeLength && TakeNext() is { } next)
        {
            Start(next.Track, CrossfadeLength, next.FromPlaylist);
        }
    }

    // ── Inside ──────────────────────────────────────────────────────────────

    /// <summary>The next song: anything waiting to cut in, otherwise the playlist (round again at the end).</summary>
    private (Track Track, bool FromPlaylist)? TakeNext()
    {
        if (_upNext.Count > 0)
        {
            var track = _upNext[0];
            _upNext.RemoveAt(0);
            return (track, false);
        }
        if (_playlist.Count == 0) return null;
        _index = (_index + 1) % _playlist.Count;
        return (_playlist[_index], true);
    }

    /// <summary>Starts a song on the free deck, fading it in over the current one (or straight away).</summary>
    private void Start(Track track, TimeSpan fadeLength, bool fromPlaylist)
    {
        // Only one song fades out at a time: finish off any fade that's still going.
        if (_fade is { } unfinished)
        {
            _decks[unfinished.From].Stop();
            _fade = null;
        }

        int from = _live, to = 1 - _live;
        bool crossfade = Current != null && IsPlaying && fadeLength > TimeSpan.Zero;
        var incoming = _decks[to];
        incoming.Volume = crossfade ? 0 : _volume;
        incoming.Play(_pathOf(track));
        if (crossfade) _fade = new Fade(from, to, fadeLength, _decks[from].Volume);
        else _decks[from].Stop();

        _live = to;
        _fromPlaylist = fromPlaylist;
        Current = track;
        IsPlaying = true;
        Problem = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(object? sender, EventArgs e)
    {
        if (sender != _decks[_live]) return; // the old song finishing during a fade doesn't matter
        if (TakeNext() is { } next) Start(next.Track, TimeSpan.Zero, next.FromPlaylist);
        else Stop();
    }

    private void OnFailed(object? sender, EventArgs e)
    {
        if (sender != _decks[_live]) return;
        string title = Current?.Title ?? "that song";
        // Skip it — but if nothing at all will play, stop rather than trying forever.
        if (++_failures > _playlist.Count + _upNext.Count || TakeNext() is not { } next)
        {
            Stop();
        }
        else
        {
            Start(next.Track, TimeSpan.Zero, next.FromPlaylist);
        }
        Problem = $"Couldn't play \"{title}\".";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A fade from one deck to the other.</summary>
    private sealed class Fade(int from, int to, TimeSpan length, double fromVolume)
    {
        public int From { get; } = from;
        public int To { get; } = to;
        public TimeSpan Length { get; } = length;
        public double FromVolume { get; } = fromVolume;
        public TimeSpan Elapsed { get; set; }
    }
}
