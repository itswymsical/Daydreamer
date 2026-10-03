using LogicalDirection = System.Windows.Documents.LogicalDirection;
using System.Windows.Media.TextFormatting;
using Daydreamer.Core.Parsing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Daydreamer.App.Editor;

/// <summary>
/// Live preview: hides the characters that only say how to format a line, so the page reads like the
/// finished script. A line's {tag} (alignment, or a type picked from the menu) is always hidden — it
/// never needs typing. The "# ", "!" and ** you type yourself are hidden too, except on the line you're
/// editing, where they show faintly so you can change them. The text itself is never changed.
/// </summary>
public sealed class MarkupHider : VisualLineElementGenerator
{
    private readonly Func<string, ScriptLine> _parse;
    private readonly Func<int> _editingLine;

    private int _lineOffset;
    private int _lineLength;
    private IReadOnlyList<TextRange> _hidden = [];

    /// <param name="parse">Parses a line of text.</param>
    /// <param name="editingLine">The number of the line being edited (its typed markup stays visible), or 0 for none.</param>
    public MarkupHider(Func<string, ScriptLine> parse, Func<int> editingLine)
    {
        _parse = parse;
        _editingLine = editingLine;
    }

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        var line = context.VisualLine.FirstDocumentLine;
        var parsed = _parse(context.Document.GetText(line));
        _lineOffset = line.Offset;
        _lineLength = line.Length;
        _hidden = line.LineNumber == _editingLine()
            ? (parsed.Tag.IsEmpty ? [] : [parsed.Tag])
            : parsed.Markup();
    }

    public override void FinishGeneration()
    {
        base.FinishGeneration();
        _hidden = [];
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        foreach (var range in _hidden)
        {
            int start = _lineOffset + range.Start;
            if (start >= startOffset) return start;
        }
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        foreach (var range in _hidden)
        {
            if (_lineOffset + range.Start == offset)
                return new HiddenElement(range.Length, atLineStart: range.Start == 0 && range.End < _lineLength);
        }
        return null!; // AvalonEdit treats null as "nothing here"
    }

    /// <summary>
    /// Takes up its characters in the document but draws nothing and offers no place for the caret, so
    /// arrow keys and Backspace step straight over it. (Backspace at the start of a tagged line joins it
    /// to the line above, taking the tag with it — the same as joining two paragraphs in a word processor.)
    /// </summary>
    private sealed class HiddenElement(int documentLength, bool atLineStart) : VisualLineElement(1, documentLength)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new TextHidden(1);

        public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode) => -1;

        // At the start of a line (with words after it), there's no caret stop in front of it either. A line that is
        // nothing but hidden markup keeps its stops, or the caret would have nowhere to go on it.
        public override bool HandlesLineBorders => atLineStart;
    }
}
