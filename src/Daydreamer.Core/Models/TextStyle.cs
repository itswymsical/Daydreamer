namespace Daydreamer.Core.Models;

/// <summary>
/// A set of formatting choices. Every property is optional: <c>null</c> means "inherit".
/// Styles stack on top of each other — Default text, then the element (e.g. Dialogue),
/// then the character, then a modifier like (thought). The last one to set a value wins.
/// </summary>
public sealed class TextStyle : ObservableObject
{
    private string? _fontFamily;
    private double? _fontSize;
    private bool? _bold;
    private bool? _italic;
    private bool? _underline;
    private string? _color;
    private double? _lineSpacing;
    private TextAlign? _alignment;
    private double? _spaceBefore;

    public string? FontFamily
    {
        get => _fontFamily;
        set => SetField(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Size in points, like a word processor (12 pt is a typical body size).</summary>
    public double? FontSize
    {
        get => _fontSize;
        set => SetField(ref _fontSize, value is > 0 ? value : null);
    }

    public bool? Bold { get => _bold; set => SetField(ref _bold, value); }

    public bool? Italic { get => _italic; set => SetField(ref _italic, value); }

    public bool? Underline { get => _underline; set => SetField(ref _underline, value); }

    /// <summary>Hex color such as <c>#E03131</c>.</summary>
    public string? Color
    {
        get => _color;
        set => SetField(ref _color, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>
    /// Space between lines, as a multiple of the font's normal spacing: 1 is single, 1.15 a little airier,
    /// 2 is double. Anything outside 0.5–4 is treated as "inherit".
    /// </summary>
    public double? LineSpacing
    {
        get => _lineSpacing;
        set => SetField(ref _lineSpacing, value is >= 0.5 and <= 4 ? Math.Round(value.Value, 2) : null);
    }

    /// <summary>Left, center, right or justified. Null means inherit (and in the end, left).</summary>
    public TextAlign? Alignment { get => _alignment; set => SetField(ref _alignment, value); }

    /// <summary>
    /// Extra space above each paragraph, in points (like "space before paragraph" in Google Docs or Word):
    /// 0 is none, 12 is about one blank line. Anything outside 0–72 is treated as "inherit".
    /// </summary>
    public double? SpaceBefore
    {
        get => _spaceBefore;
        set => SetField(ref _spaceBefore, value is >= 0 and <= 72 ? Math.Round(value.Value, 1) : null);
    }

    public TextStyle Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Color = Color,
        LineSpacing = LineSpacing,
        Alignment = Alignment,
        SpaceBefore = SpaceBefore,
    };
}
