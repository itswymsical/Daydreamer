using System.Globalization;

namespace Daydreamer.Core.Models;

/// <summary>
/// The words that start special lines — PAGE 1, PANEL 2, CAPTION, SFX, NOTE — so each project can use its own
/// vocabulary (SCENE instead of PANEL, say). The built-in words keep working as well, so older text still reads right.
/// </summary>
public sealed class Keywords : ObservableObject
{
    public const string DefaultPage = "PAGE";
    public const string DefaultPanel = "PANEL";
    public const string DefaultCaption = "CAPTION";
    public const string DefaultSfx = "SFX";
    public const string DefaultNote = "NOTE";

    private string _page = DefaultPage;
    private string _panel = DefaultPanel;
    private string _caption = DefaultCaption;
    private string _sfx = DefaultSfx;
    private string _note = DefaultNote;

    public string Page { get => _page; set => SetField(ref _page, Clean(value, DefaultPage)); }
    public string Panel { get => _panel; set => SetField(ref _panel, Clean(value, DefaultPanel)); }
    public string Caption { get => _caption; set => SetField(ref _caption, Clean(value, DefaultCaption)); }
    public string Sfx { get => _sfx; set => SetField(ref _sfx, Clean(value, DefaultSfx)); }
    public string Note { get => _note; set => SetField(ref _note, Clean(value, DefaultNote)); }

    /// <summary>The kinds of line that start with a keyword.</summary>
    public static IReadOnlyList<ElementType> Customizable { get; } =
        [ElementType.Page, ElementType.Panel, ElementType.Caption, ElementType.Sfx, ElementType.Note];

    /// <summary>The keyword for a kind of line, or "" if it doesn't have one.</summary>
    public string For(ElementType type) => type switch
    {
        ElementType.Page => Page,
        ElementType.Panel => Panel,
        ElementType.Caption => Caption,
        ElementType.Sfx => Sfx,
        ElementType.Note => Note,
        _ => "",
    };

    public static string DefaultFor(ElementType type) => type switch
    {
        ElementType.Page => DefaultPage,
        ElementType.Panel => DefaultPanel,
        ElementType.Caption => DefaultCaption,
        ElementType.Sfx => DefaultSfx,
        ElementType.Note => DefaultNote,
        _ => "",
    };

    /// <summary>
    /// The name people see: "Scene" when the Panel keyword is SCENE, "SFX" for short words,
    /// and the usual names ("Description", "Dialogue") for kinds of line without a keyword.
    /// </summary>
    public string NameOf(ElementType type)
    {
        string word = For(type);
        if (word.Length == 0) return StyleSheet.DisplayNameOf(type);
        return word.Length <= 3 ? word : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant());
    }

    /// <summary>Keywords are written in capitals; anything that isn't a letter, digit or space is dropped.</summary>
    private static string Clean(string? value, string fallback)
    {
        string cleaned = new string((value ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().ToUpperInvariant();
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
