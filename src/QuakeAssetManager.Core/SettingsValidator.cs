namespace QuakeAssetManager.Core;

public sealed record SettingsValidation(
    string? QuakeFolderError,
    string? AssetsFolderError,
    string? ClientError,
    string? MapError,
    IReadOnlyList<AssetType> MissingActiveFolders)
{
    public bool IsValid => QuakeFolderError is null && AssetsFolderError is null && ClientError is null && MapError is null;

    public int ErrorCount =>
        new[] { QuakeFolderError, AssetsFolderError, ClientError, MapError }.Count(e => e is not null);
}

public static class SettingsValidator
{
    public static SettingsValidation Validate(AppSettings s)
    {
        string? quakeError = null;
        var missing = new List<AssetType>();
        if (string.IsNullOrWhiteSpace(s.QuakeFolder))
            quakeError = "Choose your Quake folder.";
        else if (!Directory.Exists(s.QuakeFolder))
            quakeError = "This folder doesn't exist.";
        else if (!Directory.Exists(Path.Combine(s.QuakeFolder, "id1")) || !Directory.Exists(Path.Combine(s.QuakeFolder, "qw")))
            quakeError = "This isn't a Quake folder. It must contain the id1 and qw folders.";
        else
            missing.AddRange(AssetType.All.Where(t => !Directory.Exists(t.ActiveFolder(s.QuakeFolder))));

        string? assetsError = null;
        if (string.IsNullOrWhiteSpace(s.AssetsFolder))
            assetsError = "Choose where to keep your asset copies.";
        else if (!Path.IsPathFullyQualified(s.AssetsFolder))
            assetsError = "Use a full path.";
        else if (quakeError is null && IsInside(s.AssetsFolder, Path.Combine(s.QuakeFolder, "qw", "textures")))
            assetsError = "The Local assets folder can't be inside qw\\textures, or the game would load it.";

        string? clientError = null;
        if (string.IsNullOrWhiteSpace(s.Client))
            clientError = "Pick the program to start when you press Test.";
        else if (quakeError is null && !File.Exists(Path.Combine(s.QuakeFolder, s.Client)))
            clientError = "This program isn't in the Quake folder any more.";

        string? mapError = string.IsNullOrWhiteSpace(s.Map) ? "Type a map name, e.g. dm4." :
            s.Map.Any(char.IsWhiteSpace) ? "A map name can't contain spaces." : null;

        return new SettingsValidation(quakeError, assetsError, clientError, mapError, missing);
    }

    /// <summary>Programs in the Quake root folder that can be started as the client.</summary>
    public static IReadOnlyList<string> FindClients(string quakeFolder)
    {
        if (!Directory.Exists(quakeFolder))
            return [];
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(quakeFolder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                continue;
            if (OperatingSystem.IsWindows())
            {
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    result.Add(name);
            }
            else
            {
                try
                {
                    var mode = File.GetUnixFileMode(file);
                    if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                        result.Add(name);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        // Prefer ezQuake builds at the top of the list.
        return result
            .OrderByDescending(n => n.Equals(OperatingSystem.IsWindows() ? "ezquake.exe" : "ezquake-linux-x86_64", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(n => n.Contains("ezquake", StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return p.StartsWith(f, cmp);
    }
}
