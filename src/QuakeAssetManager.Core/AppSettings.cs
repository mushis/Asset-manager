using System.Text.Json;

namespace QuakeAssetManager.Core;

public sealed class AppSettings
{
    public string QuakeFolder { get; set; } = "";
    public string AssetsFolder { get; set; } = "";
    /// <summary>Client file name, relative to the Quake folder.</summary>
    public string Client { get; set; } = "";
    public string Map { get; set; } = "dm4";
    /// <summary>UI language code: en, fr, es, pt, sv, fi, ru.</summary>
    public string Language { get; set; } = "en";
    /// <summary>Colour theme: rust, slate or parchment.</summary>
    public string Theme { get; set; } = "rust";
    /// <summary>Show the Local/Online switch above the Assets list.</summary>
    public bool ShowOnline { get; set; } = true;
    /// <summary>Show the welcome screen at start (until "Don't show this again" is ticked).</summary>
    public bool ShowWelcome { get; set; } = true;
    /// <summary>Look for a new version on GitHub when the app starts.</summary>
    public bool CheckUpdatesAtStart { get; set; }
    /// <summary>Quake folders that have been scanned at least once (full paths).</summary>
    public List<string> ScannedQuakeFolders { get; set; } = [];

    /// <summary>config\settings.json next to the app (see <see cref="AppPaths"/>).</summary>
    public static string DefaultPath => AppPaths.SettingsFile;

    /// <summary>Folders found inside the game folders that were already imported or dismissed, per Quake folder.</summary>
    public Dictionary<string, List<string>> HandledSubfolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllText(path), PlainJson.Files.AppSettings) is { } settings)
            {
                // Folder keys ignore case, as on Windows; a key that differs only in case is merged into the first.
                var handled = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (folder, names) in settings.HandledSubfolders ?? [])
                    if (!handled.TryAdd(folder, names ?? []))
                        handled[folder].AddRange(names ?? []);
                settings.HandledSubfolders = handled;
                return settings;
            }
        }
        catch (Exception)
        {
            // A broken settings file sends the user back to the setup screen.
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, PlainJson.Files.AppSettings));
    }

    public AppSettings Clone() => new()
    {
        QuakeFolder = QuakeFolder,
        AssetsFolder = AssetsFolder,
        Client = Client,
        Map = Map,
        Language = Language,
        Theme = Theme,
        ShowOnline = ShowOnline,
        ShowWelcome = ShowWelcome,
        CheckUpdatesAtStart = CheckUpdatesAtStart,
        HandledSubfolders = new(HandledSubfolders, StringComparer.OrdinalIgnoreCase),
        ScannedQuakeFolders = [.. ScannedQuakeFolders],
    };
}
