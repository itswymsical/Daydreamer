using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Daydreamer.Core.Models;

/// <summary>The formatting rule for one kind of line, e.g. "SFX lines are italic".</summary>
public sealed class ElementStyle : ObservableObject
{
    private TextStyle _style = new();

    public ElementStyle()
    {
        _style.PropertyChanged += OnStyleChanged;
    }

    public ElementType Type { get; set; }

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

    [JsonIgnore] public string DisplayName => StyleSheet.DisplayNameOf(Type);
    [JsonIgnore] public string Example => StyleSheet.ExampleOf(Type);

    private void OnStyleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Style));
}

/// <summary>
/// A rule triggered by a word in parentheses on a dialogue line,
/// e.g. <c>AKIRA (thought): ...</c> makes the line italic.
/// </summary>
public sealed class ModifierStyle : ObservableObject
{
    private string _keyword = "";
    private TextStyle _style = new();

    public ModifierStyle()
    {
        _style.PropertyChanged += OnStyleChanged;
    }

    public string Keyword
    {
        get => _keyword;
        set => SetField(ref _keyword, value?.Trim() ?? "");
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

    public override string ToString() => $"({Keyword})";

    private void OnStyleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Style));
}

/// <summary>All the formatting rules for a project.</summary>
public sealed class StyleSheet : ObservableObject
{
    private TextStyle _base = new();
    private ObservableCollection<ElementStyle> _elements = [];
    private ObservableCollection<ModifierStyle> _modifiers = [];

    public StyleSheet()
    {
        _base.PropertyChanged += OnChildChanged;
        _elements.CollectionChanged += OnCollectionChanged;
        _modifiers.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>"Default text" — the starting point every other style builds on.</summary>
    public TextStyle Base
    {
        get => _base;
        set
        {
            _base.PropertyChanged -= OnChildChanged;
            _base = value ?? new TextStyle();
            _base.PropertyChanged += OnChildChanged;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ElementStyle> Elements
    {
        get => _elements;
        set => _elements = Rewire(_elements, value);
    }

    public ObservableCollection<ModifierStyle> Modifiers
    {
        get => _modifiers;
        set => _modifiers = Rewire(_modifiers, value);
    }

    public TextStyle? For(ElementType type) => Elements.FirstOrDefault(e => e.Type == type)?.Style;

    /// <summary>Finds a modifier rule whose keyword appears in "(thought, off-panel)"-style text.</summary>
    public IEnumerable<ModifierStyle> MatchModifiers(string? modifierText)
    {
        if (string.IsNullOrWhiteSpace(modifierText)) yield break;
        var words = modifierText.Split([',', ' ', '/', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rule in Modifiers)
        {
            if (rule.Keyword.Length > 0 && words.Contains(rule.Keyword, StringComparer.OrdinalIgnoreCase))
                yield return rule;
        }
    }

    /// <summary>Makes sure every element type has an entry (older project files may be missing some).</summary>
    public void EnsureAllElements()
    {
        var defaults = CreateDefault();
        foreach (var def in defaults.Elements)
        {
            if (Elements.All(e => e.Type != def.Type))
                Elements.Add(def);
        }
        // Keep them in the same order as the enum so the Styles list is predictable.
        var ordered = Elements.OrderBy(e => e.Type).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int current = Elements.IndexOf(ordered[i]);
            if (current != i) Elements.Move(current, i);
        }
    }

    /// <summary>
    /// The starting rules for a new project. No colors on purpose: unset colors follow the
    /// light/dark theme's grays, and color is something you add only where you want it.
    /// </summary>
    public static StyleSheet CreateDefault()
    {
        var sheet = new StyleSheet
        {
            // 1.15 line spacing, like a new Google Doc: a touch airier than single.
            Base = new TextStyle { FontFamily = "Segoe UI", FontSize = 12, LineSpacing = 1.15 },
        };

        void Add(ElementType type, TextStyle style) => sheet.Elements.Add(new ElementStyle { Type = type, Style = style });

        Add(ElementType.Heading, new TextStyle { FontSize = 20, Bold = true });
        Add(ElementType.Page, new TextStyle { FontSize = 14, Bold = true });
        Add(ElementType.Panel, new TextStyle { Bold = true });
        Add(ElementType.Action, new TextStyle());
        Add(ElementType.ListItem, new TextStyle());
        Add(ElementType.Dialogue, new TextStyle());
        Add(ElementType.Caption, new TextStyle { Bold = true });
        Add(ElementType.Sfx, new TextStyle { Italic = true });
        Add(ElementType.Note, new TextStyle { Italic = true, FontSize = 10.5 });

        sheet.Modifiers.Add(new ModifierStyle { Keyword = "thought", Style = new TextStyle { Italic = true } });
        sheet.Modifiers.Add(new ModifierStyle { Keyword = "shout", Style = new TextStyle { Bold = true } });

        return sheet;
    }

    public static string DisplayNameOf(ElementType type) => type switch
    {
        ElementType.Sfx => "SFX",
        ElementType.Action => "Description",
        ElementType.ListItem => "List item",
        _ => type.ToString(),
    };

    public static string ExampleOf(ElementType type) => type switch
    {
        ElementType.Heading => "# Chapter 1",
        ElementType.Page => "PAGE 1",
        ElementType.Panel => "PANEL 1",
        ElementType.Action => "A quiet street at dusk.",
        ElementType.ListItem => "- A list item",
        ElementType.Dialogue => "NAME: Hello!",
        ElementType.Caption => "CAPTION: Tokyo, 2045.",
        ElementType.Sfx => "SFX: KRAKOOM",
        ElementType.Note => "// note to self",
        _ => "",
    };

    private ObservableCollection<T> Rewire<T>(ObservableCollection<T> oldList, ObservableCollection<T>? newList)
        where T : INotifyPropertyChanged
    {
        oldList.CollectionChanged -= OnCollectionChanged;
        foreach (var item in oldList) item.PropertyChanged -= OnChildChanged;

        newList ??= [];
        newList.CollectionChanged += OnCollectionChanged;
        foreach (var item in newList) item.PropertyChanged += OnChildChanged;
        OnPropertyChanged(typeof(T) == typeof(ElementStyle) ? nameof(Elements) : nameof(Modifiers));
        return newList;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (INotifyPropertyChanged item in e.OldItems) item.PropertyChanged -= OnChildChanged;
        if (e.NewItems != null)
            foreach (INotifyPropertyChanged item in e.NewItems) item.PropertyChanged += OnChildChanged;
        OnPropertyChanged(sender == _elements ? nameof(Elements) : nameof(Modifiers));
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(sender == _base ? nameof(Base) : sender is ElementStyle ? nameof(Elements) : nameof(Modifiers));
}
