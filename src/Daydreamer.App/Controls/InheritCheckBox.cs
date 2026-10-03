using System.Collections.ObjectModel;
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

/// <summary>Fonts for the font pickers: the installed ones, plus any the open project brought with it.</summary>
public static class FontList
{
    private static readonly List<string> Installed = Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .Distinct()
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static List<string> _projectFonts = [];

    public static ObservableCollection<string> Names { get; } = new(Installed);

    /// <summary>Adds the project's own fonts to the list (and takes away the last project's).</summary>
    public static void SetProjectFonts(IEnumerable<string> names)
    {
        foreach (string old in _projectFonts) Names.Remove(old);
        _projectFonts = names.Where(n => !Installed.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (string name in _projectFonts)
        {
            int index = 0;
            while (index < Names.Count && StringComparer.CurrentCultureIgnoreCase.Compare(Names[index], name) < 0) index++;
            Names.Insert(index, name);
        }
    }

    public static IReadOnlyList<double> Sizes { get; } = [8, 9, 10, 10.5, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36, 48];

    /// <summary>Common line spacings: single, Google Docs' default, one-and-a-half, double.</summary>
    public static IReadOnlyList<double> LineSpacings { get; } = [1, 1.15, 1.5, 2];

    /// <summary>Space between paragraphs, in points: none, a little, about a blank line, more.</summary>
    public static IReadOnlyList<double> ParagraphSpacings { get; } = [0, 6, 12, 18, 24];
}
