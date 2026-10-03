using System.Windows;
using System.Windows.Media;

namespace Daydreamer.App.Ambient;

/// <summary>
/// A soft orb of light that rises, weaves sideways and pulses, fading in and out over its lifetime.
/// Ported from TrelamiumIII's <c>GlowMote</c> (same motion and fade model), with the numbers turned
/// down for a slower, calmer drift behind a page of writing.
/// </summary>
internal sealed class GlowMote
{
    // Per-step values, at 60 steps per second.
    private const double Rise = 0.004;            // upward acceleration
    private const double Drag = 0.985;            // terminal speed = Rise / (1 - Drag) ≈ 16 px/s
    private const double Weave = 0.012;           // sideways push
    private const double WeaveFrequency = 0.02;
    private const double PulseFrequency = 0.04;
    private const double FadeIn = 0.2;            // fraction of the lifetime spent fading in
    private const double FadeOut = 0.35;          // ...and fading out

    private readonly double _radius;
    private readonly int _lifetime;
    private readonly double _phase;
    private readonly double _strength;
    private readonly ImageSource _sprite;

    private Point _position;
    private Vector _velocity;
    private int _age;

    public GlowMote(Point position, Vector velocity, double radius, ImageSource sprite, int lifetime, double phase, double strength)
    {
        _position = position;
        _velocity = velocity;
        _radius = radius;
        _sprite = sprite;
        _lifetime = Math.Max(lifetime, 1);
        _phase = phase;
        _strength = strength;
    }

    public bool Active { get; private set; } = true;

    /// <summary>A mote somewhere in <paramref name="area"/>, tinted somewhere between the look's Low and High colors.</summary>
    public static GlowMote Anywhere(Random random, Size area, AmbientLook look, OrbSprites sprites, bool scattered)
    {
        // The first batch is spread everywhere; later ones start lower down so they have room to rise.
        double top = scattered ? 0 : area.Height * 0.3;
        double radius = Range(random, 8, 26);

        return new GlowMote(
            new Point(random.NextDouble() * area.Width, Range(random, top, area.Height)),
            new Vector(Range(random, -0.15, 0.15), -Range(random, 0.05, 0.25)),
            radius,
            sprites.For(random.NextDouble()),
            random.Next(480, 960),
            random.NextDouble() * Math.PI * 2,
            // Bigger orbs are dimmer, which reads as "further away".
            look.Strength * (1.0 - (radius - 8) / 18 * 0.5));
    }

    /// <summary>One simulation step: rise, weave, slow down, age.</summary>
    public void Update(Size area)
    {
        if (++_age >= _lifetime)
        {
            Active = false;
            return;
        }

        _velocity.Y -= Rise;
        _velocity.X += Math.Sin(_age * WeaveFrequency + _phase) * Weave;
        _velocity *= Drag;
        _position += _velocity;

        double margin = _radius * 2;
        if (_position.Y < -margin || _position.X < -margin || _position.X > area.Width + margin)
            Active = false;
    }

    public void Draw(DrawingContext dc)
    {
        double life = _age / (double)_lifetime;
        double alpha = Math.Min(life / FadeIn, 1)
            * Math.Min((1 - life) / FadeOut, 1)
            * (0.75 + 0.25 * Math.Sin(_age * PulseFrequency + _phase))
            * _strength;
        if (alpha <= 0.004) return;

        dc.PushOpacity(alpha);
        dc.DrawImage(_sprite, new Rect(_position.X - _radius, _position.Y - _radius, _radius * 2, _radius * 2));
        dc.Pop();
    }

    private static double Range(Random random, double min, double max) => min + random.NextDouble() * (max - min);
}

/// <summary>
/// Keeps a field topped up with motes: <paramref name="initial"/> scattered at the start, then
/// <paramref name="perSecond"/> more while fewer than <paramref name="limit"/> are alive.
/// Ported from TrelamiumIII's <c>ScreenEmitter</c>.
/// </summary>
internal sealed class MoteEmitter(double perSecond, int initial, int limit)
{
    private double _accumulated;
    private bool _seeded;

    public void Update(List<GlowMote> motes, Func<bool, GlowMote> create, double stepsPerSecond)
    {
        if (!_seeded)
        {
            _seeded = true;
            for (int i = 0; i < initial && motes.Count < limit; i++)
                motes.Add(create(true));
        }

        _accumulated += perSecond / stepsPerSecond;
        while (_accumulated >= 1)
        {
            _accumulated -= 1;
            if (motes.Count < limit)
                motes.Add(create(false));
        }
    }

    /// <summary>Start over with a fresh scattered batch next update.</summary>
    public void Reset()
    {
        _seeded = false;
        _accumulated = 0;
    }
}

/// <summary>The orbs' colors and brightness.</summary>
internal sealed record AmbientLook(Color Low, Color High, Color Core, double Strength)
{
    /// <summary>Blue orbs with a near-white core (like GlowMote's), added on top of the dark page like real glows.
    /// They drift under the text, so they're kept soft enough never to get in the way of reading.</summary>
    public static readonly AmbientLook Blue = new(
        Low: Color.FromRgb(56, 112, 255), High: Color.FromRgb(120, 206, 255), Core: Color.FromRgb(220, 245, 255),
        Strength: 0.5);
}
