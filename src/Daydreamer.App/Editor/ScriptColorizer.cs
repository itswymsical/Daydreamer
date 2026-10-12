using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Daydreamer.App.Editor;

/// <summary>
/// The heart of the "dynamic formatting". AvalonEdit calls <see cref="ColorizeLine"/> for every
/// line it's about to draw. We parse that line, ask Core how each part should look, and apply it.
/// The text itself is never changed — formatting is purely how it's displayed — so changing a
/// rule and calling Redraw() instantly restyles the whole document.
/// </summary>
public sealed class ScriptColorizer : DocumentColorizingTransformer
{
    private readonly ScriptParser _parser;

    public ScriptColorizer(ScriptParser parser)
    {
        _parser = parser;
    }

    /// <summary>The project's formatting rules. Null means "draw plain text".</summary>
    public StyleSheet? Styles { get; set; }

    /// <summary>The light or dark grays used wherever a style has no color of its own.</summary>
    public ThemePalette Palette { get; set; } = ThemePalette.Light;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Styles is null || line.Length == 0) return;

        string text = CurrentContext.Document.GetText(line);
        var parsed = _parser.ParseLine(text);

        foreach (var segment in StyleResolver.Segment(Styles, parsed, text.Length, Palette))
        {
            if (segment.Range.IsEmpty) continue;

            var typeface = WpfStyles.Typeface(segment.Style);
            var brush = WpfStyles.TextBrush(segment.Style.Color, Palette);
            double size = WpfStyles.PointsToPixels(segment.Style.FontSize);
            var decorations = segment.IsWarning ? WpfStyles.WarningUnderline(Palette)
                : segment.IsCompleted ? WpfStyles.Strikethrough // a ticked checklist item
                : segment.Style.Underline ? WpfStyles.Underline
                : WpfStyles.NoDecorations;

            ChangeLinePart(line.Offset + segment.Range.Start, line.Offset + segment.Range.End, element =>
            {
                element.TextRunProperties.SetTypeface(typeface);
                element.TextRunProperties.SetFontRenderingEmSize(size);
                element.TextRunProperties.SetForegroundBrush(brush);
                element.TextRunProperties.SetTextDecorations(decorations);
            });
        }
    }
}
