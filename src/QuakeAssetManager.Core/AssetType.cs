using System.Text.Json.Serialization;

namespace QuakeAssetManager.Core;

/// <summary>A list on a website that has assets for a type (shown in that type's Online tab).</summary>
public sealed record OnlineLink(string Label, string Url)
{
    public override string ToString() => Label;
}

/// <summary>
/// The client setting that decides whether a file is used, shown in the preview (docs/cvars-and-assets.md).
/// </summary>
/// <param name="Pattern">Regular expression matched against the file name, extension included, ignoring case.</param>
/// <param name="Kind">used, shown or drawn ("Used when cvar is value"), or chosen ("Chosen with cvar").</param>
public sealed record CvarHint(string Pattern, string Cvar, string Kind = "used", string Value = "1")
{
    private System.Text.RegularExpressions.Regex? _regex;

    public bool Matches(string fileName)
    {
        try
        {
            _regex ??= new System.Text.RegularExpressions.Regex(Pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return _regex.IsMatch(fileName);
        }
        catch (ArgumentException)
        {
            return false; // a bad pattern in the json is skipped
        }
    }
}

/// <summary>
/// An asset type, read from asset-types.json. Everything about it is configuration: where the game loads it
/// from, where its copies live, which file names are valid (names/&lt;id&gt;.json plus name sources computed from
/// the game data), and how dropped files are recognised.
/// </summary>
public sealed class AssetType
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; set; } = "";
    /// <summary>Shown as a tab. Hidden types are still recognised when files are dropped.</summary>
    public bool Visible { get; set; }
    /// <summary>Game folder, relative to the Quake folder, with '/' separators (e.g. "qw/textures/models").</summary>
    public required string ActiveFolderPath { get; init; }
    /// <summary>Folder inside the Assets folder (e.g. "models").</summary>
    public required string AssetsFolderName { get; init; }
    /// <summary>Accepted extensions, in the order the client tries them.</summary>
    public List<string> Extensions { get; set; } = [];
    /// <summary>File with the fixed names and patterns, relative to the config folder (e.g. "names/models.json").</summary>
    public string NamesFile { get; set; } = "";
    /// <summary>Names computed from the game data: mdl-skins, mdl-simple, md3-simple, bsp-items, bsp-items-simple, fragfile, map-names, progs-models.</summary>
    public List<string> NameSources { get; set; } = [];
    /// <summary>Paths inside archives and dropped folders that hold this type, e.g. "textures/models/".</summary>
    public List<string> ArchivePrefixes { get; set; } = [];
    /// <summary>Folder names that hint at this type when a dropped file's name alone can't decide.</summary>
    public List<string> FolderHints { get; set; } = [];
    public List<OnlineLink> Online { get; set; } = [];
    /// <summary>
    /// How a loose file with no type folder in its path is recognised: "names" (the name is on this type's list,
    /// e.g. armor_0 for Models), "extension" (e.g. .mdl for Progs) or "folder" (only its folder can tell, the
    /// default for any-name types such as Crosshairs).
    /// </summary>
    public string RecognizeBy { get; set; } = "folder";
    /// <summary>Client settings that decide whether a file is used; the first one whose pattern matches is shown.</summary>
    public List<CvarHint> Hints { get; set; } = [];
    /// <summary>Notes for people editing the file; not used by the app.</summary>
    public string? Notes { get; set; }

    // ---------- Helpers (not stored) ----------

    [JsonIgnore] public string FolderName => AssetsFolderName;

    /// <summary>Kept for readability where the code used to switch on a fixed enum.</summary>
    [JsonIgnore] public string Kind => Id;

    [JsonIgnore] public string ActiveRelativePath => ActiveFolderPath.Replace('/', Path.DirectorySeparatorChar);

    public string ActiveFolder(string quakeFolder) => Path.Combine([quakeFolder, .. ActiveFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

    public string AssetsFolder(string assetsRoot) => Path.Combine(assetsRoot, AssetsFolderName);

    [JsonIgnore] public string ArchivePrefix => ArchivePrefixes.FirstOrDefault() ?? ActiveFolderPath.Split('/', 2).Last() + "/";

    /// <summary>The client setting that decides whether this file is used, if one is known.</summary>
    public CvarHint? HintFor(string fileName) => Hints.FirstOrDefault(h => h.Matches(fileName));

    public override string ToString() => DisplayName;

    // ---------- The configured types ----------

    private static IReadOnlyList<AssetType> _known = AssetTypeDefaults.Create();

    /// <summary>Every configured type, shown or hidden.</summary>
    public static IReadOnlyList<AssetType> Known => _known;

    /// <summary>The types shown as tabs and managed by the app.</summary>
    public static IReadOnlyList<AssetType> All => _known.Where(t => t.Visible).ToList();

    public static void Configure(IReadOnlyList<AssetType> types) => _known = types;

    public static AssetType? ById(string id) => _known.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static AssetType Get(string id) => ById(id) ?? throw new KeyNotFoundException($"Unknown asset type \"{id}\".");

    /// <summary>The shown type whose archive prefix matches a path inside an archive.</summary>
    public static AssetType? FromArchivePath(string entryPath) => FromPath(entryPath, All);

    public static AssetType? FromPath(string path, IEnumerable<AssetType> types)
    {
        var p = "/" + path.Replace('\\', '/').ToLowerInvariant();
        return types
            .SelectMany(t => t.ArchivePrefixes.Select(prefix => (Type: t, Prefix: "/" + prefix.ToLowerInvariant())))
            .OrderByDescending(x => x.Prefix.Length)
            .FirstOrDefault(x => p.StartsWith(x.Prefix, StringComparison.Ordinal)).Type;
    }
}
