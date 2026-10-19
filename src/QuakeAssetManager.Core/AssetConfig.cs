using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace QuakeAssetManager.Core;

/// <summary>
/// The app's configuration files, all in one folder (next to settings.json):
/// <list type="bullet">
/// <item>settings.json: Quake folder, Assets folder, client, map</item>
/// <item>asset-types.json: every asset type, its folders, extensions, name sources and whether it's shown</item>
/// <item>names/&lt;id&gt;.json: fixed file names and patterns per asset type</item>
/// <item>app.json: online site address</item>
/// </list>
/// Missing files are written with defaults; asset types added in a newer version are appended.
/// </summary>
public sealed class AssetConfig
{
    public string Folder { get; }
    public List<AssetType> Types { get; private set; } = [];
    public AppConfig App { get; private set; } = new();
    public List<string> Problems { get; } = [];

    public string TypesFile => Path.Combine(Folder, "asset-types.json");
    public string AppFile => Path.Combine(Folder, "app.json");

    public AssetConfig(string folder) => Folder = folder;

    /// <summary>The folder that holds settings.json and the configuration.</summary>
    public static string DefaultFolder => Path.GetDirectoryName(AppSettings.DefaultPath)!;

    public static AssetConfig Load(string? folder = null)
    {
        var config = new AssetConfig(folder ?? DefaultFolder);
        config.Reload();
        return config;
    }

