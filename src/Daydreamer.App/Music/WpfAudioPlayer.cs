using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.Core.Music;

namespace Daydreamer.App.Music;

/// <summary>
/// One "deck" of the music channel: WPF's MediaPlayer, which plays sound with no window (it uses the
/// same engine as Windows Media Player). The channel has two of these so songs can crossfade.
/// </summary>
public sealed class WpfAudioPlayer : IAudioPlayer
{
    private readonly MediaPlayer _player = new();

    public WpfAudioPlayer()
    {
        _player.MediaEnded += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        _player.MediaFailed += (_, _) => Failed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Ended;
    public event EventHandler? Failed;

    public void Play(string path)
    {
        try
        {
            _player.Open(ToUri(path));
            _player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or COMException)
        {
            // Windows' media engine is missing or broken. Say so a moment later, like a file that won't
            // open, rather than in the middle of the channel starting this song.
            Dispatcher.CurrentDispatcher.BeginInvoke(() => Failed?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Pause() => _player.Pause();

    public void Resume() => _player.Play();

    public void Stop()
    {
        _player.Stop();
        _player.Close(); // lets go of the file, so it can be moved or deleted
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public TimeSpan Position
    {
        get => _player.Position;
        set => _player.Position = value;
    }

    public TimeSpan? Duration => _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : null;

    /// <summary>
    /// A file path as a "file:" address. In an address # and % mean something else, so they're spelled
    /// out (UriBuilder does #, and % becomes %25) to keep them as part of a song's name.
    /// </summary>
    public static Uri ToUri(string path) =>
        new UriBuilder(Uri.UriSchemeFile, "") { Path = Path.GetFullPath(path).Replace("%", "%25") }.Uri;
}
