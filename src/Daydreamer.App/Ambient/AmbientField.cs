using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Daydreamer.App.Ambient;

/// <summary>
/// Ambient mode (dark mode only): soft blue orbs of light drifting up over the page, underneath the words.
///
/// The motion is simulated at a fixed 60 steps per second (like a game loop) but drawn about 30 times
/// a second — plenty for something this slow, and it keeps the effect light on the CPU. Each orb is a
/// pre-drawn image of pure light (see <see cref="OrbSprites"/>), so it adds to the page beneath
/// instead of painting over it.
/// </summary>
public sealed class AmbientField : FrameworkElement
{
    private const double StepsPerSecond = 60;
    private const double StepSeconds = 1 / StepsPerSecond;

    private readonly List<GlowMote> _motes = [];
    private readonly MoteEmitter _emitter = new(perSecond: 0.9, initial: 14, limit: 28);
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private readonly AmbientLook _look = AmbientLook.Blue;
    private readonly OrbSprites _sprites = new(AmbientLook.Blue);

    private double _accumulator;

    public AmbientField()
    {
        IsHitTestVisible = false; // purely decorative: clicks go straight through
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _clock.Restart();
        _accumulator = 0;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _clock.Stop();
        _motes.Clear();
        _emitter.Reset();
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Fixed time step: the motion is the same however unevenly the timer fires.
        double elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        _accumulator += Math.Min(elapsed, 0.25); // after a pause, don't try to catch up for minutes

        bool moved = false;
        while (_accumulator >= StepSeconds)
        {
            _accumulator -= StepSeconds;
            Step();
            moved = true;
        }
        if (moved) InvalidateVisual();
    }

    private void Step()
    {
        var area = new Size(ActualWidth, ActualHeight);
        if (area.Width < 1 || area.Height < 1) return;

        _emitter.Update(_motes, scattered => GlowMote.Anywhere(_random, area, _look, _sprites, scattered), StepsPerSecond);
        foreach (var mote in _motes) mote.Update(area);
        _motes.RemoveAll(m => !m.Active);
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var mote in _motes) mote.Draw(dc);
    }
}
