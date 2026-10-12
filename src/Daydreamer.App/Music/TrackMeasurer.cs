using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.Core.Music;

namespace Daydreamer.App.Music;

/// <summary>
/// Finds out how long songs are, so the lists can show "3:45". It opens each file in a player that
/// never plays (opening is enough for Windows to read the length), one song at a time.
/// </summary>
public sealed class TrackMeasurer
{
    private readonly MediaPlayer _player = new() { Volume = 0 };
    private readonly Queue<(Track Track, string Path)> _waiting = new();
    private readonly DispatcherTimer _giveUp = new() { Interval = TimeSpan.FromSeconds(10) };
    private Track? _measuring;

    public TrackMeasurer()
    {
        _player.MediaOpened += (_, _) =>
        {
            if (_measuring != null && _player.NaturalDuration.HasTimeSpan)
            {
                _measuring.Seconds = Math.Round(_player.NaturalDuration.TimeSpan.TotalSeconds, 1);
                Measured?.Invoke(this, EventArgs.Empty);
            }
            MeasureNext();
        };
        _player.MediaFailed += (_, _) => MeasureNext();
        _giveUp.Tick += (_, _) => MeasureNext(); // a file that never answers (stuck in OneDrive, say)
    }

    /// <summary>Raised each time a song's length is found (so it can be saved).</summary>
    public event EventHandler? Measured;

    public void Measure(Track track, string path)
    {
        _waiting.Enqueue((track, path));
        if (_measuring == null) MeasureNext();
    }

    private void MeasureNext()
    {
        _giveUp.Stop();
        _player.Close();
        _measuring = null;

        while (_waiting.TryDequeue(out var next))
        {
            if (next.Track.Seconds != null) continue; // measured some other way meanwhile
            _measuring = next.Track;
            try
            {
                _player.Open(WpfAudioPlayer.ToUri(next.Path));
                _giveUp.Start();
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                _measuring = null; // no media engine: lengths just stay blank
            }
        }
    }
}