    public void Reload()
    {
        Problems.Clear();
        Directory.CreateDirectory(Folder);
        Types = LoadTypes();
        App = LoadOrCreate(AppFile, () => new AppConfig(), ConfigJson.Files.AppConfig);
        foreach (var type in Types)
        {
            if (string.IsNullOrWhiteSpace(type.NamesFile)) continue;
            var path = NamesPath(type);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Write(path, AssetTypeDefaults.DefaultNames(type.Id), ConfigJson.Files.NamesConfig);
            }
        }
        AssetType.Configure(Types);
    }

    private List<AssetType> LoadTypes()
    {
        var defaults = AssetTypeDefaults.Create();
        List<AssetType>? types = null;
        var version = CurrentVersion;
        if (File.Exists(TypesFile))
        {
            try
            {
                var model = JsonSerializer.Deserialize(File.ReadAllText(TypesFile), ConfigJson.Files.TypesFileModel);
                types = model?.AssetTypes;
                version = model?.ConfigVersion ?? 1;
            }
            catch (JsonException e)
            {
                Problems.Add($"asset-types.json couldn't be read ({e.Message}). Using the built-in asset types.");
                return defaults;
            }
        }
        types ??= [];
        var changed = types.Count == 0;
        if (types.Count > 0 && version < CurrentVersion)
        {
            types = Migrate(types, defaults, version);
            changed = true;
        }
        foreach (var d in defaults.Where(d => types.All(t => !t.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase))))
        {
            types.Add(d);
            changed = true;
        }
        var duplicate = types.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            Problems.Add($"asset-types.json lists \"{duplicate.Key}\" more than once; the first one is used.");
        types = types.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var t in types)
            t.Hints ??= [];
        if (changed)
            SaveTypes(types);
        return types;
    }

    public void SaveTypes(IReadOnlyList<AssetType>? types = null) =>
        Write(TypesFile, new TypesFileModel { ConfigVersion = CurrentVersion, AssetTypes = [.. types ?? Types] }, ConfigJson.Files.TypesFileModel);

    /// <summary>
    /// 2 = version 0.4: Gfx replaces Conbacks; Maps, Lits and Locs removed; new recognition settings.
    /// 3 = version 0.5: cvar hints; Sprays and Emoticons added.
    /// </summary>
    public const int CurrentVersion = 3;

    private static readonly string[] RemovedTypes = ["maps", "lits", "locs"];

    /// <summary>
    /// Upgrade an older asset-types.json. Built-in types get the new definitions (keeping whether they're shown);
    /// types you added yourself are kept as they are.
    /// </summary>
    private List<AssetType> Migrate(List<AssetType> old, List<AssetType> defaults, int fromVersion)
    {
        var shown = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in old)
            shown.TryAdd(t.Id.Equals("conbacks", StringComparison.OrdinalIgnoreCase) ? "gfx" : t.Id, t.Visible);
        var result = new List<AssetType>();
        foreach (var t in old)
        {
            var id = t.Id.Equals("conbacks", StringComparison.OrdinalIgnoreCase) ? "gfx" : t.Id;
            if (RemovedTypes.Contains(id, StringComparer.OrdinalIgnoreCase) || result.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (defaults.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } d)
            {
                d.Visible = shown.GetValueOrDefault(id, d.Visible);
                result.Add(d);
            }
            else
            {
                result.Add(t);
            }
        }
        if (fromVersion < 2)
        {
            // Name files whose built-in contents changed in 0.4, or whose type is gone.
            foreach (var name in new[] { "conbacks", "maps", "lits", "locs", "levelshots", "progs", "crosshairs" })
            {
                try { File.Delete(Path.Combine(Folder, "names", name + ".json")); }
                catch (IOException) { }
            }
        }
        return result;
    }

    public string NamesPath(AssetType type) => Path.Combine(Folder, type.NamesFile.Replace('/', Path.DirectorySeparatorChar));

    public NamesConfig LoadNames(AssetType type)
    {
        if (string.IsNullOrWhiteSpace(type.NamesFile)) return new NamesConfig();
        try
        {
            var path = NamesPath(type);
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ConfigJson.Files.NamesConfig) ?? new NamesConfig()
                : AssetTypeDefaults.DefaultNames(type.Id);
        }
        catch (JsonException e)
        {
            Problems.Add($"{type.NamesFile} couldn't be read ({e.Message}).");
            return new NamesConfig();
        }
    }

    /// <summary>Write names/&lt;id&gt;.json (e.g. after "Mark as valid"). Throws when it can't be written.</summary>
    public void SaveNames(AssetType type, NamesConfig names)
    {
        if (string.IsNullOrWhiteSpace(type.NamesFile))
            throw new InvalidOperationException($"{type.DisplayName} has no names file in asset-types.json.");
        var path = NamesPath(type);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(names, ConfigJson.Files.NamesConfig));
    }

    private T LoadOrCreate<T>(string path, Func<T> create, JsonTypeInfo<T> info) where T : class
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), info) ?? create();
        }
        catch (JsonException e)
        {
            Problems.Add($"{Path.GetFileName(path)} couldn't be read ({e.Message}). Using defaults.");
            return create();
        }
        var value = create();
        Write(path, value, info);
        return value;
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> info)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, info));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>The contents of asset-types.json.</summary>
internal sealed class TypesFileModel
{
    public string About { get; set; } =
        "Asset types managed by Quake Asset Manager. Set visible to show or hide a tab. activeFolderPath is relative to the Quake folder; " +
        "assetsFolderName is the folder inside the Assets folder. Valid names: names/<id>.json plus nameSources " +
        "(mdl-skins, mdl-simple, md3-simple, bsp-items, bsp-items-simple, fragfile, gfx-wad-lumps, map-names, progs-models). " +
        "recognizeBy: names | extension | folder (how a file with no type folder in its path is recognised). " +
        "hints: the client setting shown in the preview for files matching a pattern (kind: used, shown, drawn or chosen). " +
        "Restart the app after editing.";
    public int ConfigVersion { get; set; } = 1;
    public List<AssetType> AssetTypes { get; set; } = [];
}

/// <summary>app.json</summary>
public sealed class AppConfig
{
    public string OnlineSiteName { get; set; } = "gfx.quakeworld.nu";
    public string OnlineSiteUrl { get; set; } = "https://gfx.quakeworld.nu";
}
