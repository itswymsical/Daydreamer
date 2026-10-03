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

    public Project()
    {
        _characters.CollectionChanged += OnCharactersChanged;
        _styles.PropertyChanged += OnStylesChanged;
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

    private void OnStylesChanged(object? sender, PropertyChangedEventArgs e) =>
        FormattingChanged?.Invoke(this, EventArgs.Empty);
}
