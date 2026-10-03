using System.Windows;
using System.Windows.Media;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

namespace Daydreamer.App;

/// <summary>
/// One row in the Styles list. Shows an example line drawn in the style it will actually have,
/// so the list itself is a live preview.
/// </summary>
public sealed class StyleEntry : ObservableObject
{
    private readonly Func<ThemePalette, ResolvedStyle> _resolve;

    public StyleEntry(string name, string example, string description, TextStyle style, ElementType? type,
        Func<ThemePalette, ResolvedStyle> resolve)
    {
        Name = name;
        Example = example;
        Description = description;
        Style = style;
        Type = type;
        _resolve = resolve;
    }

    public string Name { get; }
    public string Example { get; }
    public string Description { get; }
    public TextStyle Style { get; }

    /// <summary>The kind of line this entry styles, or null for "Default text".</summary>
    public ElementType? Type { get; }

    /// <summary>How this kind of line looks in the given theme.</summary>
    public ResolvedStyle Resolve(ThemePalette palette) => _resolve(palette);

    private ResolvedStyle Resolved => _resolve(ThemeManager.Palette);

    public FontFamily PreviewFont => new(Resolved.FontFamily);
    public double PreviewSize => Math.Min(WpfStyles.PointsToPixels(Resolved.FontSize), 22); // keep big headings tidy
    public FontWeight PreviewWeight => Resolved.Bold ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle PreviewStyle => Resolved.Italic ? FontStyles.Italic : FontStyles.Normal;
    public Brush PreviewBrush => WpfStyles.TextBrush(Resolved.Color, ThemeManager.Palette);
    public TextDecorationCollection PreviewDecorations => Resolved.Underline ? WpfStyles.Underline : WpfStyles.NoDecorations;

    /// <summary>Tells the UI every preview property may have changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    public override string ToString() => Name; // what screen readers announce for the row

    public static List<StyleEntry> CreateFor(StyleSheet sheet)
    {
        var entries = new List<StyleEntry>
        {
            new("Default text", "The quick brown fox", "The starting point for everything. Other styles only change what they set.",
                sheet.Base, type: null, palette => StyleResolver.ResolveBase(sheet, palette)),
        };

        foreach (var element in sheet.Elements)
        {
            var type = element.Type;
            entries.Add(new StyleEntry(element.DisplayName, element.Example, DescriptionOf(type), element.Style, type,
                palette => StyleResolver.ResolveElement(sheet, type, palette)));
        }
        return entries;
    }

    private static string DescriptionOf(ElementType type) => type switch
    {
        ElementType.Heading => "Lines starting with #. Chapter and scene titles.",
        ElementType.Page => "PAGE 1, PAGES 4-5 …",
        ElementType.Panel => "PANEL 1, PANEL 2 …",
        ElementType.Action => "Panel descriptions, prose — any line that isn't something else.",
        ElementType.Dialogue => "NAME: line. Each character's own style is added on top of this one.",
        ElementType.Caption => "CAPTION: narration boxes.",
        ElementType.Sfx => "SFX: or FX: sound effects.",
        ElementType.Note => "// or NOTE: — notes to yourself. Not counted in word totals.",
        _ => "",
    };
}
