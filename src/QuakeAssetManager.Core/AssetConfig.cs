using System.Text.Json;

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
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
        App = LoadOrCreate(AppFile, () => new AppConfig());
        foreach (var type in Types)
        {
            if (string.IsNullOrWhiteSpace(type.NamesFile)) continue;
            var path = NamesPath(type);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Write(path, AssetTypeDefaults.DefaultNames(type.Id));
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
                var model = JsonSerializer.Deserialize<TypesFileModel>(File.ReadAllText(TypesFile), Json);
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
            types = Migrate(types, defaults);
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
        if (changed)
            SaveTypes(types);
        return types;
    }

    public void SaveTypes(IReadOnlyList<AssetType>? types = null) =>
        Write(TypesFile, new TypesFileModel { ConfigVersion = CurrentVersion, AssetTypes = [.. types ?? Types] });

    /// <summary>2 = version 0.4: Gfx replaces Conbacks; Maps, Lits and Locs removed; new recognition settings.</summary>
    public const int CurrentVersion = 2;

    private static readonly string[] RemovedTypes = ["maps", "lits", "locs"];

    /// <summary>
    /// Upgrade an older asset-types.json. Built-in types get the new definitions (keeping whether they're shown);
    /// types you added yourself are kept as they are.
    /// </summary>
    private List<AssetType> Migrate(List<AssetType> old, List<AssetType> defaults)
    {
        var shown = old.ToDictionary(t => t.Id == "conbacks" ? "gfx" : t.Id, t => t.Visible, StringComparer.OrdinalIgnoreCase);
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
        // Name files whose built-in contents changed, or whose type is gone.
        foreach (var name in new[] { "conbacks", "maps", "lits", "locs", "levelshots", "progs", "crosshairs" })
        {
            try { File.Delete(Path.Combine(Folder, "names", name + ".json")); }
            catch (IOException) { }
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
            return File.Exists(path) ? JsonSerializer.Deserialize<NamesConfig>(File.ReadAllText(path), Json) ?? new NamesConfig() : AssetTypeDefaults.DefaultNames(type.Id);
        }
        catch (JsonException e)
        {
            Problems.Add($"{type.NamesFile} couldn't be read ({e.Message}).");
            return new NamesConfig();
        }
    }

    private T LoadOrCreate<T>(string path, Func<T> create) where T : class
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? create();
        }
        catch (JsonException e)
        {
            Problems.Add($"{Path.GetFileName(path)} couldn't be read ({e.Message}). Using defaults.");
            return create();
        }
        var value = create();
        Write(path, value);
        return value;
    }

    private static void Write<T>(string path, T value)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed class TypesFileModel
    {
        public string About { get; set; } =
            "Asset types managed by Quake Asset Manager. Set visible to show or hide a tab. activeFolderPath is relative to the Quake folder; " +
            "assetsFolderName is the folder inside the Assets folder. Valid names: names/<id>.json plus nameSources " +
            "(mdl-skins, mdl-simple, md3-simple, bsp-items, bsp-items-simple, fragfile, gfx-wad-lumps, map-names, progs-models). " +
            "recognizeBy: names | extension | folder (how a file with no type folder in its path is recognised). Restart the app after editing.";
        public int ConfigVersion { get; set; } = 1;
        public List<AssetType> AssetTypes { get; set; } = [];
    }
}

/// <summary>app.json</summary>
public sealed class AppConfig
{
    public string OnlineSiteName { get; set; } = "gfx.quakeworld.nu";
    public string OnlineSiteUrl { get; set; } = "https://gfx.quakeworld.nu";
}
