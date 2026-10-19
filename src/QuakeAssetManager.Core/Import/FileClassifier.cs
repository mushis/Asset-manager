namespace QuakeAssetManager.Core.Import;

/// <param name="Type">The asset type the file goes to (shown or hidden), or null.</param>
/// <param name="RelativePath">Where it goes inside the Assets folder chosen for the type.</param>
/// <param name="OtherFolder">A known folder the app doesn't manage (sound, maps, map textures...).</param>
/// <param name="Reason">Why the file was left out.</param>
/// <param name="Invalid">It's in a type's folder but the game doesn't load that name; it's copied, marked invalid.</param>
/// <param name="NeedsChoice">No folder or name decides: the user picks the type (or skips).</param>
/// <param name="Suggested">For <paramref name="NeedsChoice"/>: the one type the name points to, if any.</param>
/// <param name="OtherPath">For <paramref name="OtherFolder"/>: where it goes inside assets/other (e.g. sound/weapons/coilgun.wav).</param>
public sealed record Classification(
    AssetType? Type, string RelativePath, string? OtherFolder, string? Reason,
    bool Invalid = false, bool NeedsChoice = false, AssetType? Suggested = null, string? OtherPath = null);

/// <summary>
/// Decides where a file goes (docs/version-0.4-plan.md, sections 10 and 11):
/// 1. a known folder in its path decides (type folders, and folders the app doesn't manage);
/// 2. inside a pak/pk3, any other folder is asked about (with a suggestion from the names);
/// 3. a loose file's name decides only for types with fixed name lists (or Progs by model extension);
/// 4. otherwise the user picks.
/// </summary>
public static class FileClassifier
{
    /// <summary>Known qw folders the app doesn't manage (see docs/qw-folder-names.md).</summary>
    public static readonly string[] NotManagedFolders =
    [
        "textures/scrollbars", "textures/sprites", "textures/scoreboard", "textures",
        "maps", "lits", "locs", "sound", "radars", "configs", "cfg",
    ];

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase) { "thumbs.db", "desktop.ini", ".ds_store" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".nfo", ".htm", ".html", ".doc", ".pdf", ".url" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".gz", ".tgz", ".tar", ".pk3", ".pak" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".tga", ".png", ".jpg", ".jpeg", ".pcx", ".bmp", ".link", ".lmp" };

    private static readonly Dictionary<string, string> OtherByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wav"] = "sound", [".ogg"] = "sound", [".cfg"] = "configs",
        [".bsp"] = "maps", [".lit"] = "lits", [".loc"] = "locs",
    };

    public static Classification Classify(string path, AssetManager manager, bool fromArchive = false)
    {
        var p = path.Replace('\\', '/');
        var lower = "/" + p.ToLowerInvariant();
        var name = p[(p.LastIndexOf('/') + 1)..];
        var ext = Path.GetExtension(name);

        if (JunkNames.Contains(name))
            return new Classification(null, name, null, "system file");

        // 1. The longest known folder in the path decides.
        var typeMatch = AssetType.Known
            .SelectMany(t => t.ArchivePrefixes.Select(prefix => (Type: t, Marker: "/" + prefix.ToLowerInvariant().Trim('/') + "/")))
            .Select(x => (x.Type, x.Marker, At: lower.LastIndexOf(x.Marker, StringComparison.Ordinal)))
            .Where(x => x.At >= 0)
            .OrderByDescending(x => x.Marker.Length)
            .FirstOrDefault();
        var otherMatch = NotManagedFolders
            .Select(f => "/" + f + "/")
            .Where(f => lower.Contains(f, StringComparison.Ordinal))
            .OrderByDescending(f => f.Length)
            .FirstOrDefault();
        if (typeMatch.Type is not null && (otherMatch is null || typeMatch.Marker.Length >= otherMatch.Length))
        {
            var relative = p[(typeMatch.At + typeMatch.Marker.Length - 1)..];
            var names = Names(manager, typeMatch.Type);
            if (names?.IsValidFileName(name) == true)
                return new Classification(typeMatch.Type, relative, null, null);
            if (ArchiveExtensions.Contains(ext))
                return new Classification(null, name, null, "archive inside a type folder (not opened)");
            if (DocumentExtensions.Contains(ext))
                return new Classification(null, name, null, "document");
            // A renamed alternative (e.g. particles/bps.png): copied, marked invalid, so it can be renamed later.
            return new Classification(typeMatch.Type, relative, null, null, Invalid: true);
        }
        if (otherMatch is not null)
        {
            // Kept in assets/other with the same path from that folder on, e.g. sound/weapons/coilgun.wav.
            var at = lower.LastIndexOf(otherMatch, StringComparison.Ordinal);
            return new Classification(null, name, otherMatch.Trim('/'), null, OtherPath: p[at..]);
        }

        // 2/3. No known folder: what does the name say?
        var suggested = ByName(name, manager);

        if (fromArchive)
            return new Classification(null, name, null, null, NeedsChoice: true, Suggested: suggested.Count == 1 ? suggested[0] : null);

        if (suggested.Count == 1)
            return new Classification(suggested[0], name, null, null);
        if (suggested.Count > 1)
            return new Classification(null, name, null, null, NeedsChoice: true);
        if (ImageExtensions.Contains(ext))
            return new Classification(null, name, null, null, NeedsChoice: true);

        // 4. Not something the app manages.
        if (OtherByExtension.TryGetValue(ext, out var byExt))
            return new Classification(null, name, byExt, null, OtherPath: $"{byExt}/{name}");
        return new Classification(null, name, null, "unsupported file type");
    }

    /// <summary>Types the name alone points to: fixed name lists, or Progs by model extension.</summary>
    public static List<AssetType> ByName(string name, AssetManager manager)
    {
        var ext = Path.GetExtension(name);
        return AssetType.Known.Where(t =>
            (t.RecognizeBy.Equals("names", StringComparison.OrdinalIgnoreCase) && Names(manager, t)?.IsSpecificName(name) == true) ||
            (t.RecognizeBy.Equals("extension", StringComparison.OrdinalIgnoreCase) && !ImageExtensions.Contains(ext) &&
             t.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    private static ValidNameSet? Names(AssetManager manager, AssetType type) =>
        manager.ValidNames.TryGetValue(type.Id, out var set) ? set : null;
}
