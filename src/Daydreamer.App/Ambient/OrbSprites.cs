using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Daydreamer.App.Ambient;

/// <summary>
/// Pre-drawn orb images — the WPF version of TrelamiumIII's RadialGradient texture. Drawing a ready-made
/// image is far cheaper than re-rasterizing a gradient brush every frame, so each look gets a handful of
/// tints (from its Low to High color), each with the soft glow and bright core baked in.
///
/// The images are then turned into pure light — see <see cref="AsLight"/>.
/// </summary>
internal sealed class OrbSprites
{
    private const int Tints = 6;
    private const int Size = 96;            // pixels; scaled down to each orb's radius when drawn
    private const double CoreScale = 0.32;  // the bright centre, relative to the glow

    private readonly BitmapSource[] _sprites;

    public OrbSprites(AmbientLook look)
    {
        _sprites = new BitmapSource[Tints];
        for (int i = 0; i < Tints; i++)
        {
            var sprite = Render(Lerp(look.Low, look.High, i / (double)(Tints - 1)), look.Core);
            _sprites[i] = AsLight(sprite);
        }
    }

    /// <summary>
    /// Additive blending, the same idea as TrelamiumIII's AdditiveColor blend state ("adds light without
    /// raising alpha"). WPF stores these pixels premultiplied — colour already multiplied by alpha — and
    /// blends them onto the page as
    ///     result = sprite.rgb + page.rgb × (1 − sprite.alpha)
    /// Keeping the colour but setting alpha to 0 turns that into  result = page.rgb + sprite.rgb,
    /// so the orb adds light to whatever is under it instead of covering it.
    /// Doing this once to the image is far cheaper than running a pixel shader over the page every frame.
    /// </summary>
    private static BitmapSource AsLight(BitmapSource sprite)
    {
        int stride = sprite.PixelWidth * 4;
        var pixels = new byte[stride * sprite.PixelHeight];
        sprite.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 0; // byte order is blue, green, red, alpha

        var light = BitmapSource.Create(sprite.PixelWidth, sprite.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        light.Freeze();
        return light;
    }

    /// <summary>The sprite closest to a color that's <paramref name="t"/> of the way from Low to High.</summary>
    public BitmapSource For(double t) => _sprites[(int)Math.Round(Math.Clamp(t, 0, 1) * (Tints - 1))];

    private static BitmapSource Render(Color color, Color core)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(Size / 2.0, Size / 2.0);
            double radius = Size / 2.0;
            dc.DrawEllipse(Glow(color, core), null, center, radius, radius);
            dc.DrawEllipse(Core(core), null, center, radius * CoreScale, radius * CoreScale);
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static RadialGradientBrush Glow(Color color, Color core) => new()
    {
        GradientStops =
        {
            new GradientStop(WithAlpha(core, 230), 0.0),
            new GradientStop(WithAlpha(color, 150), 0.18),
            new GradientStop(WithAlpha(color, 72), 0.45),
            new GradientStop(WithAlpha(color, 24), 0.72),
            new GradientStop(WithAlpha(color, 0), 1.0),
        },
    };

    private static RadialGradientBrush Core(Color core) => new()
    {
        GradientStops =
        {
            new GradientStop(WithAlpha(core, 150), 0.0),
            new GradientStop(WithAlpha(core, 50), 0.5),
            new GradientStop(WithAlpha(core, 0), 1.0),
        },
    };

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
