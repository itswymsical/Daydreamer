using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;

namespace Daydreamer.Core.Formatting;

/// <summary>A fully worked-out style: every value is filled in, nothing left to inherit.</summary>
public readonly record struct ResolvedStyle(
    string FontFamily,
    double FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    string Color,
    double LineSpacing);

public enum SegmentRole
{
    /// <summary>The tag at the start: "AKIRA:", "SFX:", "# ".</summary>
    Label,
    /// <summary>"(whisper)" in a cue.</summary>
    Modifier,
    /// <summary>The words themselves.</summary>
    Body,
    /// <summary>The ** or * around inline emphasis.</summary>
    Marker,
}

/// <summary>A piece of a line and exactly how to draw it.</summary>
public sealed record StyledSegment(TextRange Range, ResolvedStyle Style, SegmentRole Role, bool IsWarning = false);

/// <summary>
/// Decides how every part of a line should look by stacking style layers:
/// theme gray → Default text → element style (e.g. SFX) → character → modifier such as (thought).
/// Each layer only changes what it sets, so with no colors chosen anywhere, everything stays in the theme's grays.
/// </summary>
public static class StyleResolver
{
    private const string FallbackFont = "Segoe UI";
    private const double FallbackSize = 12;

    /// <summary>The bottom layer: a plain, single-spaced font in the given theme color.</summary>
    public static ResolvedStyle Start(string color) => new(FallbackFont, FallbackSize, false, false, false, color, 1.0);

    /// <summary>Stacks layers in order on top of <paramref name="start"/>; later layers win wherever they set a value.</summary>
    public static ResolvedStyle Merge(ResolvedStyle start, params TextStyle?[] layers)
    {
        var result = start;
        foreach (var layer in layers)
        {
            if (layer is null) continue;
            result = result with
            {
                FontFamily = layer.FontFamily ?? result.FontFamily,
                FontSize = layer.FontSize ?? result.FontSize,
                Bold = layer.Bold ?? result.Bold,
                Italic = layer.Italic ?? result.Italic,
                Underline = layer.Underline ?? result.Underline,
                Color = layer.Color ?? result.Color,
                LineSpacing = layer.LineSpacing ?? result.LineSpacing,
            };
        }
        return result;
    }

    /// <summary>"Default text" on its own.</summary>
    public static ResolvedStyle ResolveBase(StyleSheet sheet, ThemePalette palette) =>
        Merge(Start(palette.Text), sheet.Base);

    /// <summary>How a kind of line looks before any character or modifier is involved.</summary>
    public static ResolvedStyle ResolveElement(StyleSheet sheet, ElementType type, ThemePalette palette) =>
        Merge(Start(palette.ToneFor(type)), sheet.Base, sheet.For(type));

    /// <summary>How a character's dialogue looks (without modifiers).</summary>
    public static ResolvedStyle ResolveCharacter(StyleSheet sheet, Character character, ThemePalette palette) =>
        Merge(Start(palette.Text), sheet.Base, sheet.For(ElementType.Dialogue), character.Style);

    /// <summary>The style for the main text of a line.</summary>
    public static ResolvedStyle ResolveBody(StyleSheet sheet, ScriptLine line, ThemePalette palette)
    {
        var layers = new List<TextStyle?> { sheet.Base, sheet.For(line.Type) };
        if (line.Character != null)
            layers.Add(line.Character.Style);
        if (line.Type == ElementType.Dialogue)
            layers.AddRange(sheet.MatchModifiers(line.Modifier).Select(m => m.Style));
        return Merge(Start(palette.ToneFor(line.Type)), [.. layers]);
    }

    /// <summary>Splits a line into styled pieces, ready to be drawn. Later segments override earlier ones.</summary>
    public static List<StyledSegment> Segment(StyleSheet sheet, ScriptLine line, int lineLength, ThemePalette palette)
    {
        var segments = new List<StyledSegment>();
        if (line.Type == ElementType.Blank || lineLength == 0) return segments;

        var body = ResolveBody(sheet, line, palette);

        // 1. Paint the whole line in the body style so spacing and line height are consistent.
        segments.Add(new StyledSegment(new TextRange(0, lineLength), body, SegmentRole.Body));

        // 2. The label ("AKIRA:", "SFX:", "# ").
        if (!line.Label.IsEmpty)
        {
            var label = ResolveLabel(sheet, line, body, palette);
            segments.Add(new StyledSegment(line.Label, label, SegmentRole.Label, IsWarning: line.IsUnknownSpeaker));

            if (!line.ModifierRange.IsEmpty)
            {
                var modifier = label with { Bold = false, Italic = true, Color = palette.Muted };
                segments.Add(new StyledSegment(line.ModifierRange, modifier, SegmentRole.Modifier));
            }
        }

        // 3. Inline **bold** / *italic*, with their markers faded out.
        foreach (var span in line.Inlines)
        {
            var marker = body with { Color = palette.Ghost };
            var emphasis = span.Kind == InlineKind.Bold ? body with { Bold = true } : body with { Italic = true };
            segments.Add(new StyledSegment(span.OpenMarker, marker, SegmentRole.Marker));
            segments.Add(new StyledSegment(span.Content, emphasis, SegmentRole.Body));
            segments.Add(new StyledSegment(span.CloseMarker, marker, SegmentRole.Marker));
        }

        return segments;
    }

    private static ResolvedStyle ResolveLabel(StyleSheet sheet, ScriptLine line, ResolvedStyle body, ThemePalette palette)
    {
        if (line.Type == ElementType.Dialogue)
        {
            // Undefined speakers keep the normal text color; the editor adds a dotted warning underline.
            if (line.IsUnknownSpeaker)
                return body with { Bold = true, Italic = false };

            // The name tag uses the character's font but stays upright and bold, in their name color if they have one.
            var tag = ResolveCharacter(sheet, line.Character!, palette);
            return tag with { Bold = true, Italic = false, Color = line.Character!.NameColor ?? tag.Color };
        }

        // "SFX:", "CAPTION:", "# " — keep them quiet so the content stands out.
        return body with { Bold = false, Italic = false, Underline = false, Color = palette.Faint };
    }
}
