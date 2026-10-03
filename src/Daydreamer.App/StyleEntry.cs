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
    private readonly Keywords? _keywords;

    public StyleEntry(TextStyle style, ElementType? type, Func<ThemePalette, ResolvedStyle> resolve, Keywords? keywords = null)
    {
        Style = style;
        Type = type;
        _resolve = resolve;
        _keywords = keywords;
    }

    public TextStyle Style { get; }

    /// <summary>The kind of line this entry styles, or null for "Default text".</summary>
    public ElementType? Type { get; }

    /// <summary>"Default text", "Description", "Scene" (if panels are called SCENE)…</summary>
    public string Name => Type is { } type ? _keywords?.NameOf(type) ?? StyleSheet.DisplayNameOf(type) : "Default text";

    /// <summary>A sample line, as it would read on the page (hidden tags and "# " aren't shown there either).</summary>
    public string Example => Type switch
    {
        null => "The quick brown fox",
        ElementType.Heading => "Chapter 1",
        ElementType.Page => Word(ElementType.Page) + " 1",
        ElementType.Panel => Word(ElementType.Panel) + " 1",
        ElementType.Caption => Word(ElementType.Caption) + ": Tokyo, 2045.",
        ElementType.Sfx => Word(ElementType.Sfx) + ": KRAKOOM",
        ElementType.Note => "Note to self",
        { } type => StyleSheet.ExampleOf(type),
    };

    public string Description => Type switch
    {
        null => "The starting point for everything. Other styles only change what they set.",
        ElementType.Heading => "Chapter and scene titles. Pick Heading from the line menu (or start a line with #).",
        ElementType.Page => $"{Word(ElementType.Page)} 1, {Word(ElementType.Page)}S 4-5 … or any line set to {Name} in the line menu.",
        ElementType.Panel => $"{Word(ElementType.Panel)} 1, {Word(ElementType.Panel)} 2 … or any line set to {Name} in the line menu.",
        ElementType.Action => "Panel descriptions, prose — any line that isn't something else.",
        ElementType.Dialogue => "A character's name, then their line (with or without a colon). Each character's own style is added on top.",
        ElementType.Caption => $"{Word(ElementType.Caption)}: narration boxes.",
        ElementType.Sfx => $"{Word(ElementType.Sfx)} or FX: sound effects.",
        ElementType.Note => "Notes to yourself (// at the start of a line, or Note in the line menu). Not counted in word totals.",
        _ => "",
    };

    private string Word(ElementType type) => _keywords?.For(type) ?? Keywords.DefaultFor(type);

    /// <summary>How this kind of line looks in the given theme.</summary>
    public ResolvedStyle Resolve(ThemePalette palette) => _resolve(palette);

    private ResolvedStyle Resolved => _resolve(ThemeManager.Palette);

    public FontFamily PreviewFont => WpfStyles.Family(Resolved.FontFamily, 1);
    public double PreviewSize => Math.Min(WpfStyles.PointsToPixels(Resolved.FontSize), 22); // keep big headings tidy
    public FontWeight PreviewWeight => Resolved.Bold ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle PreviewStyle => Resolved.Italic ? FontStyles.Italic : FontStyles.Normal;
    public Brush PreviewBrush => WpfStyles.TextBrush(Resolved.Color, ThemeManager.Palette);
    public TextDecorationCollection PreviewDecorations => Resolved.Underline ? WpfStyles.Underline : WpfStyles.NoDecorations;

    /// <summary>Tells the UI every property may have changed (names too, if a keyword was renamed).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    public override string ToString() => Name; // what screen readers announce for the row

    public static List<StyleEntry> CreateFor(StyleSheet sheet, Keywords? keywords = null)
    {
        var entries = new List<StyleEntry>
        {
            new(sheet.Base, type: null, palette => StyleResolver.ResolveBase(sheet, palette), keywords),
        };

        foreach (var element in sheet.Elements)
        {
            var type = element.Type;
            entries.Add(new StyleEntry(element.Style, type, palette => StyleResolver.ResolveElement(sheet, type, palette), keywords));
        }
        return entries;
    }
}
