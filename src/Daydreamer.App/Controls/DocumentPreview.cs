using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.App.Controls;

/// <summary>
/// A small picture of the top of a document's first page, drawn with the project's real formatting
/// (the same parser and style rules as the editor), shrunk to fit — like a page thumbnail in Google Docs.
/// </summary>
public sealed class DocumentPreview : FrameworkElement
{
    // The preview lays text out on a full-size page (8.5in, 1in margins), then scales the page down.
    private const double PageWidth = 816;
    private const double PageMargin = 72;
    private const double TopMargin = 64;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DocumentPreview), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StylesProperty = DependencyProperty.Register(
        nameof(Styles), typeof(StyleSheet), typeof(DocumentPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CharactersProperty = DependencyProperty.Register(
        nameof(Characters), typeof(IEnumerable<Character>), typeof(DocumentPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KeywordsProperty = DependencyProperty.Register(
        nameof(Keywords), typeof(Keywords), typeof(DocumentPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The project's words for PAGE, PANEL and so on.</summary>
    public Keywords? Keywords
    {
        get => (Keywords?)GetValue(KeywordsProperty);
        set => SetValue(KeywordsProperty, value);
    }

    /// <summary>The start of the document (only the first page's worth is drawn).</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StyleSheet? Styles
    {
        get => (StyleSheet?)GetValue(StylesProperty);
        set => SetValue(StylesProperty, value);
    }

    public IEnumerable<Character>? Characters
    {
        get => (IEnumerable<Character>?)GetValue(CharactersProperty);
        set => SetValue(CharactersProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Styles is not { } styles || string.IsNullOrEmpty(Text) || ActualWidth <= 0) return;

        var palette = ThemeManager.Palette;
        var parser = new ScriptParser(new CharacterRegistry(Characters ?? []), Keywords);
        double scale = ActualWidth / PageWidth;
        double pageHeight = ActualHeight / scale;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var baseStyle = StyleResolver.ResolveBase(styles, palette);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.PushTransform(new ScaleTransform(scale, scale));

        double y = TopMargin;
        bool first = true;
        foreach (string raw in ScriptParser.SplitLines(Text))
        {
            if (y > pageHeight) break;
            var line = parser.ParseLine(raw);
            var body = StyleResolver.ResolveBody(styles, line, palette);
            if (!first) y += WpfStyles.PointsToPixels(body.SpaceBefore); // paragraph spacing, as in the editor
            first = false;
            if (line.Type == ElementType.Blank)
            {
                y += Measure(" ", baseStyle, palette, pixelsPerDip).Height;
                continue;
            }

            // Like the editor, the page shows the words without their markup ({tags}, "# ", **).
            var hidden = line.Markup();
            int Shown(int index) => index - hidden.Where(r => r.Start < index).Sum(r => Math.Min(r.End, index) - r.Start);
            string shown = string.Concat(raw.Select((c, i) => hidden.Any(r => i >= r.Start && i < r.End) ? "" : c.ToString()));
            if (shown.Length == 0) shown = " ";

            var segments = StyleResolver.Segment(styles, line, raw.Length, palette);
            var text = Measure(shown, segments[0].Style, palette, pixelsPerDip);
            foreach (var segment in segments.Skip(1))
            {
                int start = Shown(segment.Range.Start), end = Shown(segment.Range.End);
                if (end <= start || end > shown.Length) continue;
                text.SetFontTypeface(WpfStyles.Typeface(segment.Style), start, end - start);
                text.SetFontSize(WpfStyles.PointsToPixels(segment.Style.FontSize), start, end - start);
                text.SetForegroundBrush(WpfStyles.TextBrush(segment.Style.Color, palette), start, end - start);
                if (segment.IsCompleted) text.SetTextDecorations(TextDecorations.Strikethrough, start, end - start);
            }
            dc.DrawText(text, new Point(PageMargin, y));
            y += text.Height;
        }

        dc.Pop();
        dc.Pop();
    }

    private static FormattedText Measure(string text, ResolvedStyle style, ThemePalette palette, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, WpfStyles.Typeface(style),
            WpfStyles.PointsToPixels(style.FontSize), WpfStyles.TextBrush(style.Color, palette), pixelsPerDip)
        {
            MaxTextWidth = PageWidth - PageMargin * 2,
            TextAlignment = style.Alignment switch
            {
                TextAlign.Center => TextAlignment.Center,
                TextAlign.Right => TextAlignment.Right,
                TextAlign.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
        };
}
