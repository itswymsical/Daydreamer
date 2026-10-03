namespace Daydreamer.Core.Models;

/// <summary>What kind of script line something is. The parser assigns one of these to every line.</summary>
public enum ElementType
{
    /// <summary>An empty line.</summary>
    Blank,

    /// <summary><c># Chapter 1</c> — a chapter or scene title.</summary>
    Heading,

    /// <summary><c>PAGE 1</c></summary>
    Page,

    /// <summary><c>PANEL 1</c></summary>
    Panel,

    /// <summary>Anything that isn't a special line: panel descriptions, prose, stage directions.</summary>
    Action,

    /// <summary><c>AKIRA: I can't believe it's over.</c></summary>
    Dialogue,

    /// <summary><c>CAPTION: Tokyo, 2045.</c></summary>
    Caption,

    /// <summary><c>SFX: KRRRAAASH</c></summary>
    Sfx,

    /// <summary><c>// a note to yourself</c> or <c>NOTE: ...</c></summary>
    Note,
}
