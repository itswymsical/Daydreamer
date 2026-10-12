namespace Daydreamer.Core.Models;

/// <summary>How a line sits between the page margins.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,

    /// <summary>Stretched to both margins (the last row of a wrapped line stays left-aligned).</summary>
    Justify,
}

/// <summary>The kinds of list a line can be.</summary>
public enum ListKind
{
    None,

    /// <summary><c>- item</c> (or <c>* item</c>, <c>• item</c>)</summary>
    Bullet,

    /// <summary><c>1. item</c> (or <c>1) item</c>)</summary>
    Numbered,

    /// <summary><c>[ ] task</c> or <c>[x] done</c></summary>
    Checkbox,
}
