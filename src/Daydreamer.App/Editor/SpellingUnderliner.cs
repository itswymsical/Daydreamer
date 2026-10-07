using System.Windows;
using System.Windows.Media;
using Daydreamer.Core.Proofing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Daydreamer.App.Editor;

/// <summary>
/// Draws a wavy line under misspelled words, like Word and Google Docs. It's drawn behind the text, on the
/// lines on screen only, and never changes the text itself.
/// </summary>
/// <param name="issuesOf">The spelling problems in a line (empty when spell checking is off).</param>
/// <param name="brush">The squiggle color (it differs between light and dark mode).</param>
/// <param name="typingAt">Where the caret is while typing: the word ending there isn't finished yet, so it isn't marked.</param>
public sealed class SpellingUnderliner(Func<DocumentLine, IReadOnlyList<SpellingIssue>> issuesOf, Func<Brush> brush, Func<int> typingAt)
    : IBackgroundRenderer
{
    private const double WaveLength = 4;   // one up-and-down, in pixels
    private const double WaveHeight = 1.6;
    private const double BelowBaseline = 2.5;

    public KnownLayer Layer => KnownLayer.Selection; // behind the text, like a selection highlight

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        var pen = new Pen(brush(), 1.1);
        int typing = typingAt();

        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            foreach (var issue in issuesOf(line))
            {
                var segment = new TextSegment { StartOffset = line.Offset + issue.Range.Start, Length = issue.Range.Length };
                if (segment.EndOffset == typing) continue;

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    // Just under the letters. (With wide line spacing the row is much taller than the text,
                    // so the bottom of the row would be too far down.)
                    var row = visualLine.GetTextLineByVisualYPosition(rect.Top + textView.VerticalOffset + 1);
                    double baseline = visualLine.GetTextLineVisualYPosition(row, VisualYPosition.Baseline) - textView.VerticalOffset;
                    drawingContext.DrawGeometry(null, pen, Wave(rect.Left, rect.Right, baseline + BelowBaseline));
                }
            }
        }
    }

    /// <summary>A zigzag from left to right, centred on the height y.</summary>
    private static StreamGeometry Wave(double left, double right, double y)
    {
        var wave = new StreamGeometry();
        using (var draw = wave.Open())
        {
            draw.BeginFigure(new Point(left, y + WaveHeight / 2), isFilled: false, isClosed: false);
            bool up = true;
            for (double x = left + WaveLength / 2; x <= right + 0.01; x += WaveLength / 2)
            {
                draw.LineTo(new Point(x, y + (up ? -WaveHeight : WaveHeight) / 2), isStroked: true, isSmoothJoin: true);
                up = !up;
            }
        }
        wave.Freeze();
        return wave;
    }
}
