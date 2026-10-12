namespace Daydreamer.Core.Models;

/// <summary>
/// What Google Docs / Word headings become when a document is imported or pasted. The defaults suit a
/// manga script written in Docs: Heading 1 → PAGE, Heading 4 → PANEL, Title / Heading 2 / Heading 3 → # heading.
/// </summary>
public sealed class HeadingMap : ObservableObject
{
    private ElementType _title = ElementType.Heading;
    private ElementType _heading1 = ElementType.Page;
    private ElementType _heading2 = ElementType.Heading;
    private ElementType _heading3 = ElementType.Heading;
    private ElementType _heading4 = ElementType.Panel;
    private ElementType _heading5 = ElementType.Action;
    private ElementType _heading6 = ElementType.Action;

    public ElementType Title { get => _title; set => SetField(ref _title, value); }
    public ElementType Heading1 { get => _heading1; set => SetField(ref _heading1, value); }
    public ElementType Heading2 { get => _heading2; set => SetField(ref _heading2, value); }
    public ElementType Heading3 { get => _heading3; set => SetField(ref _heading3, value); }
    public ElementType Heading4 { get => _heading4; set => SetField(ref _heading4, value); }
    public ElementType Heading5 { get => _heading5; set => SetField(ref _heading5, value); }
    public ElementType Heading6 { get => _heading6; set => SetField(ref _heading6, value); }

    /// <summary>The kinds of line a heading can become.</summary>
    public static IReadOnlyList<ElementType> Choices { get; } =
    [
        ElementType.Page, ElementType.Panel, ElementType.Heading, ElementType.Action,
        ElementType.Caption, ElementType.Sfx, ElementType.Note,
    ];

    /// <summary>Level 0 is the Title style; 1–6 are Heading 1–6.</summary>
    public ElementType For(int level) => level switch
    {
        0 => Title,
        1 => Heading1,
        2 => Heading2,
        3 => Heading3,
        4 => Heading4,
        5 => Heading5,
        _ => Heading6,
    };
}
