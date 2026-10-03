using System.Windows.Controls;
using System.Windows.Media;

namespace Daydreamer.App.Controls;

/// <summary>
/// A three-state check box for style options: [–] inherit → [✓] on → [ ] off → back to inherit.
/// (The standard check box cycles in a less intuitive order.)
/// </summary>
public sealed class InheritCheckBox : CheckBox
{
    public InheritCheckBox()
    {
        IsThreeState = true;
        ToolTip = "Click to cycle: inherit (–) → on (✓) → off (empty)";
    }

    protected override void OnToggle() => IsChecked = IsChecked switch
    {
        null => true,
        true => false,
        false => null,
    };
}

/// <summary>Installed fonts, for the font picker.</summary>
public static class FontList
{
    public static IReadOnlyList<string> Names { get; } = Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .Distinct()
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static IReadOnlyList<double> Sizes { get; } = [8, 9, 10, 10.5, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36, 48];

    /// <summary>Common line spacings: single, Google Docs' default, one-and-a-half, double.</summary>
    public static IReadOnlyList<double> LineSpacings { get; } = [1, 1.15, 1.5, 2];
}
