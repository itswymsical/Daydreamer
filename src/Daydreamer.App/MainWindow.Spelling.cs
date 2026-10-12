using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Daydreamer.App.Editor;
using Daydreamer.Core.Proofing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Daydreamer.App;

// Spell checking: wavy lines under misspelled words, fixes on right-click, and a count in the status bar
// that jumps to the next mistake. Windows' spell checker does the checking (Editor/WindowsSpellChecker.cs);
// ScriptSpellChecker points it at the words people read and away from names, SFX and notes.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private const int MaxSuggestions = 5;

    private ScriptSpellChecker? _spelling; // null if this PC has no spell checker
    private int _typingAt = -1;            // where the caret was left by typing a letter (that word isn't finished yet)

    private bool SpellingOn => _spelling != null && _settings.SpellCheck && _document != null;

    private void SetUpSpelling()
    {
        if (WindowsSpellChecker.TryCreate() is { } windows) _spelling = new ScriptSpellChecker(windows);

        Editor.TextArea.TextView.BackgroundRenderers.Add(new SpellingUnderliner(
            SpellingIssuesOf, () => (Brush)FindResource("SpellingBrush"), () => _typingAt));

        // A word is marked once you've finished it (typed a space or punctuation after it), not while it's half typed.
        Editor.TextArea.TextEntered += (_, e) =>
            _typingAt = e.Text.Length > 0 && char.IsLetter(e.Text[^1]) ? Editor.CaretOffset : -1;
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_typingAt != -1 && Editor.CaretOffset != _typingAt) _typingAt = -1;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        };
    }

    /// <summary>The spelling problems in a line of the open document.</summary>
    private IReadOnlyList<SpellingIssue> SpellingIssuesOf(DocumentLine line) =>
        SpellingOn ? _spelling!.CheckLine(Editor.Document.GetText(line), _parser.ParseLine) : [];

    /// <summary>The story's characters and own words are never mistakes. Called whenever the project's formatting changes.</summary>
    private void UpdateSpellingWords() =>
        _spelling?.SetKnownWords(_project?.Characters ?? [], _project?.KnownWords ?? []);

    /// <summary>"3 spelling mistakes" in the status bar (nothing when there are none).</summary>
    private void UpdateSpellingStatus()
    {
        int count = 0;
        if (SpellingOn)
            foreach (var line in Editor.Document.Lines) count += SpellingIssuesOf(line).Count;

        SpellingStatus.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpellingStatusText.Text = count == 1 ? "1 spelling mistake" : $"{count:N0} spelling mistakes";
    }

    /// <summary>Jumps to the next mistake after the cursor (round to the top again at the end).</summary>
    private void SpellingStatus_Click(object sender, RoutedEventArgs e)
    {
        if (!SpellingOn) return;
        var document = Editor.Document;
        int caret = Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength : Editor.CaretOffset;
        int startLine = document.GetLineByOffset(caret).LineNumber;

        for (int i = 0; i <= document.LineCount; i++)
        {
            var line = document.GetLineByNumber((startLine - 1 + i) % document.LineCount + 1);
            foreach (var issue in SpellingIssuesOf(line))
            {
                int start = line.Offset + issue.Range.Start;
                if (i == 0 && start < caret) continue; // earlier on the cursor's own line: that's for the next time round
                Editor.Select(start, issue.Range.Length);
                Editor.ScrollTo(line.LineNumber, issue.Range.Start + 1);
                Editor.Focus();
                return;
            }
        }
    }

    // ── The right-click menu ────────────────────────────────────────────────

    /// <summary>For a right-click on a marked word: fixes at the top of the menu, then Ignore and Add to dictionary.</summary>
    private void AddSpellingItems(ContextMenu menu, TextViewPosition? position)
    {
        if (position == null || !SpellingOn) return;
        var line = Editor.Document.GetLineByNumber(position.Value.Line);
        int column = position.Value.Column - 1; // columns count from 1
        var issue = SpellingIssuesOf(line).FirstOrDefault(i => column >= i.Range.Start && column <= i.Range.End);
        if (issue == null) return;

        int start = line.Offset + issue.Range.Start;
        string word = Editor.Document.GetText(start, issue.Range.Length);

        if (issue.Kind == SpellingIssueKind.RepeatedWord)
        {
            menu.Items.Add(MenuItem($"Delete the repeated “{word}”", () => DeleteRepeatedWord(start, issue.Range.Length), bold: true));
        }
        else
        {
            var suggestions = _spelling!.Suggest(word).Take(MaxSuggestions).ToList();
            foreach (string suggestion in suggestions)
                menu.Items.Add(MenuItem(suggestion, () => ReplaceWord(start, word, suggestion), bold: true));
            if (suggestions.Count == 0)
                menu.Items.Add(new MenuItem { Header = "No suggestions", IsEnabled = false });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem($"Ignore “{word}”", () => IgnoreWord(word), "Stop marking this word until Daydreamer closes."));
        if (issue.Kind == SpellingIssueKind.Misspelled && _project != null)
        {
            menu.Items.Add(MenuItem($"Add “{word}” to this story's dictionary", () => _project.KnownWords.Add(word),
                "For names of places, slang and other words this story uses. Saved with the project."));
        }
        menu.Items.Add(new Separator());
    }

    /// <summary>The on/off switch, at the bottom of the right-click menu.</summary>
    private MenuItem SpellingToggleItem()
    {
        var item = new MenuItem
        {
            Header = _spelling != null ? "Check spelling" : "Check spelling (Windows has no spell checker here)",
            IsCheckable = true,
            IsChecked = _spelling != null && _settings.SpellCheck,
            IsEnabled = _spelling != null,
        };
        // Checked/Unchecked rather than Click, so screen readers and other accessibility tools can switch it too.
        item.Checked += (_, _) => SetSpellCheck(true);
        item.Unchecked += (_, _) => SetSpellCheck(false);
        return item;
    }

    private void SetSpellCheck(bool on)
    {
        _settings.SpellCheck = on;
        SaveSettings();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        UpdateSpellingStatus();
    }

    private void ReplaceWord(int start, string word, string replacement)
    {
        // Only if the word is still there (the menu could have been open while autosave or an import changed things).
        if (start + word.Length <= Editor.Document.TextLength && Editor.Document.GetText(start, word.Length) == word)
            Editor.Document.Replace(start, word.Length, replacement);
        UpdateSpellingStatus();
    }

    /// <summary>"the the" → "the": removes the second one and the space in front of it.</summary>
    private void DeleteRepeatedWord(int start, int length)
    {
        int from = start;
        while (from > 0 && Editor.Document.GetCharAt(from - 1) == ' ') from--;
        Editor.Document.Remove(from, start + length - from);
        UpdateSpellingStatus();
    }

    private void IgnoreWord(string word)
    {
        _spelling?.Ignore(word);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        UpdateSpellingStatus();
    }
}
