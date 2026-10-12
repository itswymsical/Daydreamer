using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Daydreamer.App.Editor;
using Daydreamer.Core.Export;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

namespace Daydreamer.App.Export;

/// <summary>
/// Puts an exported document onto pages with WPF's text engine — the one the editor draws with — so lines wrap,
/// space out and find fonts (including stand-ins for Japanese text) just as they do on screen. Each page comes
/// back as a WPF <see cref="Drawing"/> in WPF units (1/96 inch), with 0,0 at the top left corner of the paper.
/// </summary>
public static class PageLayout
{
    /// <summary>One indent step: half an inch, as in Word and Google Docs.</summary>
    private const double IndentStep = 48;

    public static List<Drawing> Paginate(ExportDocument document, PaperSize paper)
    {
        double margin = WpfStyles.PointsToPixels(paper.Margin);
        double width = WpfStyles.PointsToPixels(paper.TextWidth);
        double bottom = WpfStyles.PointsToPixels(paper.Height) - margin;
        var formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        var paragraphs = document.Paragraphs;

        // Paragraphs are broken into lines as they're needed. A heading looks one paragraph ahead (see below),
        // and the lines it worked out are kept here for when that paragraph's turn comes.
        var formatted = new Dictionary<int, List<PlacedLine>>();
        List<PlacedLine> LinesOf(int index) =>
            formatted.Remove(index, out var lines) ? lines : Format(paragraphs[index], formatter, width);

        var pages = new List<Drawing>();
        var page = new DrawingGroup();
        var drawing = page.Open();
        double y = margin;
        bool pageIsEmpty = true;

        void StartNewPage()
        {
            drawing.Close();
            pages.Add(page);
            page = new DrawingGroup();
            drawing = page.Open();
            y = margin;
            pageIsEmpty = true;
        }

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var lines = LinesOf(i);

            // Empty lines that fall at the top of a new page would just look like a bigger margin.
            if (pageIsEmpty && pages.Count > 0 && paragraph.Type == ElementType.Blank)
            {
                lines.ForEach(l => l.Line.Dispose());
                continue;
            }

            // Space above the paragraph, like in the editor — except at the top of a page.
            double space = pageIsEmpty ? 0 : WpfStyles.PointsToPixels(paragraph.Style.SpaceBefore);

            // Headings, pages and panels never sit alone at the bottom of a page: if the first line after
            // them won't fit too, they move to the next page with it.
            if (!pageIsEmpty && KeepsWithNext(paragraph) && i + 1 < paragraphs.Count)
            {
                var next = formatted[i + 1] = LinesOf(i + 1);
                double needed = space + lines.Sum(l => l.Line.Height)
                    + WpfStyles.PointsToPixels(paragraphs[i + 1].Style.SpaceBefore) + next[0].Line.Height;
                if (y + needed > bottom)
                {
                    StartNewPage();
                    space = 0;
                }
            }

            foreach (var (line, x) in lines)
            {
                if (!pageIsEmpty && y + space + line.Height > bottom)
                {
                    StartNewPage();
                    space = 0;
                }
                line.Draw(drawing, new Point(margin + x, y + space), InvertAxes.None);
                y += space + line.Height;
                space = 0;
                pageIsEmpty = false;
                line.Dispose();
            }
        }

