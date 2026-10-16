namespace QuakeAssetManager.Core;

/// <summary>
/// Where the app keeps its files. Portable by default: config\ and cache\ next to the executable, so the whole app
/// lives in one folder (e.g. C:\QuakeAssets). When that folder can't be written (Program Files, /usr/bin), it falls
/// back to %AppData%\QuakeAssetManager or ~/.config/QuakeAssetManager.
/// </summary>
public static class AppPaths
{
    private static string? _configFolder;

    /// <summary>QAM_SETTINGS (a settings.json path) overrides everything; used for tests and snapshots.</summary>
    public static string ConfigFolder => _configFolder ??= ResolveConfigFolder();

    public static string SettingsFile => Environment.GetEnvironmentVariable("QAM_SETTINGS") is { Length: > 0 } custom
        ? custom
        : Path.Combine(ConfigFolder, "settings.json");

    public static string CacheFolder => Path.Combine(IsPortable ? AppContext.BaseDirectory : UserFolder(), "cache");

    public static bool IsPortable { get; private set; }

    /// <summary>The per-user folder used before 0.4 (and as the fallback).</summary>
    public static string UserFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuakeAssetManager");

    private static string ResolveConfigFolder()
    {
        if (Environment.GetEnvironmentVariable("QAM_SETTINGS") is { Length: > 0 } custom)
            return Path.GetDirectoryName(Path.GetFullPath(custom))!;
        var portable = Path.Combine(AppContext.BaseDirectory, "config");
        if (CanWrite(portable))
        {
            IsPortable = true;
            MigrateInto(portable);
            return portable;
        }
        return UserFolder();
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// First start of 0.4: copy settings and configuration from where 0.3 kept them. That is %AppData%, or, when
    /// the app was started from a packaged app (e.g. the Claude desktop app), the package's redirected AppData copy.
    /// </summary>
    private static void MigrateInto(string portable)
    {
        if (File.Exists(Path.Combine(portable, "settings.json")))
            return;
        var candidates = new List<string> { UserFolder() };
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (OperatingSystem.IsWindows() && Directory.Exists(packages))
        {
            try
            {
                candidates.AddRange(Directory.EnumerateDirectories(packages)
                    .Select(p => Path.Combine(p, "LocalCache", "Roaming", "QuakeAssetManager")));
            }
            catch (UnauthorizedAccessException) { }
        }
        var source = candidates.Where(c => File.Exists(Path.Combine(c, "settings.json")))
            .OrderByDescending(c => File.GetLastWriteTimeUtc(Path.Combine(c, "settings.json")))
            .FirstOrDefault();
        if (source is null)
            return;
        try
        {
            // Only the configuration itself: settings, asset types, app settings and the names/ lists.
            var files = new[] { "settings.json", "asset-types.json", "app.json" }
                .Select(f => Path.Combine(source, f)).Where(File.Exists)
                .Concat(Directory.Exists(Path.Combine(source, "names"))
                    ? Directory.EnumerateFiles(Path.Combine(source, "names"), "*.json")
                    : []);
            foreach (var file in files)
            {
                var target = Path.Combine(portable, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                    File.Copy(file, target);
            }
            MigratedFrom = source;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Set when settings were copied from an older location on this start.</summary>
    public static string? MigratedFrom { get; private set; }
}
