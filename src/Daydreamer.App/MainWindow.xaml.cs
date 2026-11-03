using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;
using Daydreamer.Core.Analysis;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Storage;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Search;

namespace Daydreamer.App;

public partial class MainWindow : Window
{
    // The "paper" is 8.5 inches wide with 1-inch side margins, like a printed page.
    private const double PageWidth = 816;
    private const double PageMargin = 72;

    private readonly AppSettings _settings;
    private readonly bool _persistSettings = true;
    private readonly ScriptParser _parser = new();
    private readonly ScriptColorizer _colorizer;

    // Timers wait for a pause in typing before doing work, so the app stays snappy.
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _projectSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private Library _library = null!;
    private Project? _project;
    private DocumentInfo? _document;
    private bool _dirty;
    private bool _loadingDocument;
    private bool _suppressSelectionEvents;
    private CompletionWindow? _completion;
    private SearchPanel? _searchPanel;
    private List<StyleEntry> _styleEntries = [];

    /// <summary>What the toolbar's font/size/color controls are editing (see MainWindow.Formatting.cs).</summary>
    private readonly FormatTarget _formatTarget = new();

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        // `--library <folder>` opens a different library without touching your saved settings (handy for testing).
        string[] args = Environment.GetCommandLineArgs();
        int libraryArg = Array.IndexOf(args, "--library");
        if (libraryArg >= 0 && libraryArg + 1 < args.Length)
        {
            _settings = new AppSettings { LibraryPath = args[libraryArg + 1] };
            _persistSettings = false;
        }

        _colorizer = new ScriptColorizer(_parser);
        SetUpEditor();
        SetUpWritingTools();
        SetUpSpelling();
        SetUpShell();
        SetUpLayout();
        FormatControls.DataContext = _formatTarget;

        _autosaveTimer.Tick += (_, _) => SaveDocument();
        _statsTimer.Tick += (_, _) => UpdateStats();
        _projectSaveTimer.Tick += (_, _) => SaveProject();

        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        Loaded += (_, _) => StartUp();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Start-up
    // ═══════════════════════════════════════════════════════════════════════

    private void StartUp()
    {
        _library = new Library(_settings.LibraryPath);
        SetUpMusic(); // the music library lives in the library folder too

        // First launch: give the writer something to look at.
        if (_library.ListProjectFolders().Count == 0)
            SampleContent.CreateSampleProject(_library);

        // The last document is opened behind the scenes, so it's one click away on the Home page.
        RefreshProjects(_settings.LastProject, _settings.LastDocument);
        ShowHome();
    }