        drawing.Close();
        if (!pageIsEmpty || pages.Count == 0) pages.Add(page);
        return pages;
    }

    private static bool KeepsWithNext(ExportParagraph paragraph) =>
        paragraph.Type is ElementType.Heading or ElementType.Page or ElementType.Panel;

    /// <summary>A line of text, ready to draw, and how far in from the left margin it starts.</summary>
    private readonly record struct PlacedLine(TextLine Line, double X);

    /// <summary>Breaks a paragraph into lines that fit the page width.</summary>
    private static List<PlacedLine> Format(ExportParagraph paragraph, TextFormatter formatter, double width)
    {
        var source = new ParagraphSource();
        var defaults = new RunProperties(paragraph.Style);

        // A list item's marker goes first. Checkboxes come from a font that's sure to have them.
        double hanging = 0;
        if (paragraph.Marker.Length > 0)
        {
            var markerLook = paragraph.List == ListKind.Checkbox
                ? paragraph.Style with { FontFamily = "Segoe UI Symbol", Bold = false, Italic = false }
                : paragraph.Style;
            var marker = new RunProperties(markerLook);
            source.Add(paragraph.Marker + " ", marker);
            hanging = Measure(paragraph.Marker + " ", marker);
        }
        foreach (var run in paragraph.Runs)
            source.Add(run.Text, new RunProperties(run.Style, run.Strikethrough));

        var alignment = paragraph.Style.Alignment switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            TextAlign.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
        double indent = Math.Min(paragraph.IndentLevel * IndentStep, width / 2);

        var lines = new List<PlacedLine>();
        int position = 0;
        TextLineBreak? lineBreak = null;
        while (true)
        {
            // Wrapped rows of a list item line up with its words, not its marker (as in the editor).
            double x = indent + (lines.Count > 0 ? hanging : 0);
            var settings = new ParagraphProperties(alignment, defaults, firstLine: lines.Count == 0);
            var line = formatter.FormatLine(source, position, Math.Max(1, width - x), settings, lineBreak);
            lines.Add(new PlacedLine(line, x));
            position += line.Length;
            lineBreak = line.GetTextLineBreak();
            // The end-of-paragraph mark counts as one more character after the text.
            if (line.Length == 0 || position >= source.Length + 1) break;
        }
        return lines;
    }

    private static double Measure(string text, RunProperties look) =>
        new FormattedText(text, look.CultureInfo, FlowDirection.LeftToRight, look.Typeface, look.FontRenderingEmSize,
            look.ForegroundBrush, pixelsPerDip: 1).WidthIncludingTrailingWhitespace;

    // ── What WPF's text engine asks for ────────────────────────────────────
    // TextFormatter reads a paragraph through these three small classes: the text and its looks (the source),
    // how each piece looks (run properties), and how the paragraph is set (paragraph properties).

    /// <summary>A paragraph's text, in pieces that each have one look.</summary>
    private sealed class ParagraphSource : TextSource
    {
        private readonly StringBuilder _builder = new();
        private readonly List<(int Start, int Length, TextRunProperties Look)> _runs = [];
        private string? _text;

        public int Length => _builder.Length;

        private string Text => _text ??= _builder.ToString();

        public void Add(string text, TextRunProperties look)
        {
            if (text.Length == 0) return;
            _runs.Add((_builder.Length, text.Length, look));
            _builder.Append(text);
            _text = null;
        }

        public override TextRun GetTextRun(int textSourceCharacterIndex)
        {
            foreach (var (start, length, look) in _runs)
            {
                if (textSourceCharacterIndex >= start && textSourceCharacterIndex < start + length)
                    return new TextCharacters(Text, textSourceCharacterIndex, start + length - textSourceCharacterIndex, look);
            }
            return new TextEndOfParagraph(1);
        }

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
        {
            int limit = Math.Clamp(textSourceCharacterIndexLimit, 0, Text.Length);
            return new TextSpan<CultureSpecificCharacterBufferRange>(limit,
                new CultureSpecificCharacterBufferRange(CultureInfo.CurrentUICulture, new CharacterBufferRange(Text, 0, limit)));
        }

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) =>
            textSourceCharacterIndex;
    }

    /// <summary>How a piece of text looks: the same fonts, sizes and colors the editor uses (in light mode).</summary>
    private sealed class RunProperties : TextRunProperties
    {
        public RunProperties(ResolvedStyle style, bool strikethrough = false)
        {
            // The typeface carries the line spacing, exactly as in the editor (see WpfStyles.Family).
            Typeface = WpfStyles.Typeface(style);
            FontRenderingEmSize = WpfStyles.PointsToPixels(style.FontSize);
            ForegroundBrush = WpfStyles.TextBrush(style.Color, ExportDocument.Palette);
            if (style.Underline && strikethrough)
            {
                var both = new TextDecorationCollection(WpfStyles.Underline.Concat(WpfStyles.Strikethrough));
                both.Freeze();
                TextDecorations = both;
            }
            else
            {
                TextDecorations = style.Underline ? WpfStyles.Underline : strikethrough ? WpfStyles.Strikethrough : null;
            }
        }

        public override Typeface Typeface { get; }
        public override double FontRenderingEmSize { get; }
        public override double FontHintingEmSize => FontRenderingEmSize;
        public override TextDecorationCollection? TextDecorations { get; }
        public override Brush ForegroundBrush { get; }
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => CultureInfo.CurrentUICulture;
        public override TextEffectCollection? TextEffects => null;
    }

    /// <summary>How a paragraph is set: its alignment and wrapping.</summary>
    private sealed class ParagraphProperties(TextAlignment alignment, TextRunProperties defaults, bool firstLine) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => alignment;
        public override double LineHeight => 0; // the font sets it (see RunProperties)
        public override bool FirstLineInParagraph => firstLine;
        public override TextRunProperties DefaultTextRunProperties => defaults;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
        public override double DefaultIncrementalTab => IndentStep;
    }
}
