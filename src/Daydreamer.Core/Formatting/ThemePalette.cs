using Daydreamer.Core.Models;

namespace Daydreamer.Core.Formatting;

/// <summary>
/// The grays text is drawn in when a style doesn't choose its own color. Light and dark mode each have
/// a palette, so the default look stays readable in both — color is something you opt into, never required.
/// </summary>
public sealed record ThemePalette(
    string Strong,     // headings and page markers
    string Text,       // everything else, by default
    string Secondary,  // panel markers
    string Muted,      // notes and (modifiers)
    string Faint,      // labels like "SFX:" and "CAPTION:"
    string Ghost,      // the ** and * around emphasis
    string Warning,    // the underline under speakers who aren't characters yet
    string Paper)      // the page color, used to keep custom colors readable
{
    public static readonly ThemePalette Light = new(
        Strong: "#202124", Text: "#3C4043", Secondary: "#5F6368", Muted: "#80868B",
        Faint: "#9AA0A6", Ghost: "#C4C7CC", Warning: "#E8590C", Paper: "#FFFFFF");

    public static readonly ThemePalette Dark = new(
        Strong: "#F1F1F4", Text: "#CFD0D6", Secondary: "#A3A6AE", Muted: "#80848C",
        Faint: "#6E727A", Ghost: "#4A4E55", Warning: "#FF922B", Paper: "#1E1E23");

    /// <summary>True if <paramref name="color"/> is one of this palette's own tones (as opposed to a color someone picked).</summary>
    public bool Contains(string color) =>
        new[] { Strong, Text, Secondary, Muted, Faint, Ghost, Warning }.Contains(color, StringComparer.OrdinalIgnoreCase);

    /// <summary>The default gray for a kind of line.</summary>
    public string ToneFor(ElementType type) => type switch
    {
        ElementType.Heading or ElementType.Page => Strong,
        ElementType.Panel => Secondary,
        ElementType.Note => Muted,
        _ => Text,
    };
}
