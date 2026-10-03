using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Daydreamer.App.Editor;

/// <summary>
/// Draws list markers nicely without changing the text: "- " shows as a "•" bullet, and "[ ]" / "[x]"
/// show as a checkbox you can click. The saved text stays plain ("- item", "[x] done").
/// </summary>
public sealed class ListMarkerGenerator : VisualLineElementGenerator
{
    private readonly Func<string, ScriptLine> _parse;
    private readonly Func<ScriptLine, double> _fontSize;
    private readonly Func<Brush> _foreground;
    private readonly Action<DocumentLine> _toggleCheckbox;

    private DocumentLine? _line;
    private ScriptLine? _parsed;

    /// <param name="parse">Parses a line of text.</param>
    /// <param name="fontSize">The line's font size in pixels, so checkboxes match the text.</param>
    /// <param name="foreground">The checkbox color.</param>
    /// <param name="toggleCheckbox">Called when a checkbox is clicked.</param>
    public ListMarkerGenerator(Func<string, ScriptLine> parse, Func<ScriptLine, double> fontSize, Func<Brush> foreground,
        Action<DocumentLine> toggleCheckbox)
    {
        _parse = parse;
        _fontSize = fontSize;
        _foreground = foreground;
        _toggleCheckbox = toggleCheckbox;
    }

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _line = context.VisualLine.FirstDocumentLine;
        _parsed = _parse(context.Document.GetText(_line));
    }

    public override void FinishGeneration()
    {
        base.FinishGeneration();
        _line = null;
        _parsed = null;
    }

    /// <summary>The marker is the only thing this generator replaces, so report where it starts (or -1).</summary>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (_line == null || _parsed is not { Type: ElementType.ListItem } parsed || parsed.List == ListKind.Numbered)
            return -1;
        int marker = _line.Offset + parsed.ListMarker.Start;
        return marker >= startOffset ? marker : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        if (_line == null || _parsed is not { } parsed) return null!; // AvalonEdit treats null as "nothing here"

        if (parsed.List == ListKind.Bullet)
            return new FormattedTextElement("•", parsed.ListMarker.Length); // drawn in the line's own font

        if (parsed.List == ListKind.Checkbox)
        {
            var line = _line;
            var box = new TextBlock
            {
                Text = parsed.IsChecked ? "" : "", // checked / empty box
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = _fontSize(parsed) * 0.95,
                Foreground = _foreground(),
                Cursor = Cursors.Hand,
                ToolTip = parsed.IsChecked ? "Done — click to untick" : "Click to tick",
                Margin = new Thickness(0, 0, 1, 0),
            };
            box.MouseLeftButtonDown += (_, e) =>
            {
                _toggleCheckbox(line);
                e.Handled = true; // don't also start a text selection
            };
            return new InlineObjectElement(parsed.ListMarker.Length, box);
        }
        return null!;
    }
}
