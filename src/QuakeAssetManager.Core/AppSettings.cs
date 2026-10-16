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
    /// <summary>Quake folders that have been scanned at least once (full paths).</summary>
    public List<string> ScannedQuakeFolders { get; set; } = [];

    /// <summary>config\settings.json next to the app (see <see cref="AppPaths"/>).</summary>
    public static string DefaultPath => AppPaths.SettingsFile;

    /// <summary>Folders found inside the game folders that were already imported or dismissed, per Quake folder.</summary>
    public Dictionary<string, List<string>> HandledSubfolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
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
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() => new()
    {
        QuakeFolder = QuakeFolder,
        AssetsFolder = AssetsFolder,
        Client = Client,
        Map = Map,
        Language = Language,
        HandledSubfolders = new(HandledSubfolders, StringComparer.OrdinalIgnoreCase),
        ScannedQuakeFolders = [.. ScannedQuakeFolders],
    };
}
