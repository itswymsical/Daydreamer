using System.Globalization;
using System.Windows.Media;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

namespace Daydreamer.App;

/// <summary>
/// What the toolbar's font, size and color controls are editing right now. In Daydreamer you don't
/// format individual words — you format a kind of line. So with the cursor on an AKIRA line, picking
/// a font changes every AKIRA line; on an SFX line, every SFX line; on an empty line, "Default text".
/// The controls show the style as it currently looks, and write only what you change.
/// </summary>
public sealed class FormatTarget : ObservableObject
{
    private TextStyle? _style;
    private Func<ThemePalette, ResolvedStyle>? _resolve;

    /// <summary>"AKIRA", "SFX", "Default text"…</summary>
    public string Label { get; private set; } = "Nothing selected";

    /// <summary>An icon-font glyph: a person for characters, an "A" for everything else.</summary>
    public string Icon { get; private set; } = "";

    /// <summary>The <see cref="Character"/> or <see cref="StyleEntry"/> this is, so the chip can open its full settings.</summary>
    public object? Source { get; private set; }

    public bool IsEnabled => _style != null;

    public string ToolTip => _style == null
        ? "Open a document to change fonts"
        : $"Font, size, color, line spacing and alignment here change every {Label} line. Click to see all of its settings.";

    public void Set(string label, string icon, TextStyle style, Func<ThemePalette, ResolvedStyle> resolve, object source)
    {
        bool unchanged = ReferenceEquals(style, _style) && label == Label;
        (Label, Icon, _style, _resolve, Source) = (label, icon, style, resolve, source);
        if (!unchanged) Refresh(); // moving around within the same kind of line changes nothing
    }

    public void Clear()
    {
        if (_style == null && Source == null) return;
        (Label, Icon, _style, _resolve, Source) = ("Nothing selected", "", null, null, null);
        Refresh();
    }

    private ResolvedStyle? Resolved => _resolve?.Invoke(ThemeManager.Palette);

    /// <summary>The font as it currently looks. Choosing one sets it on the target style.</summary>
    public string? FontFamily
    {
        get => Resolved?.FontFamily;
        set
        {
            // Re-picking the font it already has shouldn't turn an inherited value into a fixed one.
            if (_style != null && !string.IsNullOrWhiteSpace(value) && value != FontFamily)
                _style.FontFamily = value;
        }
    }

    /// <summary>The size in points, as text so the box can be typed into.</summary>
    public string? FontSize
    {
        get => Resolved?.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
        set
        {
            if (_style == null || Resolved is not { } current) return;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double size)
                && size is >= 6 and <= 96 && Math.Abs(size - current.FontSize) > 0.01)
            {
                _style.FontSize = size;
            }
        }
    }

    /// <summary>Line spacing as it currently looks (1 = single). Setting it applies to every line of this kind.</summary>
    public double? LineSpacing
    {
        get => Resolved?.LineSpacing;
        set
        {
            if (_style == null || value is not { } spacing || Resolved is not { } current) return;
            if (spacing is >= 0.5 and <= 4 && Math.Abs(spacing - current.LineSpacing) > 0.001)
                _style.LineSpacing = spacing;
        }
    }

    /// <summary>Alignment as it currently looks. Setting it applies to every line of this kind.</summary>
    public TextAlign? Alignment
    {
        get => Resolved?.Alignment;
        set
        {
            if (_style != null && value is { } alignment && Resolved is { } current && alignment != current.Alignment)
                _style.Alignment = alignment;
        }
    }

    /// <summary>The color chosen for this target, or null for the default gray.</summary>
    public string? Color
    {
        get => _style?.Color;
        set
        {
            if (_style != null) _style.Color = value;
        }
    }

    /// <summary>The color the text actually appears in — shown as the bar under the toolbar's "A".</summary>
    public Brush EffectiveBrush => Resolved is { } style
        ? WpfStyles.TextBrush(style.Color, ThemeManager.Palette)
        : Brushes.Transparent;

    /// <summary>Re-reads everything (after a style edit, an undo, or a theme switch).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
