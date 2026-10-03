using System.Windows;
using Daydreamer.App.Shell;
using Daydreamer.Core.Formatting;
using Microsoft.Win32;

namespace Daydreamer.App.Theming;

public enum AppTheme { Light, Dark }

/// <summary>
/// Switches the whole app between light and dark mode by swapping two resource dictionaries:
/// WPF's Fluent control styles and Daydreamer's own colors. Everything that uses DynamicResource
/// recolors itself; code that draws its own colors (the editor) listens to <see cref="Changed"/>.
/// </summary>
public static class ThemeManager
{
    private const int FluentIndex = 0;
    private const int ColorsIndex = 1;

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static bool IsDark => Current == AppTheme.Dark;

    /// <summary>The grays the editor uses for text that has no color of its own.</summary>
    public static ThemePalette Palette => IsDark ? ThemePalette.Dark : ThemePalette.Light;

    public static event EventHandler? Changed;

    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[FluentIndex] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{theme}.xaml"),
        };
        dictionaries[ColorsIndex] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml"),
        };

        Current = theme;
        foreach (Window window in Application.Current.Windows)
            WindowEffects.UseDarkFrame(window, IsDark);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>"Light", "Dark", or null/anything else to follow the Windows setting.</summary>
    public static AppTheme FromSetting(string? setting) => setting?.ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => WindowsTheme(),
    };

    /// <summary>Reads Settings → Personalization → Colors → "Choose your app mode".</summary>
    public static AppTheme WindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int and 0 ? AppTheme.Dark : AppTheme.Light;
    }
}
