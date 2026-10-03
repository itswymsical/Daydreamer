using System.Windows;
using System.Windows.Media;
using Daydreamer.Core.Formatting;

namespace Daydreamer.App.Editor;

/// <summary>
/// Turns Core's plain style values (font names, hex colors, point sizes) into WPF drawing objects.
/// Results are cached because the editor asks for them on every redraw.
/// </summary>
public static class WpfStyles
{
    /// <summary>Custom colors are nudged until they reach at least this contrast against the page.</summary>
    private const double MinimumContrast = 3.0;

    private static readonly Dictionary<(string, bool, bool, int), Typeface> Typefaces = [];
    private static readonly Dictionary<(string, int), FontFamily> Families = [];
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string), SolidColorBrush> ReadableBrushes = [];
    private static readonly Dictionary<string, TextDecorationCollection> WarningUnderlines = [];

    public static readonly TextDecorationCollection NoDecorations = Freeze(new TextDecorationCollection());
    public static readonly TextDecorationCollection Underline = TextDecorations.Underline;

    /// <summary>Word processors measure fonts in points (1/72 inch); WPF uses 1/96 inch.</summary>
    public static double PointsToPixels(double points) => points * 96.0 / 72.0;

    public static Typeface Typeface(ResolvedStyle style)
    {
        var key = (style.FontFamily, style.Bold, style.Italic, Hundredths(style.LineSpacing));
        if (!Typefaces.TryGetValue(key, out var typeface))
        {
            typeface = new Typeface(
                Family(style.FontFamily, style.LineSpacing),
                style.Italic ? FontStyles.Italic : FontStyles.Normal,
                style.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            Typefaces[key] = typeface;
        }
        return typeface;
    }

    /// <summary>
    /// A font with its own line spacing. WPF works out how tall a line is from the font, so to space lines
    /// we wrap the real font in a "composite font" (a built-in WPF feature): it draws exactly the same letters,
    /// but reports taller lines. Moving the baseline down by half the extra space keeps the text centred in it.
    /// Because this is part of the font, wrapped lines in a long paragraph get the same spacing too.
    /// </summary>
    /// <param name="lineSpacing">1 = the font's normal (single) spacing, 2 = double.</param>
    public static FontFamily Family(string name, double lineSpacing)
    {
        var key = (name, Hundredths(lineSpacing));
        if (Families.TryGetValue(key, out var cached)) return cached;

        var physical = new FontFamily(name);
        FontFamily family = physical;
        if (key.Item2 != 100)
        {
            double natural = physical.LineSpacing;
            family = new FontFamily();
            family.FamilyMaps.Add(new FontFamilyMap { Target = name, Unicode = "0000-10ffff" });
            family.LineSpacing = natural * key.Item2 / 100.0;
            family.Baseline = physical.Baseline + (family.LineSpacing - natural) / 2;
        }

        Families[key] = family;
        return family;
    }

    private static int Hundredths(double lineSpacing) => (int)Math.Round(lineSpacing * 100);

    /// <summary>Hex like "#E03131" → brush. Anything unreadable falls back to the given brush.</summary>
    public static SolidColorBrush Brush(string? color, SolidColorBrush? fallback = null)
    {
        fallback ??= System.Windows.Media.Brushes.Gray;
        if (string.IsNullOrWhiteSpace(color)) return fallback;
        if (Brushes.TryGetValue(color, out var cached)) return cached;

        var parsed = TryParseColor(color);
        if (parsed is null) return fallback;

        var brush = Frozen(parsed.Value);
        Brushes[color] = brush;
        return brush;
    }

    /// <summary>
    /// The brush for a piece of text: the theme's grays as they are (some are faint on purpose),
    /// and any color someone picked made readable on the current page.
    /// </summary>
    public static SolidColorBrush TextBrush(string? color, ThemePalette palette) =>
        color != null && palette.Contains(color) ? Brush(color) : ReadableBrush(color, palette.Paper);

    /// <summary>
    /// Like <see cref="Brush"/>, but if the color would be hard to read on <paramref name="paper"/>
    /// (dark blue in dark mode, pale yellow in light mode…) it's lightened or darkened just enough.
    /// The theme's own grays already pass, so they come back unchanged.
    /// </summary>
    public static SolidColorBrush ReadableBrush(string? color, string paper)
    {
        if (string.IsNullOrWhiteSpace(color)) return System.Windows.Media.Brushes.Gray;
        if (ReadableBrushes.TryGetValue((color, paper), out var cached)) return cached;

        var foreground = TryParseColor(color);
        var background = TryParseColor(paper) ?? Colors.White;
        if (foreground is null) return System.Windows.Media.Brushes.Gray;

        var brush = Frozen(EnsureContrast(foreground.Value, background, MinimumContrast));
        ReadableBrushes[(color, paper)] = brush;
        return brush;
    }

    public static Color? TryParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(color.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A dotted underline for speakers that aren't defined characters yet, in the theme's warning color.</summary>
    public static TextDecorationCollection WarningUnderline(ThemePalette palette)
    {
        if (WarningUnderlines.TryGetValue(palette.Warning, out var cached)) return cached;

        var pen = new Pen(Brush(palette.Warning), 1.5)
        {
            DashStyle = new DashStyle([1.5, 1.5], 0),
            DashCap = PenLineCap.Round,
        };
        pen.Freeze();
        var decorations = Freeze(new TextDecorationCollection
        {
            new TextDecoration(TextDecorationLocation.Underline, pen, 2, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel),
        });
        WarningUnderlines[palette.Warning] = decorations;
        return decorations;
    }

    // ── Contrast (the same math WCAG accessibility guidelines use) ─────────

    private static Color EnsureContrast(Color foreground, Color background, double minimum)
    {
        if (ContrastRatio(foreground, background) >= minimum) return foreground;

        // Blend toward white on dark pages and toward black on light ones, a little at a time.
        Color target = RelativeLuminance(background) < 0.5 ? Colors.White : Colors.Black;
        for (double t = 0.1; t < 1.0; t += 0.1)
        {
            var candidate = Lerp(foreground, target, t);
            if (ContrastRatio(candidate, background) >= minimum) return candidate;
        }
        return target;
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte value)
        {
            double s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static TextDecorationCollection Freeze(TextDecorationCollection collection)
    {
        collection.Freeze();
        return collection;
    }
}
