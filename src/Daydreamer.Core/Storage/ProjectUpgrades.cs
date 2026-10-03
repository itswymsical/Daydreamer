using Daydreamer.Core.Models;

namespace Daydreamer.Core.Storage;

/// <summary>Brings project files saved by older versions of Daydreamer up to date when they're opened.</summary>
public static class ProjectUpgrades
{
    public static void Apply(Project project)
    {
        if (project.Version < 2) UpgradeToVersion2(project);
        project.Version = Project.CurrentVersion;
    }

    /// <summary>
    /// Version 1 saved its default grays and accent purple as fixed colors, which would be dark-on-dark in dark mode.
    /// Any color that still exactly matches one of those old defaults becomes "follow the theme".
    /// Colors you picked yourself are left alone.
    /// </summary>
    private static void UpgradeToVersion2(Project project)
    {
        var styles = project.Styles;
        ClearIfDefault(styles.Base, "#202124");
        ClearIfDefault(styles.For(ElementType.Page), "#6741D9");
        ClearIfDefault(styles.For(ElementType.Panel), "#5F6368");
        ClearIfDefault(styles.For(ElementType.Note), "#80868B");
        foreach (var rule in styles.Modifiers.Where(m => m.Keyword.Equals("whisper", StringComparison.OrdinalIgnoreCase)))
            ClearIfDefault(rule.Style, "#80868B");
    }

    private static void ClearIfDefault(TextStyle? style, string oldDefault)
    {
        if (style?.Color != null && style.Color.Equals(oldDefault, StringComparison.OrdinalIgnoreCase))
            style.Color = null;
    }
}
