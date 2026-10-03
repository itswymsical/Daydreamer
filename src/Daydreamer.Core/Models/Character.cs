using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Daydreamer.Core.Models;

/// <summary>
/// A character the parser knows about. Any line that starts with their name (or an alias)
/// followed by a colon becomes a Dialogue line and is drawn with <see cref="Style"/>.
/// </summary>
public sealed class Character : ObservableObject
{
    private string _name = "";
    private string _aliases = "";
    private string? _nameColor;
    private string _notes = "";
    private TextStyle _style = new();
    private int _lineCount;

    public Character()
    {
        _style.PropertyChanged += OnStyleChanged;
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value?.Trim() ?? "");
    }

    /// <summary>Other names that should count as this character, comma-separated (e.g. "AKI, The Kid").</summary>
    public string Aliases
    {
        get => _aliases;
        set => SetField(ref _aliases, value ?? "");
    }

    /// <summary>Color of the name tag (the "AKIRA:" part) so speakers are easy to tell apart.</summary>
    public string? NameColor
    {
        get => _nameColor;
        set => SetField(ref _nameColor, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value ?? "");
    }

    /// <summary>Formatting for this character's lines (font, size, color...).</summary>
    public TextStyle Style
    {
        get => _style;
        set
        {
            _style.PropertyChanged -= OnStyleChanged;
            _style = value ?? new TextStyle();
            _style.PropertyChanged += OnStyleChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>How many dialogue lines this character has in the open document. Not saved.</summary>
    [JsonIgnore]
    public int LineCount
    {
        get => _lineCount;
        set => SetField(ref _lineCount, value);
    }

    /// <summary>The name plus every alias — everything the parser should recognise.</summary>
    public IEnumerable<string> AllNames()
    {
        if (Name.Length > 0) yield return Name;
        foreach (var alias in Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return alias;
    }

    public override string ToString() => Name;

    // Re-announce nested style edits as a change to this character, so one listener catches everything.
    private void OnStyleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Style));
}
