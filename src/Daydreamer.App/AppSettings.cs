using System.IO;
using System.Text.Json;

namespace Daydreamer.App;

/// <summary>
/// Small per-user settings (where the library lives, what was open last),
/// stored in %AppData%\Daydreamer\settings.json.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Daydreamer", "settings.json");

    /// <summary>
    /// Where projects are stored. Prefers OneDrive\Documents\Daydreamer so your stories are
    /// backed up to the cloud automatically (like Google Docs); otherwise Documents\Daydreamer.
    /// </summary>
    public string LibraryPath { get; set; } = DefaultLibraryPath();

    private static string DefaultLibraryPath()
    {
        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        string documents = !string.IsNullOrEmpty(oneDrive) && Directory.Exists(Path.Combine(oneDrive, "Documents"))
            ? Path.Combine(oneDrive, "Documents")
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "Daydreamer");
    }

    public string? LastProject { get; set; }
    public string? LastDocument { get; set; }

    /// <summary>"Light" or "Dark". Empty means follow the Windows app mode.</summary>
    public string? Theme { get; set; }

    /// <summary>Ambient mode (drifting lights). Empty means on, unless Windows animations are turned off.</summary>
    public bool? Ambient { get; set; }

    /// <summary>Where the window was when the app last closed.</summary>
    public WindowPlacement? Window { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file shouldn't stop the app from starting; fall back to defaults.
        }
        return new AppSettings();
    }

    /// <summary>The window's size and position (in WPF's device-independent units) and whether it was maximized.</summary>
    public sealed class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // Not critical — worst case the app forgets which document was open.
        }
    }
}
