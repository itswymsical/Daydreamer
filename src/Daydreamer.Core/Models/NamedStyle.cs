using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Daydreamer.Core.Models;

/// <summary>
/// A style you made yourself ("Letter", "Inscription"), for lines that don't fit the usual kinds. Any line can
/// wear one (it's kept in the line's hidden tag: <c>{style:Letter}</c>), and it goes on top of everything else
/// the line already is, so a character's line in the Letter style is still that character's line.
/// </summary>
public sealed class NamedStyle : ObservableObject
{
    private string _name = "";
    private TextStyle _style = new();

    public NamedStyle()
    {
        _style.PropertyChanged += OnStyleChanged;
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, CleanName(value));
    }

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

    /// <summary>A style's name goes inside a line's {tag}, so it can't have braces in it.</summary>
    public static string CleanName(string? name) => (name ?? "").Replace("{", "").Replace("}", "").Trim();

    public override string ToString() => Name;

    private void OnStyleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Style));
}

/// <summary>What a project remembers about one of its documents. Empty settings aren't kept.</summary>
public sealed class DocumentSettings : ObservableObject
{
    private bool _pinned;
    private ObservableCollection<NamedStyle> _styles = [];

    public DocumentSettings()
    {
        _styles.CollectionChanged += OnStylesChanged;
    }

    /// <summary>Pinned documents stay at the top of the document lists and on the Home page.</summary>
    public bool Pinned
    {
        get => _pinned;
        set => SetField(ref _pinned, value);
    }

    /// <summary>Styles that only this document offers. They win over a project style with the same name.</summary>
    public ObservableCollection<NamedStyle> Styles
    {
        get => _styles;
        set
        {
            _styles.CollectionChanged -= OnStylesChanged;
            foreach (var style in _styles) style.PropertyChanged -= OnStyleChanged;
            _styles = value ?? [];
            _styles.CollectionChanged += OnStylesChanged;
            foreach (var style in _styles) style.PropertyChanged += OnStyleChanged;
            OnPropertyChanged();
        }
    }

    [JsonIgnore] public bool IsEmpty => !Pinned && Styles.Count == 0;

    private void OnStylesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (NamedStyle style in e.OldItems) style.PropertyChanged -= OnStyleChanged;
        if (e.NewItems != null)
            foreach (NamedStyle style in e.NewItems) style.PropertyChanged += OnStyleChanged;
        OnPropertyChanged(nameof(Styles));
    }

    private void OnStyleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Styles));
}
