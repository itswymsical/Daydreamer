using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Analysis;
using Daydreamer.Core.Editing;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Import;
using Daydreamer.Core.Models;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;

namespace Daydreamer.App;

// The writing tools: line types, alignment, lists, indents, clear formatting, and bringing text in
// from Google Docs / Word (pasting or importing). (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private readonly DispatcherTimer _infoBarTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private Action? _infoBarAction;

    private void SetUpWritingTools()
    {
        var view = Editor.TextArea.TextView;
        view.LineAlignmentProvider = AlignmentOf;
        view.WrapIndentProvider = WrapIndentOf;
        view.ElementGenerators.Add(new ListMarkerGenerator(
            _parser.ParseLine,
            line => _project == null ? Editor.FontSize : WpfStyles.PointsToPixels(StyleResolver.ResolveBody(_project.Styles, line, ThemeManager.Palette).FontSize),
            () => (Brush)FindResource("MutedTextBrush"),
            ToggleCheckbox));

        Editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;

        // Pasting from Google Docs or Word brings headings, lists and emphasis along (see OnPasting).
        DataObject.AddPastingHandler(Editor.TextArea, OnPasting);
        // Copying from Daydreamer gives plain text only, so pasting back in never goes through the HTML reader.
        DataObject.AddSettingDataHandler(Editor.TextArea, (_, e) =>
        {
            if (e.Format == DataFormats.Html) e.CancelCommand();
        });

        _infoBarTimer.Tick += (_, _) => HideInfo();
    }

    // ── Per-line layout (alignment, list indents) ──────────────────────────

    private TextAlignment AlignmentOf(DocumentLine line)
    {
        if (_project == null) return TextAlignment.Left;
        var parsed = _parser.ParseLine(Editor.TextArea.TextView.Document.GetText(line));
        return StyleResolver.ResolveBody(_project.Styles, parsed, ThemeManager.Palette).Alignment switch
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
        var parsed = _parser.ParseLine(Editor.TextArea.TextView.Document.GetText(line));
        return parsed.Type == ElementType.ListItem ? parsed.Body.Start : 0;
    }

    // ── The line type menu ─────────────────────────────────────────────────

    private void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || _document == null) return;

        var current = _parser.ParseLine(Editor.Document.GetText(Editor.Document.GetLineByOffset(Editor.CaretOffset))).Type;
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

        menu.IsOpen = true;
    }

    /// <summary>A menu entry that looks like the kind of line it stands for (like Docs' style menu).</summary>
    private TextBlock PreviewOf(ElementType type)
    {
        var block = new TextBlock { Text = StyleSheet.DisplayNameOf(type) };
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
        var before = LinesBefore(first.LineNumber).ToList();
        var converted = new List<string>();
        foreach (string line in LinesBetween(first, last))
        {
            var earlier = before.Concat(converted).ToList();
            var context = new LineContext(
                DocumentStats.NextPageNumber(earlier, _parser),
                DocumentStats.NextPanelNumber(earlier, _parser),
                speaker);
            converted.Add(LineConverter.ConvertTo(line, type, _parser, context));
        }
        ReplaceLines(first, last, converted);
    }

    // ── Alignment ──────────────────────────────────────────────────────────

    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (_formatTarget.Alignment is not { } current) return;
        var menu = new ContextMenu { PlacementTarget = AlignButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Alignment  ·  {_formatTarget.Label}", IsEnabled = false });
        foreach (var alignment in Enum.GetValues<TextAlign>())
        {
            var item = new MenuItem
            {
                Header = alignment.ToString(),
                Icon = new System.Windows.Shapes.Path { Style = (Style)FindResource("IconPath"), Data = (Geometry)FindResource("IconAlign" + alignment) },
                IsCheckable = true,
                IsChecked = alignment == current,
            };
            item.Click += (_, _) =>
            {
                _formatTarget.Alignment = alignment;
                Editor.Focus();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
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

    /// <summary>Enter continues a list (or ends it on an empty item); Tab / Shift+Tab nest list items.</summary>
    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_document == null || _completion != null || Editor.SelectionLength > 0) return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = ContinueList();
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift
                 && _parser.ParseLine(Editor.Document.GetText(Editor.Document.GetLineByOffset(Editor.CaretOffset))).Type == ElementType.ListItem)
        {
            IndentSelection(outdent: Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
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
            // Enter on an empty item ends the list: the marker goes, the (now empty) line stays.
            document.Replace(line.Offset, line.Length, LineConverter.LeadingWhitespace(text));
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

    // ── Bringing text in from Google Docs / Word ───────────────────────────

    /// <summary>
    /// Pasting: Google Docs, Word and web pages put HTML on the clipboard. Turn it into script text —
    /// headings become pages/panels (see the Styles tab), lists stay lists, bold and italic are kept.
    /// </summary>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_project == null || !e.DataObject.GetDataPresent(DataFormats.Html)) return;
        if (e.DataObject.GetData(DataFormats.Html) is not string html) return;

        ImportedDocument imported;
        try
        {
            imported = HtmlImporter.Read(html);
        }
        catch (Exception)
        {
            return; // anything odd: fall back to pasting the plain text
        }

        string text = ScriptImporter.ToScript(imported, _project.ImportHeadings, _parser);
        if (text.Length == 0) return;

        var plain = new DataObject();
        plain.SetText(text);
        e.DataObject = plain;
        Dispatcher.BeginInvoke(() => SuggestLineSpacing(imported.LineSpacing, "The pasted text"), DispatcherPriority.Background);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import a document",
            Filter = "Documents (*.docx, *.txt, *.md)|*.docx;*.txt;*.md|Word or Google Docs files (*.docx)|*.docx|Text files (*.txt, *.md)|*.txt;*.md",
        };
        if (dialog.ShowDialog(this) != true) return;

        string text;
        double? spacing = null;
        try
        {
            // Opened for reading only, and allowed while Word still has the file open.
            using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (System.IO.Path.GetExtension(dialog.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                var imported = DocxImporter.Read(stream);
                text = ScriptImporter.ToScript(imported, _project.ImportHeadings, _parser);
                spacing = imported.LineSpacing;
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
        var document = _library.CreateDocument(_project, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), text);
        RefreshDocuments(document.Title);
        ShowWriting(); // if this came from the Home page
        SuggestLineSpacing(spacing, "This document");
    }

    /// <summary>If brought-in text used different line spacing, offer to use it for Default text.</summary>
    private void SuggestLineSpacing(double? spacing, string what)
    {
        if (_project == null || spacing is not { } wanted) return;
        double current = StyleResolver.ResolveBase(_project.Styles, ThemeManager.Palette).LineSpacing;
        if (Math.Abs(current - wanted) < 0.01) return;

        string name = wanted switch { 1 => "single", 2 => "double", _ => wanted.ToString("0.##", CultureInfo.CurrentCulture) };
        var project = _project;
        ShowInfo($"{what} used {name} line spacing. Use it for all your text?", "Use it",
            () => project.Styles.Base.LineSpacing = wanted);
    }

    // ── The suggestion bar ─────────────────────────────────────────────────

    private void ShowInfo(string message, string actionText, Action action)
    {
        InfoBarText.Text = message;
        InfoBarAction.Content = actionText;
        _infoBarAction = action;
        InfoBar.Visibility = Visibility.Visible;
        _infoBarTimer.Stop();
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

    /// <summary>Swaps the given lines for new ones as a single undo step.</summary>
    private void ReplaceLines(DocumentLine first, DocumentLine last, IReadOnlyList<string> lines)
    {
        var document = Editor.Document;
        bool hadSelection = Editor.SelectionLength > 0;
        string newline = TextUtilities.GetNewLineFromDocument(document, first.LineNumber);
        string replacement = string.Join(newline, lines);
        int start = first.Offset;
        document.Replace(start, last.EndOffset - start, replacement);

        if (hadSelection) Editor.Select(start, replacement.Length);
        else Editor.CaretOffset = start + (lines.Count > 0 ? lines[0].Length : 0);
        Editor.Focus();
    }
}