    private void SetUpEditor()
    {
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.AllowScrollBelowDocument = true;
        Editor.Options.HighlightCurrentLine = false;

        // Keep the formatting colors visible while text is selected.
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionCornerRadius = 2;

        _searchPanel = SearchPanel.Install(Editor); // Ctrl+F
        ApplyEditorTheme();

        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.TextEntered += Editor_TextEntered;
        Editor.ContextMenu = new ContextMenu();
        Editor.ContextMenuOpening += Editor_ContextMenuOpening;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Projects
    // ═══════════════════════════════════════════════════════════════════════

    private void RefreshProjects(string? selectName, string? documentTitle = null)
    {
        var names = _library.ListProjectFolders().Select(Path.GetFileName).OfType<string>().ToList();
        string? target = names.Contains(selectName ?? "") ? selectName : names.FirstOrDefault();

        _suppressSelectionEvents = true;
        ProjectPicker.ItemsSource = names;
        ProjectPicker.SelectedItem = target;
        _suppressSelectionEvents = false;

        if (target != null) OpenProject(target, documentTitle);
    }

    private void OpenProject(string name, string? documentTitle)
    {
        SaveAll();
        OpenDocument(null);

        if (_project != null)
        {
            _project.FormattingChanged -= Project_FormattingChanged;
            _project.Keywords.PropertyChanged -= Keywords_PropertyChanged;
        }

        _project = _library.LoadProject(Path.Combine(_library.RootPath, name));
        _project.FormattingChanged += Project_FormattingChanged;
        _project.Keywords.PropertyChanged += Keywords_PropertyChanged;
        ElementTypeNameConverter.Keywords = _project.Keywords;
        LoadProjectFonts();

        FormattingPanel.DataContext = _project;
        _styleEntries = StyleEntry.CreateFor(_project.Styles, _project.Keywords);
        StylesList.ItemsSource = _styleEntries;
        ProjectLabel.Text = _project.Name;
        UpdateGameFolderLabel();
        ApplyFormatting();

        _settings.LastProject = name;
        SaveSettings();
        RefreshDocuments(documentTitle);
    }

    private void ProjectPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || ProjectPicker.SelectedItem is not string name || name == _project?.Name) return;
        OpenProject(name, documentTitle: null);
    }

    private void NewProject_Click(object sender, RoutedEventArgs e) => CreateProject();

    /// <summary>Asks for a name, then creates the project with a first empty chapter. False if cancelled.</summary>
    private bool CreateProject()
    {
        string? name = PromptDialog.Ask(this, "New project", "What's the story or series called?", "", "Create");
        if (name == null) return false;

        SaveAll();
        var project = _library.CreateProject(name);
        _library.CreateDocument(project, "Chapter 1");
        RefreshProjects(project.Name);
        return true;
    }

    private void RenameProject_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        string? name = PromptDialog.Ask(this, "Rename project", "New name:", _project.Name, "Rename");
        if (name == null || name == _project.Name) return;

        SaveAll();
        string? reopen = _document?.Title;
        OpenDocument(null); // the folder is about to move, so let go of the open file first
        try
        {
            _library.RenameProject(_project, name);
        }
        catch (IOException ex)
        {
            PromptDialog.Inform(this, "Rename project", $"Couldn't rename the project folder.\n\n{ex.Message}");
        }
        RefreshProjects(_project.Name, reopen);
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_project != null)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_project.FolderPath}\"") { UseShellExecute = true });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Documents
    // ═══════════════════════════════════════════════════════════════════════

    private void RefreshDocuments(string? selectTitle)
    {
        if (_project == null) return;

        var documents = _library.ListDocuments(_project);
        var target = documents.FirstOrDefault(d => d.Title == selectTitle) ?? documents.FirstOrDefault();

        _suppressSelectionEvents = true;
        DocumentList.ItemsSource = documents;
        DocumentList.SelectedItem = target;
        _suppressSelectionEvents = false;

        OpenDocument(target);
    }

    private void OpenDocument(DocumentInfo? document)
    {
        if (document != null && document.FilePath == _document?.FilePath) return;

        SaveDocument();
        _completion?.Close();
        _document = document;
        RefreshCustomStyles(); // a document has its own styles, as well as the project's

        _loadingDocument = true;
        Editor.Document = new TextDocument(document != null ? _library.ReadDocument(document) : "");
        _loadingDocument = false;
        _dirty = false;

        bool hasDocument = document != null;
        TitleBox.Text = document?.Title ?? "";
        TitleBox.IsEnabled = ExportButton.IsEnabled = hasDocument;
        Editor.Visibility = hasDocument ? Visibility.Visible : Visibility.Hidden;
        Paper.Visibility = Editor.Visibility;
        EmptyState.Visibility = hasDocument || _project == null ? Visibility.Collapsed : Visibility.Visible;
        Title = hasDocument ? $"{document!.Title} — Daydreamer" : "Daydreamer";
        SetSaveStatus(hasDocument ? SaveState.Saved : SaveState.None);

        UpdateStats();
        UpdateCaretInfo();

        if (hasDocument)
        {
            _settings.LastDocument = document!.Title;
            SaveSettings();
            Dispatcher.BeginInvoke(() => Editor.Focus(), DispatcherPriority.Input);
        }
    }

    private void DocumentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionEvents && DocumentList.SelectedItem is DocumentInfo document)
            OpenDocument(document);
    }

    private void NewDocument_Click(object sender, RoutedEventArgs e) => NewDocument();

    private void NewDocument()
    {
        if (_project == null) return;
        ShowWriting(focusEditor: false); // in case we're on the Home page
        SaveDocument();
        var document = _library.CreateDocument(_project, "Untitled document");
        RefreshDocuments(document.Title);

        // Like Google Docs: name it straight away (or just start typing in the page).
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void RenameDocumentMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DocumentInfo document) return;
        DocumentList.SelectedItem = document;
        OpenDocument(document);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void TrashDocumentMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DocumentInfo document) return;

        bool confirmed = PromptDialog.Confirm(this, "Move to trash",
            $"Move \"{document.Title}\" to the trash?\n\nYou can get it back from the project's .trash folder (the Files button).",
            "Move to trash");
        if (!confirmed) return;

        if (document.FilePath == _document?.FilePath)
        {
            SaveDocument();
            OpenDocument(null);
        }
        _library.TrashDocument(document);
        if (_project != null)
        {
            _project.DocumentRemoved(document.Title);
            SaveProject();
        }
        RefreshDocuments(_document?.Title);
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitle();
            Editor.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TitleBox.Text = _document?.Title ?? "";
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void TitleBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitTitle();

    /// <summary>Renaming the title renames the file on disk.</summary>
    private void CommitTitle()
    {
        if (_document == null) return;
        string newTitle = TitleBox.Text.Trim();
        if (newTitle.Length == 0 || newTitle == _document.Title)
        {
            TitleBox.Text = _document.Title;
            return;
        }

        SaveDocument();
        try
        {
            string oldTitle = _document.Title;
            _document = _library.RenameDocument(_document, newTitle);
            if (_project != null)
            {
                _project.DocumentRenamed(oldTitle, _document.Title); // its pin and its styles go with it
                SaveProject();
            }
        }
        catch (IOException ex)
        {
            PromptDialog.Inform(this, "Rename", $"Couldn't rename the document.\n\n{ex.Message}");
        }

        TitleBox.Text = _document.Title;
        Title = $"{_document.Title} — Daydreamer";
        _settings.LastDocument = _document.Title;
        SaveSettings();
        RefreshDocuments(_document.Title);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Saving
    // ═══════════════════════════════════════════════════════════════════════

    private enum SaveState { None, Saved, Pending, Failed }

    private void SetSaveStatus(SaveState state)
    {
        (SaveIcon.Text, SaveStatus.Text) = state switch
        {
            SaveState.Saved => ("", "Saved"),
            SaveState.Pending => ("", "Saving…"),
            SaveState.Failed => ("", "Couldn't save — retrying"),
            _ => ("", ""),
        };
        var brush = state == SaveState.Failed ? (Brush)FindResource("WarningBrush") : (Brush)FindResource("MutedTextBrush");
        SaveIcon.Foreground = SaveStatus.Foreground = brush;
        SaveStatus.ToolTip = _document?.FilePath;
    }

    /// <summary>Writes the open document to disk if it has unsaved changes. Returns false if saving failed.</summary>
    private bool SaveDocument()
    {
        _autosaveTimer.Stop();
        if (!_dirty || _document == null) return true;

        try
        {
            _library.WriteDocument(_document, Editor.Text);
            _dirty = false;
            SetSaveStatus(SaveState.Saved);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // OneDrive sometimes locks files for a moment while syncing. Try again shortly.
            SetSaveStatus(SaveState.Failed);
            _autosaveTimer.Start();
            return false;
        }
    }

    private bool SaveProject()
    {
        _projectSaveTimer.Stop();
        if (_project == null) return true;
        try
        {
            _library.SaveProject(_project);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _projectSaveTimer.Start();
            return false;
        }
    }

    private bool SaveAll() => SaveDocument() & SaveProject();

    private void SaveSettings()
    {
        if (_persistSettings) _settings.Save();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (TitleBox.IsKeyboardFocused) CommitTitle();
        RememberWindowPlacement();
        SaveMusic(); // nothing in it is worth stopping the window closing for
        if (SaveAll()) return;

        e.Cancel = !PromptDialog.Confirm(this, "Daydreamer", "Some changes couldn't be saved. Close anyway?", "Close anyway");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Editor: typing, status, formatting
    // ═══════════════════════════════════════════════════════════════════════

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingDocument || _document == null) return;

        _dirty = true;
        SetSaveStatus(SaveState.Pending);
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
        _statsTimer.Stop();
        _statsTimer.Start();
    }

    private void Project_FormattingChanged(object? sender, EventArgs e)
    {
        if (_addingCharacters) return;
        ApplyFormatting();
        _projectSaveTimer.Stop();
        _projectSaveTimer.Start();
        _statsTimer.Stop();
        _statsTimer.Start(); // a renamed character changes which lines are theirs
    }

    /// <summary>Renamed keywords (SCENE for PANEL, say) change the names shown all over the window.</summary>
    private void Keywords_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The heading map's lists show type names, so give them a fresh look at the new names.
        HeadingMapGrid.DataContext = null;
        HeadingMapGrid.DataContext = _project?.ImportHeadings;
    }

    /// <summary>Re-reads the character list and styles, then redraws every visible line.</summary>
    private void ApplyFormatting()
    {
        var palette = ThemeManager.Palette;
        _parser.Registry = new CharacterRegistry(_project?.Characters ?? []);
        _parser.Keywords = _project?.Keywords ?? new Keywords();
        _parser.CustomStyles = _project?.StylesFor(_document?.Title) ?? [];
        _parser.DialogueMarkup = _project?.GameFolder != null;
        UpdateKeywordLabels();
        _colorizer.Styles = _project?.Styles;
        _colorizer.Palette = palette;
        UpdateSpellingWords(); // a new character (or a renamed one) is a known word

        var baseStyle = _project != null ? StyleResolver.ResolveBase(_project.Styles, palette) : StyleResolver.Start(palette.Text);
        // The editor's own font sets the height of empty lines, so it carries the line spacing too.
        Editor.FontFamily = WpfStyles.Family(baseStyle.FontFamily, baseStyle.LineSpacing);
        Editor.FontSize = WpfStyles.PointsToPixels(baseStyle.FontSize);
        Editor.Foreground = WpfStyles.TextBrush(baseStyle.Color, palette);
        Editor.TextArea.TextView.Redraw();

        foreach (var entry in _styleEntries.Concat(_customEntries)) entry.Refresh();
        _formatTarget.Refresh();
        UpdateCaretInfo();
    }

    private void UpdateStats()
    {
        _statsTimer.Stop();
        UpdateSpellingStatus();
        if (_project == null || _document == null)
        {
            StatsText.Text = "";
            UnknownSpeakersPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var stats = DocumentStats.Analyze(Editor.Text, _parser);
        StatsText.Text = $"{stats.Words:N0} words   ·   {Plural(stats.Pages, NameOf(ElementType.Page).ToLower(CultureInfo.CurrentCulture))}   ·   " +
                         $"{Plural(stats.Panels, NameOf(ElementType.Panel).ToLower(CultureInfo.CurrentCulture))}   ·   {Plural(stats.DialogueLines, "balloon")}";

        foreach (var character in _project.Characters)
            character.LineCount = stats.LinesByCharacter.GetValueOrDefault(character);

        UnknownSpeakersList.ItemsSource = stats.UnknownSpeakers;
        UnknownSpeakersPanel.Visibility = stats.UnknownSpeakers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddAllSpeakersButton.Visibility = stats.UnknownSpeakers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        AddAllSpeakersButton.Content = $"Add all {stats.UnknownSpeakers.Count}";
    }

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count:N0} {word}s";

    /// <summary>Shows what the parser thinks the current line is — handy for checking your formatting.</summary>
    private void UpdateCaretInfo()
    {
        UpdateFormatTarget();
        UpdateAlignmentIcon();
        TypeButton.IsEnabled = _document != null;
        if (_document == null)
        {
            CaretInfo.Text = "";
            TypeLabel.Text = NameOf(ElementType.Action);
            return;
        }

        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var parsed = _parser.ParseLine(Editor.Document.GetText(line));
        string modifier = string.IsNullOrEmpty(parsed.Modifier) ? "" : $" ({parsed.Modifier})";
        TypeLabel.Text = parsed.CustomStyle?.Name ?? NameOf(parsed.Type == ElementType.Blank ? ElementType.Action : parsed.Type);

        string what = parsed.Type switch
        {
            ElementType.Dialogue when parsed.Character != null => $"Dialogue · {parsed.Character.Name}{modifier}",
            ElementType.Dialogue => $"Dialogue · {parsed.Speaker} (not a character yet)",
            ElementType.Caption when parsed.Character != null => $"{NameOf(ElementType.Caption)} · narrated by {parsed.Character.Name}",
            ElementType.Blank => "",
            _ => NameOf(parsed.Type),
        };
        CaretInfo.Text = what.Length > 0 ? $"{what}      Line {line.LineNumber}" : $"Line {line.LineNumber}";
    }

    /// <summary>Centers the white "page" and lines the text up inside its margins.</summary>
    private void EditorHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double scrollBar = SystemParameters.VerticalScrollBarWidth;
        double available = Math.Max(0, EditorHost.ActualWidth - scrollBar);
        double paperWidth = Math.Max(200, Math.Min(PageWidth, available - 40));
        double left = Math.Max(12, (available - paperWidth) / 2);
        double margin = paperWidth >= 640 ? PageMargin : 32;

        Paper.Width = paperWidth;
        Paper.Margin = new Thickness(left, 18, 0, 0);
        Editor.Padding = new Thickness(left + margin, 56, Math.Max(0, available - left - paperWidth + margin), 24);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Toolbar & shortcuts
    // ═══════════════════════════════════════════════════════════════════════

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ColorPopup.IsOpen)
        {
            ColorPopup.IsOpen = false;
            Editor.Focus();
            e.Handled = true;
            return;
        }

        bool inEditor = Editor.TextArea.IsKeyboardFocusWithin;

        if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None && !IsHomeShown)
        {
            SetFocusMode(!_focusMode);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+L / E / R / J align the line (or the selected lines), as in Google Docs.
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && inEditor)
        {
            TextAlign? alignment = e.Key switch
            {
                Key.L => TextAlign.Left,
                Key.E => TextAlign.Center,
                Key.R => TextAlign.Right,
                Key.J => TextAlign.Justify,
                _ => null,
            };
            if (alignment is { } chosen)
            {
                AlignSelection(chosen);
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.N:
                NewDocument();
                break;
            case Key.S:
                if (TitleBox.IsKeyboardFocused) CommitTitle();
                SaveAll();
                break;
            case Key.B when inEditor:
                ToggleEmphasis("**");
                break;
            case Key.I when inEditor:
                ToggleEmphasis("*");
                break;
            case Key.Space when inEditor:
                ShowCompletionAtCaret(charactersOnly: false);
                break;
            case Key.Oem5 when !IsHomeShown: // Ctrl+\ (the backslash key)
                ToggleSidePanel();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Editor.Redo();
    private void Bold_Click(object sender, RoutedEventArgs e) => ToggleEmphasis("**");
    private void Italic_Click(object sender, RoutedEventArgs e) => ToggleEmphasis("*");

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        ApplicationCommands.Find.Execute(null, Editor.TextArea);
    }

    /// <summary>Wraps the selection in ** or * (or removes them if they're already there).</summary>
    private void ToggleEmphasis(string marker)
    {
        if (_document == null) return;
        var document = Editor.Document;
        int start = Editor.SelectionStart, length = Editor.SelectionLength;
        Editor.Focus();

        if (length == 0)
        {
            document.Insert(start, marker + marker);
            Editor.CaretOffset = start + marker.Length;
            return;
        }

        string selected = Editor.SelectedText;
        bool alreadyWrapped = selected.Length >= marker.Length * 2 && selected.StartsWith(marker) && selected.EndsWith(marker);
        string replacement = alreadyWrapped ? selected[marker.Length..^marker.Length] : marker + selected + marker;
        document.Replace(start, length, replacement);
        Editor.Select(start, replacement.Length);
    }

    private void InsertPage_Click(object sender, RoutedEventArgs e) =>
        InsertLine($"{_parser.Keywords.Page} {DocumentStats.NextPageNumber(LinesBeforeCaret(), _parser)}", spaced: true);

    private void InsertPanel_Click(object sender, RoutedEventArgs e) =>
        InsertLine($"{_parser.Keywords.Panel} {DocumentStats.NextPanelNumber(LinesBeforeCaret(), _parser)}", spaced: true);

    // Written the way the script writes its labels: "SFX: BOOM" or "SFX BOOM".
    private void InsertCaption_Click(object sender, RoutedEventArgs e) => InsertLine(_parser.Keywords.Caption + SpeakerSeparator());
    private void InsertSfx_Click(object sender, RoutedEventArgs e) => InsertLine(_parser.Keywords.Sfx + SpeakerSeparator());

    // A note's tag is hidden, so the note just reads as a note.
    private void InsertNote_Click(object sender, RoutedEventArgs e) => InsertLine("{note} ");

    /// <summary>The toolbar's insert buttons use the project's words ("Scene" if panels are called SCENE).</summary>
    private void UpdateKeywordLabels()
    {
        InsertPageLabel.Text = NameOf(ElementType.Page);
        InsertPanelLabel.Text = NameOf(ElementType.Panel);
        InsertCaptionLabel.Text = NameOf(ElementType.Caption);
        InsertSfxLabel.Text = NameOf(ElementType.Sfx);
        InsertNoteLabel.Text = NameOf(ElementType.Note);
        InsertPageButton.ToolTip = $"New {NameOf(ElementType.Page).ToLower(CultureInfo.CurrentCulture)} (numbered automatically)";
        InsertPanelButton.ToolTip = $"New {NameOf(ElementType.Panel).ToLower(CultureInfo.CurrentCulture)} (numbered automatically)";
    }

    private void InsertDialogue_Click(object sender, RoutedEventArgs e)
    {
        InsertLine("");
        ShowCompletionAtCaret(charactersOnly: true);
    }

    /// <summary>Puts text on the current line if it's empty, otherwise on a new line below it.</summary>
    private void InsertLine(string text, bool spaced = false)
    {
        if (_document == null) return;
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string newLine = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);

        if (string.IsNullOrWhiteSpace(document.GetText(line)))
        {
            document.Replace(line.Offset, line.Length, text);
            Editor.CaretOffset = line.Offset + text.Length;
        }
        else
        {
            string insert = (spaced ? newLine + newLine : newLine) + text;
            document.Insert(line.EndOffset, insert);
            Editor.CaretOffset = line.EndOffset + insert.Length;
        }
        Editor.Focus();
    }

    private IEnumerable<string> LinesBeforeCaret()
    {
        var document = Editor.Document;
        int caretLine = document.GetLineByOffset(Editor.CaretOffset).LineNumber;
        for (int n = 1; n < caretLine; n++)
            yield return document.GetText(document.GetLineByNumber(n));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Auto-complete pop-up
    // ═══════════════════════════════════════════════════════════════════════

    // A run of capitals at the start of a line, e.g. "AK" while typing "AKIRA".
    private static readonly Regex CapsPrefix = new(@"^\p{Lu}[\p{Lu}\p{N} .'\-]*$");

    /// <summary>Typing a name in CAPS at the start of a line offers matching characters.</summary>
    private void Editor_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_completion != null)
        {
            // Close the pop-up once nothing in it matches what's being typed.
            Dispatcher.BeginInvoke(() =>
            {
                if (_completion?.CompletionList.ListBox is { Items.Count: 0 }) _completion.Close();
            }, DispatcherPriority.Background);
            return;
        }
        if (_project == null) return;

        var (start, typed) = LinePrefixAtCaret();
        if (typed.Length < 2 || !CapsPrefix.IsMatch(typed)) return;

        var items = BuildCompletionItems(charactersOnly: false);
        if (items.Any(i => i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
            ShowCompletion(start, items, typed);
    }

    private void ShowCompletionAtCaret(bool charactersOnly)
    {
        if (_project == null || _document == null) return;
        _completion?.Close();

        var (start, typed) = LinePrefixAtCaret();
        if (typed.Length > 0 && !CapsPrefix.IsMatch(typed))
        {
            start = Editor.CaretOffset;
            typed = "";
        }
        ShowCompletion(start, BuildCompletionItems(charactersOnly), typed);
    }

    /// <summary>The text between the start of the line (ignoring indentation) and the caret.</summary>
    private (int Start, string Text) LinePrefixAtCaret()
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        string beforeCaret = Editor.Document.GetText(line.Offset, Editor.CaretOffset - line.Offset);
        string trimmed = beforeCaret.TrimStart();
        return (line.Offset + beforeCaret.Length - trimmed.Length, trimmed);
    }

    private List<ScriptCompletionData> BuildCompletionItems(bool charactersOnly)
    {
        string separator = SpeakerSeparator();
        var items = _project!.Characters
            .Where(c => c.Name.Length > 0)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => ScriptCompletionData.ForCharacter(c, ThemeManager.Palette, separator))
            .ToList();

        if (!charactersOnly)
        {
            var keywords = _parser.Keywords;
            var before = LinesBeforeCaret().ToList();
            items.Add(new(keywords.Page, $"{keywords.Page} {DocumentStats.NextPageNumber(before, _parser)}", $"Start a new {NameOf(ElementType.Page).ToLower(CultureInfo.CurrentCulture)}"));
            items.Add(new(keywords.Panel, $"{keywords.Panel} {DocumentStats.NextPanelNumber(before, _parser)}", $"Start a new {NameOf(ElementType.Panel).ToLower(CultureInfo.CurrentCulture)}"));
            items.Add(new(keywords.Caption, keywords.Caption + separator, "Narration box"));
            items.Add(new(keywords.Sfx, keywords.Sfx + separator, "Sound effect"));
            items.Add(new(keywords.Note, "{note} ", "A note to yourself"));
        }
        return items;
    }

    private void ShowCompletion(int startOffset, IEnumerable<ICompletionData> items, string filter)
    {
        var window = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = startOffset,
            MinWidth = 240,
            CloseWhenCaretAtBeginning = true,
            // The list inside is themed by a style in Themes/Daydreamer.xaml; the window frame is set here.
            Background = (Brush)FindResource("PaperBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("DividerBrush"),
            BorderThickness = new Thickness(1),
        };
        foreach (var item in items) window.CompletionList.CompletionData.Add(item);
        window.Closed += (_, _) => _completion = null;
        _completion = window;
        window.Show();
        if (filter.Length > 0) window.CompletionList.SelectItem(filter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Right-click menu in the editor
    // ═══════════════════════════════════════════════════════════════════════

    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = Editor.ContextMenu;
        menu.Items.Clear();

        // Work out where was clicked (right-clicking doesn't move the caret). Opened from the keyboard
        // (the Menu key, or Shift+F10), it's about where the caret is.
        bool fromKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        TextViewPosition? position = fromKeyboard ? Editor.TextArea.Caret.Position : Editor.GetPositionFromPoint(Mouse.GetPosition(Editor));
        AddSpellingItems(menu, position); // a misspelled word's fixes come first, like in Word

        menu.Items.Add(new MenuItem { Header = "Cut", Command = ApplicationCommands.Cut, CommandTarget = Editor.TextArea });
        menu.Items.Add(new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = Editor.TextArea });
        menu.Items.Add(new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste, CommandTarget = Editor.TextArea });
        AddEffectItems(menu);

        if (_project == null || _document == null) return;
        int lineNumber = position?.Line ?? Editor.TextArea.Caret.Line;
        var parsed = _parser.ParseLine(Editor.Document.GetText(Editor.Document.GetLineByNumber(lineNumber)));

        MenuItem? action = null;
        if (parsed.IsUnknownSpeaker && parsed.Speaker != null)
        {
            string speaker = parsed.Speaker;
            action = new MenuItem { Header = $"Add {speaker} as a character", FontWeight = FontWeights.SemiBold };
            action.Click += (_, _) => AddCharacter(speaker);
        }
        else if (parsed.Character != null)
        {
            var character = parsed.Character;
            action = new MenuItem { Header = $"Edit {character.Name}'s formatting" };
            action.Click += (_, _) => SelectCharacter(character);
        }
        else if (parsed.Type != ElementType.Blank)
        {
            var entry = _styleEntries.FirstOrDefault(s => s.Type == parsed.Type);
            if (entry != null)
            {
                action = new MenuItem { Header = $"Edit the {entry.Name} style" };
                action.Click += (_, _) =>
                {
                    ShowSidePanelTab(StylesTab);
                    StylesList.SelectedItem = entry;
                };
            }
        }

        if (action != null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(action);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(SpellingToggleItem());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Characters & styles panel
    // ═══════════════════════════════════════════════════════════════════════

    private void AddCharacter_Click(object sender, RoutedEventArgs e) => AddCharacter(null);

    private void AddUnknownSpeaker_Click(object sender, RoutedEventArgs e) =>
        AddCharacter((string)((FrameworkElement)sender).Tag);

    /// <summary>Adds every suggested speaker as a character in one go.</summary>
    private void AddAllSpeakers_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || UnknownSpeakersList.ItemsSource is not IEnumerable<string> names) return;
        _addingCharacters = true; // one redraw at the end, not one per name
        try
        {
            foreach (string name in names.ToList())
            {
                if (!_project.Characters.Any(c => c.AllNames().Contains(name, StringComparer.OrdinalIgnoreCase)))
                    _project.Characters.Add(new Character { Name = name });
            }
        }
        finally
        {
            _addingCharacters = false;
        }
        Project_FormattingChanged(this, EventArgs.Empty);
    }

    private bool _addingCharacters;

    private void AddCharacter(string? name)
    {
        if (_project == null) return;
        name ??= PromptDialog.Ask(this, "New character", "Character name, as you'll type it in the script:", "", "Add");
        if (name == null) return;

        var existing = _project.Characters.FirstOrDefault(c => c.AllNames().Contains(name, StringComparer.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectCharacter(existing);
            return;
        }

        // No color by default: characters are told apart by font, and color is there if you want it.
        var character = new Character { Name = name };
        _project.Characters.Add(character);
        SelectCharacter(character);
    }

    private void SelectCharacter(Character character)
    {
        ShowSidePanelTab(CharactersTab);
        CharacterList.SelectedItem = character;
        CharacterList.ScrollIntoView(character);
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || CharacterList.SelectedItem is not Character character) return;
        bool confirmed = PromptDialog.Confirm(this, "Remove character",
            $"Remove {character.Name}?\n\nTheir lines stay in your script — they just lose their special formatting.",
            "Remove");
        if (confirmed)
            _project.Characters.Remove(character);
    }

    /// <summary>Enter in a keyword box applies the new word straight away.</summary>
    private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.SelectAll();
        e.Handled = true;
    }

    private void AddModifier_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        string? keyword = PromptDialog.Ask(this, "New modifier rule",
            "Which word in parentheses should trigger it? (e.g. yell, radio, flashback)", "", "Add");
        if (keyword == null) return;

        var rule = new ModifierStyle { Keyword = keyword.Trim('(', ')', ' ') };
        _project.Styles.Modifiers.Add(rule);
        ModifierList.SelectedItem = rule;
    }

    private void DeleteModifier_Click(object sender, RoutedEventArgs e)
    {
        if (_project != null && ModifierList.SelectedItem is ModifierStyle rule)
            _project.Styles.Modifiers.Remove(rule);
    }
}
