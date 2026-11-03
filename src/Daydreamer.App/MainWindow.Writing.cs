using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.App.Controls;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Analysis;
using Daydreamer.Core.Editing;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Import;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Storage;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;

namespace Daydreamer.App;

// The writing tools: line types, alignment, lists, indents, clear formatting, hidden markup, and bringing
// text in from Google Docs / Word (pasting or importing). (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private readonly DispatcherTimer _infoBarTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private Action? _infoBarAction;

    // The line whose typed markup (# , **) is showing, and where the caret was a moment ago.
    private int _markupLine;
    private int _lastCaretOffset;

    private void SetUpWritingTools()
    {
        var view = Editor.TextArea.TextView;
        view.LineAlignmentProvider = AlignmentOf;
        view.WrapIndentProvider = WrapIndentOf;
        view.SpaceAboveProvider = SpaceAboveOf;
        view.ElementGenerators.Add(new MarkupHider(_parser.ParseLine, () => _markupLine));
        view.ElementGenerators.Add(new ListMarkerGenerator(
            _parser.ParseLine,
            line => _project == null ? Editor.FontSize : WpfStyles.PointsToPixels(StyleResolver.ResolveBody(_project.Styles, line, ThemeManager.Palette).FontSize),
            () => (Brush)FindResource("MutedTextBrush"),
            ToggleCheckbox));

        Editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
        Editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretMoved();
        // Markup shows only while you're editing a line; click away and the page reads clean.
        Editor.TextArea.IsKeyboardFocusWithinChanged += (_, _) => ShowMarkupOn(EditingLineNumber());

        // Pasting from Google Docs or Word brings headings, lists and emphasis along (see OnPasting).
        DataObject.AddPastingHandler(Editor.TextArea, OnPasting);
        DataObject.AddCopyingHandler(Editor.TextArea, OnCopying);
        // Copying from Daydreamer gives plain text only, so pasting back in never goes through the HTML reader.
        DataObject.AddSettingDataHandler(Editor.TextArea, (_, e) =>
        {
            if (e.Format == DataFormats.Html) e.CancelCommand();
        });

        _infoBarTimer.Tick += (_, _) => HideInfo();
    }

    /// <summary>The name people see for a kind of line, using the project's keywords ("Scene" if panels are SCENE).</summary>
    private string NameOf(ElementType type) => _project?.Keywords.NameOf(type) ?? StyleSheet.DisplayNameOf(type);

    // ── Per-line layout (alignment, list indents, paragraph spacing) ───────

    private ScriptLine ParseDocumentLine(DocumentLine line) => _parser.ParseLine(Editor.TextArea.TextView.Document.GetText(line));

    private TextAlignment AlignmentOf(DocumentLine line)
    {
        if (_project == null) return TextAlignment.Left;
        return StyleResolver.ResolveBody(_project.Styles, ParseDocumentLine(line), ThemeManager.Palette).Alignment switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            TextAlign.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
    }

    /// <summary>Wrapped rows of a list item line up with its text, not its bullet.</summary>
    private int WrapIndentOf(DocumentLine line)
    {
        var parsed = ParseDocumentLine(line);
        return parsed.Type == ElementType.ListItem ? parsed.Body.Start : 0;
    }

    /// <summary>The space above each paragraph (none above the very first line).</summary>
    private double SpaceAboveOf(DocumentLine line)
    {
        if (_project == null || line.LineNumber == 1) return 0;
        return WpfStyles.PointsToPixels(StyleResolver.ResolveBody(_project.Styles, ParseDocumentLine(line), ThemeManager.Palette).SpaceBefore);
    }

    // ── Hidden markup: the caret and the keys around a line's tag ──────────

    private void OnCaretMoved()
    {
        if (KeepCaretOutOfTag()) return; // moving the caret raises this again, and that call does the rest
        ShowMarkupOn(EditingLineNumber());
        UpdateCaretInfo();
    }

    private int EditingLineNumber() => Editor.TextArea.IsKeyboardFocusWithin && _document != null ? Editor.TextArea.Caret.Line : 0;

    /// <summary>Redraws the lines whose typed markup should appear or disappear.</summary>
    private void ShowMarkupOn(int lineNumber)
    {
        if (lineNumber == _markupLine) return;
        int previous = _markupLine;
        _markupLine = lineNumber;
        foreach (int n in new[] { previous, lineNumber })
        {
            if (n >= 1 && n <= Editor.Document.LineCount)
                Editor.TextArea.TextView.Redraw(Editor.Document.GetLineByNumber(n));
        }
    }

    /// <summary>
    /// The caret never sits inside (or in front of) a line's hidden tag — typing there would break it.
    /// Stepping left from the start of the words goes on to the line above, as it looks like it should.
    /// Returns true if the caret had to move.
    /// </summary>
    private bool KeepCaretOutOfTag()
    {
        var caret = Editor.TextArea.Caret;
        if (_document == null)
        {
            _lastCaretOffset = caret.Offset;
            return false;
        }

        var line = Editor.Document.GetLineByOffset(caret.Offset);
        var tag = ParseDocumentLine(line).Tag;
        int wordsStart = line.Offset + tag.End;
        int last = _lastCaretOffset;
        _lastCaretOffset = caret.Offset;
        if (tag.IsEmpty || caret.Offset >= wordsStart) return false;

        bool steppingBack = last == wordsStart && line.PreviousLine != null;
        caret.Offset = steppingBack ? line.PreviousLine!.EndOffset : wordsStart;
        return true;
    }

    /// <summary>Enter, Backspace, Delete and Home where a line has a hidden tag.</summary>
    private bool HandleTagKeys(Key key)
    {
        var document = Editor.Document;
        int caret = Editor.CaretOffset;
        var line = document.GetLineByOffset(caret);
        var parsed = ParseDocumentLine(line);
        int wordsStart = line.Offset + parsed.Tag.End;

        switch (key)
        {
            case Key.Enter when !parsed.Tag.IsEmpty:
            {
                string newline = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
                if (caret == wordsStart && parsed.Type != ElementType.Blank)
                {
                    // At the start of the words: open an empty line above, and this line moves down whole.
                    document.Insert(line.Offset, newline);
                }
                else
                {
                    // The new line keeps the alignment and style (like the next paragraph in a word processor) but not the type.
                    string carry = ScriptParser.JoinTag(_parser.SplitTag(document.GetText(line)).Indent, null, parsed.Alignment, "", parsed.StyleName);
                    document.Insert(caret, newline + carry);
                }
                Editor.TextArea.Caret.BringCaretToView();
                return true;
            }

            case Key.Back when !parsed.Tag.IsEmpty && caret == wordsStart:
                if (line.PreviousLine is not { } previous)
                {
                    document.Remove(line.Offset + parsed.Tag.Start, parsed.Tag.Length); // first line: just drop the tag
                    return true;
                }
                if (string.IsNullOrWhiteSpace(document.GetText(previous)))
                {
                    document.Remove(previous.Offset, line.Offset - previous.Offset); // the empty line above goes; this keeps its type
                    return true;
                }
                return false; // the normal Backspace joins this line onto the one above, tag and all

            case Key.Delete when caret == line.EndOffset && line.NextLine is { } next:
            {
                var nextTag = ParseDocumentLine(next).Tag;
                if (nextTag.IsEmpty) return false;
                if (string.IsNullOrWhiteSpace(document.GetText(line)))
                    document.Remove(line.Offset, next.Offset - line.Offset); // this empty line goes; the next keeps its type
                else
                    document.Remove(line.EndOffset, next.Offset + nextTag.End - line.EndOffset); // join, without the next line's tag
                return true;
            }

            case Key.Home when !parsed.Tag.IsEmpty:
            {
                var textArea = Editor.TextArea;
                var from = textArea.Caret.Position;
                textArea.Caret.Offset = wordsStart;
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    textArea.Selection = textArea.Selection.StartSelectionOrSetEndpoint(from, textArea.Caret.Position);
                else
                    textArea.ClearSelection();
                textArea.Caret.BringCaretToView();
                return true;
            }
        }
        return false;
    }

    // ── The line type menu ─────────────────────────────────────────────────

    private void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || _document == null) return;

        var currentLine = _parser.ParseLine(Editor.Document.GetText(Editor.Document.GetLineByOffset(Editor.CaretOffset)));
        var current = currentLine.Type;
        var menu = new ContextMenu { PlacementTarget = TypeButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Change the selected lines to…", IsEnabled = false });

        foreach (var type in new[] { ElementType.Action, ElementType.Heading, ElementType.Page, ElementType.Panel,
                                     ElementType.Caption, ElementType.Sfx, ElementType.Note })
        {
            var item = new MenuItem { Header = PreviewOf(type), IsCheckable = true, IsChecked = type == current };
            item.Click += (_, _) => ConvertSelection(type, speaker: null);
            menu.Items.Add(item);
        }

        // Dialogue needs a speaker: pick one of the characters (or add one).
        // (Not shown as checked: the Windows 11 menu style clips a checked item that also has a submenu.)
        var dialogue = new MenuItem { Header = PreviewOf(ElementType.Dialogue) };
        foreach (var character in _project.Characters.Where(c => c.Name.Length > 0).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var who = new MenuItem { Header = character.Name };
            who.Click += (_, _) => ConvertSelection(ElementType.Dialogue, character.Name);
            dialogue.Items.Add(who);
        }
        if (dialogue.Items.Count > 0) dialogue.Items.Add(new Separator());
        var newSpeaker = new MenuItem { Header = "New character…" };
        newSpeaker.Click += (_, _) =>
        {
            string? name = PromptDialog.Ask(this, "New character", "Who's speaking?", "", "Add");
            if (name == null) return;
            if (!_project.Characters.Any(c => c.AllNames().Contains(name, StringComparer.OrdinalIgnoreCase)))
                _project.Characters.Add(new Character { Name = name });
            ConvertSelection(ElementType.Dialogue, name);
        };
        dialogue.Items.Add(newSpeaker);
        menu.Items.Insert(2, dialogue);

        // Styles you made yourself go on top of whatever kind of line this is.
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Your styles", IsEnabled = false });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in _project.StylesFor(_document.Title).Where(s => seen.Add(s.Name)))
        {
            bool isCurrent = currentLine.CustomStyle == style;
            var item = new MenuItem { Header = PreviewOf(style), IsCheckable = true, IsChecked = isCurrent };
            System.Windows.Automation.AutomationProperties.SetName(item, style.Name);
            item.Click += (_, _) => ApplyCustomStyle(isCurrent ? null : style.Name); // picking the one it's in takes it out
            menu.Items.Add(item);
        }
        foreach (var item in NewStyleItems(applyToSelection: true)) menu.Items.Add(item);

        menu.IsOpen = true;
    }

    /// <summary>A menu entry in the style it stands for.</summary>
    private TextBlock PreviewOf(NamedStyle style)
    {
        var look = StyleResolver.Merge(StyleResolver.Start(ThemeManager.Palette.Text), _project!.Styles.Base, style.Style);
        return new TextBlock
        {
            Text = style.Name,
            FontFamily = WpfStyles.Family(look.FontFamily, 1),
            FontWeight = look.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = look.Italic ? FontStyles.Italic : FontStyles.Normal,
            FontSize = Math.Clamp(WpfStyles.PointsToPixels(look.FontSize), 13, 18),
        };
    }

    /// <summary>A menu entry that looks like the kind of line it stands for (like Docs' style menu).</summary>
    private TextBlock PreviewOf(ElementType type)
    {
        var block = new TextBlock { Text = NameOf(type) };
        if (_styleEntries.FirstOrDefault(s => s.Type == type) is { } entry)
        {
            block.FontFamily = entry.PreviewFont;
            block.FontWeight = entry.PreviewWeight;
            block.FontStyle = entry.PreviewStyle;
            block.FontSize = Math.Clamp(entry.PreviewSize, 13, 18);
        }
        return block;
    }

    /// <summary>Rewrites every line in the selection so it reads as <paramref name="type"/>.</summary>
    private void ConvertSelection(ElementType type, string? speaker)
    {
        var (first, last) = SelectedLines();
        string separator = SpeakerSeparator();
        // Without a colon, a speaker is only recognised when their name is written in capitals.
        if (separator == " ") speaker = speaker?.ToUpper(CultureInfo.CurrentCulture);
        var context = new LineContext(speaker, separator);
        ReplaceLines(first, last, LinesBetween(first, last).Select(line => LineConverter.ConvertTo(line, type, _parser, context)).ToList());
    }

    /// <summary>
    /// How this script writes dialogue: "AKIRA: Hi" (with a colon) or "AKIRA Hi" (without). Whichever
    /// most of its dialogue lines use; a colon if there's no dialogue yet.
    /// </summary>
    private string SpeakerSeparator()
    {
        int withColon = 0, without = 0;
        foreach (var line in Editor.Document.Lines)
        {
            string text = Editor.Document.GetText(line);
            var parsed = _parser.ParseLine(text);
            if (parsed.Type != ElementType.Dialogue || parsed.Label.IsEmpty) continue;
            if (parsed.Label.Of(text).EndsWith(':')) withColon++;
            else without++;
        }
        return without > withColon ? " " : ": ";
    }

    // ── Alignment (each line has its own, like a word processor) ───────────

    /// <summary>The alignment a line actually has: its own, or else its style's.</summary>
    private TextAlign AlignmentAt(DocumentLine line) =>
        _project == null ? TextAlign.Left : StyleResolver.ResolveBody(_project.Styles, ParseDocumentLine(line), ThemeManager.Palette).Alignment;

    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || _document == null) return;
        var (first, last) = SelectedLines();
        var current = AlignmentAt(Editor.Document.GetLineByOffset(Editor.CaretOffset));
        int count = last.LineNumber - first.LineNumber + 1;

        var menu = new ContextMenu { PlacementTarget = AlignButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = count == 1 ? "Align this line" : $"Align {count} lines", IsEnabled = false });
        foreach (var alignment in Enum.GetValues<TextAlign>())
        {
            var item = new MenuItem
            {
                Header = alignment.ToString(),
                Icon = new System.Windows.Shapes.Path { Style = (Style)FindResource("IconPath"), Data = (Geometry)FindResource("IconAlign" + alignment) },
                InputGestureText = "Ctrl+Shift+" + AlignmentKey(alignment),
                IsCheckable = true,
                IsChecked = alignment == current,
            };
            item.Click += (_, _) => AlignSelection(alignment);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>The Google Docs shortcuts: Ctrl+Shift+L, E, R and J.</summary>
    private static Key AlignmentKey(TextAlign alignment) => alignment switch
    {
        TextAlign.Center => Key.E,
        TextAlign.Right => Key.R,
        TextAlign.Justify => Key.J,
        _ => Key.L,
    };

    /// <summary>
    /// Aligns the caret's line, or every line in the selection. Each line remembers it in its hidden tag;
    /// a line set back to its style's own alignment loses the tag again.
    /// </summary>
    private void AlignSelection(TextAlign alignment)
    {
        if (_project == null || _document == null) return;
        var (first, last) = SelectedLines();
        bool oneLine = first.LineNumber == last.LineNumber;

        var lines = LinesBetween(first, last).Select(text =>
        {
            var parsed = _parser.ParseLine(text);
            if (parsed.Type == ElementType.Blank && !oneLine && parsed.Tag.IsEmpty) return text; // gaps between paragraphs stay as they are
            var natural = StyleResolver.ResolveBody(_project.Styles, parsed with { Alignment = null }, ThemeManager.Palette).Alignment;
            return LineEditing.SetLineAlignment(text, alignment == natural ? null : alignment, _parser);
        }).ToList();
        ReplaceLines(first, last, lines);
        UpdateAlignmentIcon();
    }

    /// <summary>The toolbar's alignment button shows the caret line's alignment.</summary>
    private void UpdateAlignmentIcon()
    {
        var alignment = _document == null ? TextAlign.Left : AlignmentAt(Editor.Document.GetLineByOffset(Editor.CaretOffset));
        AlignIcon.Data = (Geometry)FindResource("IconAlign" + alignment);
        AlignButton.IsEnabled = _document != null;
    }

    // ── Lists, indents, clear formatting ───────────────────────────────────

    private void BulletList_Click(object sender, RoutedEventArgs e) => ToggleList(ListKind.Bullet);
    private void NumberedList_Click(object sender, RoutedEventArgs e) => ToggleList(ListKind.Numbered);
    private void Checklist_Click(object sender, RoutedEventArgs e) => ToggleList(ListKind.Checkbox);
    private void Indent_Click(object sender, RoutedEventArgs e) => IndentSelection(outdent: false);
    private void Outdent_Click(object sender, RoutedEventArgs e) => IndentSelection(outdent: true);

    private void ToggleList(ListKind kind)
    {
        if (_document == null) return;
        var (first, last) = SelectedLines();
        ReplaceLines(first, last, LineEditing.ToggleList(LinesBetween(first, last), kind, _parser));
        RenumberAround(first.LineNumber);
    }

    /// <summary>Adds or removes one tab at the start of each selected line. Inserting (rather than replacing) keeps the caret put.</summary>
    private void IndentSelection(bool outdent)
    {
        if (_document == null) return;
        var (first, last) = SelectedLines();
        var document = Editor.Document;
        using (document.RunUpdate())
        {
            for (var line = first; line != null && line.LineNumber <= last.LineNumber; line = line.NextLine)
            {
                string text = document.GetText(line);
                if (outdent)
                {
                    int remove = text.Length - LineEditing.Outdent(text).Length;
                    if (remove > 0) document.Remove(line.Offset, remove);
                }
                else if (text.Length > 0)
                {
                    document.Insert(line.Offset, LineEditing.IndentUnit);
                }
            }
            RenumberAround(first.LineNumber);
        }
        Editor.Focus();
    }

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        var (first, last) = SelectedLines();
        ReplaceLines(first, last, LinesBetween(first, last).Select(LineEditing.ClearEmphasis).ToList());
    }

    private void ToggleCheckbox(DocumentLine line)
    {
        if (line.IsDeleted) return;
        var parsed = _parser.ParseLine(Editor.Document.GetText(line));
        if (parsed.List != ListKind.Checkbox) return;
        Editor.Document.Replace(line.Offset + parsed.ListMarker.Start, parsed.ListMarker.Length, parsed.IsChecked ? "[ ]" : "[x]");
    }

    /// <summary>Lists, tags and line keys: Enter, Tab / Shift+Tab, Backspace, Delete and Home.</summary>
    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_document == null || _completion != null || Editor.SelectionLength > 0) return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = ContinueList() || HandleTagKeys(Key.Enter);
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift
                 && _parser.ParseLine(Editor.Document.GetText(Editor.Document.GetLineByOffset(Editor.CaretOffset))).Type == ElementType.ListItem)
        {
            IndentSelection(outdent: Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
        }
        else if (e.Key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None
                 || e.Key == Key.Home && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            e.Handled = HandleTagKeys(e.Key);
        }
    }

    private bool ContinueList()
    {
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string text = document.GetText(line);
        var parsed = _parser.ParseLine(text);
        if (parsed.Type != ElementType.ListItem || Editor.CaretOffset - line.Offset < parsed.Body.Start)
            return false; // not a list, or the caret is before the item's text: a normal Enter

        var enter = LineEditing.OnEnter(text, _parser);
        if (enter.EndList)
        {
            // Enter on an empty item ends the list: the marker goes, the (now empty) line stays — with its alignment.
            document.Replace(line.Offset, line.Length, ScriptParser.JoinTag(_parser.SplitTag(text).Indent, null, parsed.Alignment, "", parsed.StyleName));
            return true;
        }

        string newline = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        using (document.RunUpdate())
        {
            document.Insert(Editor.CaretOffset, newline + enter.NextPrefix);
            RenumberAround(line.LineNumber + 1);
        }

        // Put the caret just after the new item's marker.
        var next = line.NextLine!;
        Editor.CaretOffset = next.Offset + _parser.ParseLine(document.GetText(next)).Body.Start;
        Editor.TextArea.Caret.BringCaretToView();
        return true;
    }

    /// <summary>Makes the numbered list around a line count 1, 2, 3 again.</summary>
    private void RenumberAround(int lineNumber)
    {
        var document = Editor.Document;
        var lines = document.Lines.Select(l => document.GetText(l)).ToList();
        var changes = LineEditing.Renumber(lines, lineNumber - 1, _parser);
        if (changes.Count == 0) return;
        using (document.RunUpdate())
        {
            foreach (var (index, text) in changes)
            {
                var line = document.GetLineByNumber(index + 1);
                document.Replace(line.Offset, line.Length, text);
            }
        }
    }

    // ── Copying and pasting ────────────────────────────────────────────────

    /// <summary>
    /// Other apps get the words without Daydreamer's hidden line tags. Daydreamer remembers what it
    /// copied, so pasting that same text back in keeps the tags.
    /// </summary>
    private void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        _lastCopy = null;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text) return;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool carriageReturn = lines[i].EndsWith('\r');
            var (indent, _, _, _, rest) = _parser.SplitTag(carriageReturn ? lines[i][..^1] : lines[i]);
            lines[i] = indent + rest + (carriageReturn ? "\r" : "");
        }
        string plain = string.Join("\n", lines);
        if (plain == text) return;

        _lastCopy = (plain, text);
        e.DataObject.SetData(DataFormats.UnicodeText, plain);
        e.DataObject.SetData(DataFormats.Text, plain);
    }

    // The last copy that had tags in it: what other apps got, and the full text.
    private (string Plain, string Script)? _lastCopy;

    /// <summary>
    /// Pasting: Google Docs, Word and web pages put HTML on the clipboard. Turn it into script text —
    /// headings become pages/panels (see the Styles tab), lists stay lists, bold and italic are kept.
    /// </summary>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_project == null) return;

        // Copied from Daydreamer: paste it exactly as it was, tags and all.
        if (_lastCopy is { } copy && e.DataObject.GetData(DataFormats.UnicodeText) is string pasted && pasted == copy.Plain)
        {
            var own = new DataObject();
            own.SetText(copy.Script);
            e.DataObject = own;
            return;
        }

        if (!e.DataObject.GetDataPresent(DataFormats.Html) || e.DataObject.GetData(DataFormats.Html) is not string html) return;

        ImportedDocument imported;
        try
        {
            imported = HtmlImporter.Read(html);
        }
        catch (Exception)
        {
            return; // anything odd: fall back to pasting the plain text
        }

        var normal = StyleResolver.ResolveBase(_project.Styles, ThemeManager.Palette).Alignment;
        string text = ScriptImporter.ToScript(imported, _project.ImportHeadings, _parser, normal);
        if (text.Length == 0) return;

        var plain = new DataObject();
        plain.SetText(text);
        e.DataObject = plain;
        Dispatcher.BeginInvoke(() => SuggestLook(imported), DispatcherPriority.Background);
    }

    // ── Importing a whole document ─────────────────────────────────────────

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import a document",
            Filter = "Documents (*.docx, *.txt, *.md, *.hjson)|*.docx;*.txt;*.md;*.hjson|Word or Google Docs files (*.docx)|*.docx|Text files (*.txt, *.md)|*.txt;*.md|Game dialogue (*.hjson)|*.hjson",
        };
        if (dialog.ShowDialog(this) != true) return;

        if (Path.GetExtension(dialog.FileName).Equals(".hjson", StringComparison.OrdinalIgnoreCase))
        {
            ImportGameDialogue(dialog.FileName);
            return;
        }

        string text;
        ImportedDocument? imported = null;
        List<EmbeddedFont> fonts = [];
        try
        {
            // Opened for reading only, and allowed while Word still has the file open.
            using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (Path.GetExtension(dialog.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                imported = DocxImporter.Read(stream);
                stream.Position = 0;
                fonts = DocxImporter.ReadEmbeddedFonts(stream);
                text = ScriptImporter.ToScript(imported, _project.ImportHeadings, _parser, imported.Alignment);
            }
            else
            {
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            PromptDialog.Inform(this, "Import", $"Couldn't read that file.\n\n{ex.Message}");
            return;
        }

        SaveDocument();
        var document = _library.CreateDocument(_project, Path.GetFileNameWithoutExtension(dialog.FileName), text);
        if (imported != null) AdoptLook(imported, fonts);
        RefreshDocuments(document.Title);
        ShowWriting(); // if this came from the Home page

        // Names that start lots of lines are probably speakers: show them, ready to add.
        UpdateStats();
        if (UnknownSpeakersList.Items.Count > 0) FormattingTabs.SelectedIndex = 0;
    }

    /// <summary>
    /// Makes the project look like the imported document: its font (saved into the project if it came
    /// packed in the file), size, line spacing, space between paragraphs, alignment, and its word for
    /// panels. A bar at the top offers to undo it all.
    /// </summary>
    private void AdoptLook(ImportedDocument imported, List<EmbeddedFont> fonts)
    {
        if (_project == null) return;
        var project = _project;
        var body = project.Styles.Base;
        var before = body.Clone();
        string panelsBefore = project.Keywords.Panel;
        var changes = new List<string>();

        if (fonts.Count > 0)
        {
            try
            {
                _library.SaveFonts(project, fonts.Select(f => (f.FileName, f.Data)));
                LoadProjectFonts();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not being able to keep the font only means it shows in a stand-in font.
            }
        }

        if (imported.FontFamily is { } font && !font.Equals(StyleResolver.ResolveBase(project.Styles, ThemeManager.Palette).FontFamily, StringComparison.OrdinalIgnoreCase))
        {
            body.FontFamily = font;
            changes.Add(font);
        }
        if (imported.FontSize is { } size && Math.Abs(size - StyleResolver.ResolveBase(project.Styles, ThemeManager.Palette).FontSize) > 0.01)
        {
            body.FontSize = size;
            changes.Add($"{size:0.#} pt");
        }
        if (imported.LineSpacing is { } spacing && Math.Abs(spacing - StyleResolver.ResolveBase(project.Styles, ThemeManager.Palette).LineSpacing) > 0.01)
        {
            body.LineSpacing = spacing;
            changes.Add(spacing switch { 1 => "single spacing", 2 => "double spacing", _ => $"{spacing:0.##} line spacing" });
        }
        if (imported.ParagraphSpacing is { } gap && Math.Abs(gap - StyleResolver.ResolveBase(project.Styles, ThemeManager.Palette).SpaceBefore) > 0.01)
        {
            body.SpaceBefore = gap;
            changes.Add(gap == 0 ? "no space between paragraphs" : $"{gap:0.#} pt between paragraphs");
        }
        if (imported.Alignment != StyleResolver.ResolveBase(project.Styles, ThemeManager.Palette).Alignment)
        {
            body.Alignment = imported.Alignment;
            changes.Add(imported.Alignment.ToString().ToLowerInvariant() + " aligned");
        }
        if (ScriptImporter.SuggestKeyword(imported, project.ImportHeadings, ElementType.Panel) is { } panelWord && panelWord != project.Keywords.Panel)
        {
            project.Keywords.Panel = panelWord;
            changes.Add($"panels called {panelWord}");
        }
        if (changes.Count == 0) return;

        ShowInfo($"Using this document's look: {string.Join(", ", changes)}.", "Undo", () =>
        {
            body.FontFamily = before.FontFamily;
            body.FontSize = before.FontSize;
            body.LineSpacing = before.LineSpacing;
            body.SpaceBefore = before.SpaceBefore;
            body.Alignment = before.Alignment;
            project.Keywords.Panel = panelsBefore;
        });
    }

    /// <summary>When pasted text looks different from yours, offer to use its font, size and spacing.</summary>
    private void SuggestLook(ImportedDocument imported)
    {
        if (_project == null) return;
        var current = StyleResolver.ResolveBase(_project.Styles, ThemeManager.Palette);
        var body = _project.Styles.Base;
        var apply = new List<Action>();
        var changes = new List<string>();

        if (imported.FontFamily is { } font && FontList.Names.Contains(font) && !font.Equals(current.FontFamily, StringComparison.OrdinalIgnoreCase))
        {
            apply.Add(() => body.FontFamily = font);
            changes.Add(font);
        }
        if (imported.FontSize is { } size && Math.Abs(size - current.FontSize) > 0.01)
        {
            apply.Add(() => body.FontSize = size);
            changes.Add($"{size:0.#} pt");
        }
        if (imported.LineSpacing is { } spacing && Math.Abs(spacing - current.LineSpacing) > 0.01)
        {
            apply.Add(() => body.LineSpacing = spacing);
            changes.Add(spacing switch { 1 => "single spacing", 2 => "double spacing", _ => $"{spacing:0.##} line spacing" });
        }
        if (imported.ParagraphSpacing is { } gap && Math.Abs(gap - current.SpaceBefore) > 0.01)
        {
            apply.Add(() => body.SpaceBefore = gap);
            changes.Add(gap == 0 ? "no space between paragraphs" : $"{gap:0.#} pt between paragraphs");
        }
        if (changes.Count == 0) return;

        ShowInfo($"The pasted text used {string.Join(", ", changes)}. Use that for all your text?", "Use it",
            () => apply.ForEach(a => a()));
    }

    /// <summary>Makes the open project's own fonts (from imported documents) usable and pickable.</summary>
    private void LoadProjectFonts()
    {
        var names = WpfStyles.UseProjectFonts(_project != null ? Library.FontsFolder(_project) : null);
        FontList.SetProjectFonts(names);
    }

    // ── The suggestion bar ─────────────────────────────────────────────────

    /// <param name="showFor">How long it stays up before tidying itself away (20 seconds unless you say).</param>
    private void ShowInfo(string message, string actionText, Action action, TimeSpan? showFor = null)
    {
        InfoBarText.Text = message;
        InfoBarAction.Content = actionText;
        _infoBarAction = action;
        InfoBar.Visibility = Visibility.Visible;
        _infoBarTimer.Stop();
        _infoBarTimer.Interval = showFor ?? TimeSpan.FromSeconds(20);
        _infoBarTimer.Start();
    }

    private void HideInfo()
    {
        _infoBarTimer.Stop();
        InfoBar.Visibility = Visibility.Collapsed;
        _infoBarAction = null;
    }

    private void InfoBarAction_Click(object sender, RoutedEventArgs e)
    {
        _infoBarAction?.Invoke();
        HideInfo();
        Editor.Focus();
    }

    private void InfoBarClose_Click(object sender, RoutedEventArgs e) => HideInfo();

    // ── Selection helpers ──────────────────────────────────────────────────

    /// <summary>The lines the selection touches (or just the caret's line).</summary>
    private (DocumentLine First, DocumentLine Last) SelectedLines()
    {
        var document = Editor.Document;
        int start = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        int end = Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength : Editor.CaretOffset;
        var first = document.GetLineByOffset(start);
        var last = document.GetLineByOffset(end);
        // A selection that ends at the very start of a line doesn't really include that line.
        if (last.LineNumber > first.LineNumber && end == last.Offset) last = last.PreviousLine;
        return (first, last);
    }

    private List<string> LinesBetween(DocumentLine first, DocumentLine last)
    {
        var lines = new List<string>();
        for (var line = first; line != null && line.LineNumber <= last.LineNumber; line = line.NextLine)
            lines.Add(Editor.Document.GetText(line));
        return lines;
    }

    private IEnumerable<string> LinesBefore(int lineNumber)
    {
        for (int n = 1; n < lineNumber; n++)
            yield return Editor.Document.GetText(Editor.Document.GetLineByNumber(n));
    }

    /// <summary>
    /// Swaps the given lines for new ones as a single undo step. Only the part of each line that actually
    /// changes is replaced, so the caret and selection stay where they were (next to the same words).
    /// </summary>
    private void ReplaceLines(DocumentLine first, DocumentLine last, IReadOnlyList<string> lines)
    {
        var document = Editor.Document;
        if (lines.Count == last.LineNumber - first.LineNumber + 1)
        {
            using (document.RunUpdate())
            {
                var line = first;
                foreach (string text in lines)
                {
                    var next = line.NextLine;
                    ReplaceChangedPart(line, text);
                    line = next!;
                }
            }
            Editor.Focus();
            return;
        }

        bool hadSelection = Editor.SelectionLength > 0;
        string newline = TextUtilities.GetNewLineFromDocument(document, first.LineNumber);
        string replacement = string.Join(newline, lines);
        int start = first.Offset;
        document.Replace(start, last.EndOffset - start, replacement);

        if (hadSelection) Editor.Select(start, replacement.Length);
        else Editor.CaretOffset = start + (lines.Count > 0 ? lines[0].Length : 0);
        Editor.Focus();
    }

    /// <summary>Replaces just the middle of a line that differs ("Hello" → "{center} Hello" inserts the tag).</summary>
    private void ReplaceChangedPart(DocumentLine line, string text)
    {
        string old = Editor.Document.GetText(line);
        if (old == text) return;
        int prefix = 0;
        while (prefix < old.Length && prefix < text.Length && old[prefix] == text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < old.Length - prefix && suffix < text.Length - prefix && old[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        Editor.Document.Replace(line.Offset + prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
    }
}
