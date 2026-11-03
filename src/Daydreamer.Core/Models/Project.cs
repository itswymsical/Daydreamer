using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Daydreamer.Core.Models;

/// <summary>
/// A project is one story or manga series: a folder of documents (chapters) that share
/// the same cast of characters and the same formatting rules.
/// </summary>
public sealed class Project : ObservableObject
{
    private ObservableCollection<Character> _characters = [];
    private StyleSheet _styles = StyleSheet.CreateDefault();
    private HeadingMap _importHeadings = new();
    private Keywords _keywords = new();
    private ObservableCollection<string> _knownWords = [];
    private ObservableCollection<NamedStyle> _customStyles = [];
    private Dictionary<string, DocumentSettings> _documents = new(StringComparer.OrdinalIgnoreCase);

    public Project()
    {
        _knownWords.CollectionChanged += OnKnownWordsChanged;
        _customStyles.CollectionChanged += OnCustomStylesChanged;
        _characters.CollectionChanged += OnCharactersChanged;
        _styles.PropertyChanged += OnStylesChanged;
        _importHeadings.PropertyChanged += OnStylesChanged;
        _keywords.PropertyChanged += OnStylesChanged;
    }

    /// <summary>The words that start special lines (PAGE, PANEL, CAPTION, SFX, NOTE) in this project.</summary>
    public Keywords Keywords
    {
        get => _keywords;
        set
        {
            _keywords.PropertyChanged -= OnStylesChanged;
            _keywords = value ?? new Keywords();
            _keywords.PropertyChanged += OnStylesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>The project.json format this code writes. See <c>ProjectUpgrades</c> for what changed.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Bumped if the file layout ever changes, so old projects can be upgraded.</summary>
    public int Version { get; set; } = CurrentVersion;

    public ObservableCollection<Character> Characters
    {
        get => _characters;
        set
        {
            _characters.CollectionChanged -= OnCharactersChanged;
            foreach (var c in _characters) c.PropertyChanged -= OnCharacterChanged;

            _characters = value ?? [];
            _characters.CollectionChanged += OnCharactersChanged;
            foreach (var c in _characters) c.PropertyChanged += OnCharacterChanged;
            OnPropertyChanged();
        }
    }

    public StyleSheet Styles
    {
        get => _styles;
        set
        {
            _styles.PropertyChanged -= OnStylesChanged;
            _styles = value ?? StyleSheet.CreateDefault();
            _styles.PropertyChanged += OnStylesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Words this story uses that the spell checker shouldn't underline: made-up places, slang, sounds
    /// ("Neo-Kyoto", "gonna"). Character names never need adding; they're always known.
    /// </summary>
    public ObservableCollection<string> KnownWords
    {
        get => _knownWords;
        set
        {
            _knownWords.CollectionChanged -= OnKnownWordsChanged;
            _knownWords = value ?? [];
            _knownWords.CollectionChanged += OnKnownWordsChanged;
            OnPropertyChanged();
        }
    }

    // A new word changes which words are underlined (and needs saving), like a formatting change.
    private void OnKnownWordsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        FormattingChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>What Google Docs / Word headings become when importing or pasting.</summary>
    public HeadingMap ImportHeadings
    {
        get => _importHeadings;
        set
        {
            _importHeadings.PropertyChanged -= OnStylesChanged;
            _importHeadings = value ?? new HeadingMap();
            _importHeadings.PropertyChanged += OnStylesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>Styles you made yourself that every document in this project offers.</summary>
    public ObservableCollection<NamedStyle> CustomStyles
    {
        get => _customStyles;
        set
        {
            _customStyles.CollectionChanged -= OnCustomStylesChanged;
            foreach (var style in _customStyles) style.PropertyChanged -= OnStylesChanged;
            _customStyles = value ?? [];
            _customStyles.CollectionChanged += OnCustomStylesChanged;
            foreach (var style in _customStyles) style.PropertyChanged += OnStylesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>What's remembered about individual documents (pins, their own styles), by title.</summary>
    public Dictionary<string, DocumentSettings> Documents
    {
        get => _documents;
        set
        {
            foreach (var settings in _documents.Values) settings.PropertyChanged -= OnStylesChanged;
            _documents = new Dictionary<string, DocumentSettings>(value ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var settings in _documents.Values) settings.PropertyChanged += OnStylesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The game folder this project's dialogue is sent to, like <c>…\ModSources\TrelamiumIII\src\TrelamiumIII</c>.
    /// Setting it also turns on dialogue markup (see <c>ScriptParser.DialogueMarkup</c>). Null for other stories.
    /// </summary>
    public string? GameFolder { get; set; }

    // ── Per-document settings ───────────────────────────────────────────────

    public bool IsPinned(string title) => _documents.TryGetValue(title, out var settings) && settings.Pinned;

    public void SetPinned(string title, bool pinned)
    {
        if (pinned) SettingsFor(title).Pinned = true;
        else if (_documents.TryGetValue(title, out var settings)) settings.Pinned = false;
        DropIfEmpty(title);
    }

    /// <summary>The styles only this document offers (none yet: an empty list).</summary>
    public IReadOnlyList<NamedStyle> DocumentStyles(string? title) =>
        title != null && _documents.TryGetValue(title, out var settings) ? settings.Styles : [];

    /// <summary>The document's own settings, made if it doesn't have any yet.</summary>
    public DocumentSettings SettingsFor(string title)
    {
        if (!_documents.TryGetValue(title, out var settings))
        {
            settings = new DocumentSettings();
            settings.PropertyChanged += OnStylesChanged;
            _documents[title] = settings;
        }
        return settings;
    }

    /// <summary>Forgets a document's settings if there's nothing left in them.</summary>
    public void DropIfEmpty(string title)
    {
        if (!_documents.TryGetValue(title, out var settings) || !settings.IsEmpty) return;
        settings.PropertyChanged -= OnStylesChanged;
        _documents.Remove(title);
    }

    /// <summary>Every style a document can use: its own first (they win when names match), then the project's.</summary>
    public IEnumerable<NamedStyle> StylesFor(string? title) => DocumentStyles(title).Concat(_customStyles);

    /// <summary>A renamed document keeps its pin and its styles.</summary>
    public void DocumentRenamed(string oldTitle, string newTitle)
    {
        if (!_documents.Remove(oldTitle, out var settings)) return;
        _documents[newTitle] = settings;
    }

    /// <summary>A document moved to the trash takes its settings with it.</summary>
    public void DocumentRemoved(string title)
    {
        if (_documents.Remove(title, out var settings))
            settings.PropertyChanged -= OnStylesChanged;
    }

    /// <summary>The folder on disk. Its name is the project's name.</summary>
    [JsonIgnore] public string FolderPath { get; set; } = "";

    [JsonIgnore] public string Name => Path.GetFileName(FolderPath);

    /// <summary>
    /// Raised whenever anything that affects how text looks changes: a character is added,
    /// renamed, or restyled, or a style rule is edited. The editor listens to this to redraw.
    /// </summary>
    public event EventHandler? FormattingChanged;

    // LineCount is just a statistic shown in the UI; changing it shouldn't trigger a redraw or save.
    private void OnCharacterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Character.LineCount))
            FormattingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCharactersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Character c in e.OldItems) c.PropertyChanged -= OnCharacterChanged;
        if (e.NewItems != null)
            foreach (Character c in e.NewItems) c.PropertyChanged += OnCharacterChanged;
        FormattingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCustomStylesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (NamedStyle style in e.OldItems) style.PropertyChanged -= OnStylesChanged;
        if (e.NewItems != null)
            foreach (NamedStyle style in e.NewItems) style.PropertyChanged += OnStylesChanged;
        FormattingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStylesChanged(object? sender, PropertyChangedEventArgs e) =>
        FormattingChanged?.Invoke(this, EventArgs.Empty);
}
