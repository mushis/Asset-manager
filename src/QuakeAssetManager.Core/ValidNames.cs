using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Formats;
using QuakeAssetManager.Core.Imaging;

namespace QuakeAssetManager.Core;

/// <summary>
/// What ezQuake can load from the game data: model skin counts, item-box texture names and the palette.
/// Read from loose files and pak/pk3 archives in id1, qw and ezquake. Nothing is written.
/// </summary>
public sealed partial class GameData
{
    public static readonly string[] GameDirs = ["id1", "qw", "ezquake"];

    /// <summary>Model base name (e.g. "armor") → frames per skin.</summary>
    public Dictionary<string, IReadOnlyList<int>> MdlSkins { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Md3Models { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Sprites { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Map names (maps/*.bsp other than the b_ item boxes).</summary>
    public HashSet<string> MapNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Lump names in id1's gfx.wad (HUD pictures, including ones ezQuake itself doesn't ask for).</summary>
    public HashSet<string> WadLumps { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Item box base name (e.g. "b_bh100") → texture names inside it.</summary>
    public Dictionary<string, IReadOnlyList<string>> BrushModels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public QuakePalette? Palette { get; private set; }
    /// <summary>Frag tracker image names from WEAPON_CLASS lines in fragfile.dat (the backslash-delimited image tokens).</summary>
    public HashSet<string> TrackerImages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static GameData Load(string quakeFolder)
    {
        var data = new GameData();
        foreach (var dir in GameDirs)
        {
            var gameDir = Path.Combine(quakeFolder, dir);
            if (!Directory.Exists(gameDir))
                continue;

            foreach (var archivePath in Directory.EnumerateFiles(gameDir).Where(GameArchive.IsArchive).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var archive = GameArchive.Open(archivePath);
                    foreach (var entry in archive.Entries)
                        data.Consider(entry.Path, () => archive.ReadEntry(entry));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // Skip unreadable archives.
                }
            }

            var fragfile = Path.Combine(gameDir, "fragfile.dat");
            if (File.Exists(fragfile))
                data.Consider("fragfile.dat", () => File.ReadAllBytes(fragfile));

            foreach (var sub in new[] { "progs", "maps", "gfx" })
            {
                var folder = Path.Combine(gameDir, sub);
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(gameDir, file).Replace('\\', '/');
                    data.Consider(relative, () => File.ReadAllBytes(file));
                }
            }
        }
        return data;
    }

    private void Consider(string path, Func<byte[]> read)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(name);
        try
        {
            if (ext == ".mdl")
            {
                var info = MdlInfo.TryRead(read());
                if (info is not null)
                    MdlSkins[baseName] = info.SkinFrames;
            }
            else if (ext == ".md3")
            {
                Md3Models.Add(baseName);
            }
            else if (ext == ".spr")
            {
                Sprites.Add(baseName);
            }
            else if (ext == ".bsp" && !baseName.StartsWith("b_", StringComparison.OrdinalIgnoreCase) &&
                     path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            {
                MapNames.Add(baseName);
            }
            else if (ext == ".bsp" && baseName.StartsWith("b_", StringComparison.OrdinalIgnoreCase))
            {
                var names = BspInfo.ReadTextureNames(read());
                if (names.Count > 0)
                    BrushModels[baseName] = names;
            }
            else if (name.Equals("gfx.wad", StringComparison.OrdinalIgnoreCase))
            {
                if (WadFile.TryParse(read(), out var wad) && wad is not null)
                    foreach (var lump in wad.Lumps.Where(l => l.Type is WadFile.TypeQPic or WadFile.TypeMipTex || l.Name == "conchars"))
                        WadLumps.Add(lump.Name);
            }
            else if (name.Equals("fragfile.dat", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var image in FragfileImages(System.Text.Encoding.Latin1.GetString(read())))
                    TrackerImages.Add(image);
            }
            else if (Palette is null && path.Equals("gfx/palette.lmp", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = read();
                if (bytes.Length >= 768)
                    Palette = new QuakePalette(bytes);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            // Skip broken files.
        }
    }
}

public sealed partial class GameData
{
    /// <summary>
    /// Image names in "#DEFINE WEAPON_CLASS KEY "name" "short" "\img\ \img2"" lines (fragstats.c:309-331).
    /// The tracker loads each as textures/tracker/&lt;img&gt; (vx_tracker.c:1453).
    /// </summary>
    public static IEnumerable<string> FragfileImages(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            if (!line.StartsWith("#DEFINE", StringComparison.OrdinalIgnoreCase))
                continue;
            var args = System.Text.RegularExpressions.Regex.Matches(line, "\"([^\"]*)\"|(\\S+)")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
            if (args.Count < 6 || !(args[1].Equals("WEAPON_CLASS", StringComparison.OrdinalIgnoreCase) || args[1].Equals("WC", StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(args[5], @"\\([^\\]+)\\"))
                yield return m.Groups[1].Value.Trim();
        }
    }
}

/// <summary>The file names ezQuake looks up in one asset type folder.</summary>
public sealed class ValidNameSet
{
    /// <summary>The image extensions ezQuake tries, in order, plus .link redirects.</summary>
    public static readonly string[] Extensions = [".tga", ".png", ".jpg", ".pcx", ".link"];

    private readonly HashSet<string> _baseNames;
    private readonly List<System.Text.RegularExpressions.Regex> _patterns;
    private readonly List<string> _extensions;

    public AssetType Type { get; }
    public IReadOnlyCollection<string> BaseNames => _baseNames;
    /// <summary>True when the type accepts any name (a ".+" pattern), e.g. crosshairs or skins.</summary>
    public bool AcceptsAnyName { get; }

    /// <summary>On Linux file names are case sensitive, so the case must match what the game asks for.</summary>
    public static StringComparer NameComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public ValidNameSet(AssetType type, IEnumerable<string> baseNames, IEnumerable<string>? patterns = null)
    {
        Type = type;
        _baseNames = new HashSet<string>(baseNames, NameComparer);
        _extensions = type.Extensions.Count > 0 ? type.Extensions : [.. Extensions];
        var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                      (OperatingSystem.IsWindows() ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : 0);
        _patterns = [];
        foreach (var p in patterns ?? [])
        {
            try { _patterns.Add(new System.Text.RegularExpressions.Regex("^(?:" + p + ")$", options)); }
            catch (ArgumentException) { /* a bad pattern in the json is skipped */ }
        }
        AcceptsAnyName = _patterns.Any(r => r.IsMatch("anything_at_all"));
    }

    public bool HasValidExtension(string fileName) => _extensions.Contains(Path.GetExtension(fileName), NameComparer);

    /// <summary>The client loads this file from the type's folder.</summary>
    public bool IsValidFileName(string fileName)
    {
        if (!HasValidExtension(fileName))
            return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return _baseNames.Contains(stem) || _patterns.Any(r => r.IsMatch(stem));
    }

    /// <summary>The name is listed or computed for this type (not just allowed by a catch-all pattern).</summary>
    public bool IsSpecificName(string fileName)
    {
        if (!HasValidExtension(fileName))
            return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return _baseNames.Contains(stem) || (!AcceptsAnyName && _patterns.Any(r => r.IsMatch(stem)));
    }

    /// <summary>Fixed names and patterns from names/&lt;id&gt;.json, plus the type's name sources computed from the game data.</summary>
    public static ValidNameSet Build(AssetType type, GameData data, NamesConfig? names = null)
    {
        names ??= AssetTypeDefaults.DefaultNames(type.Id);
        var all = new List<string>(names.Names);
        foreach (var source in type.NameSources)
        {
            all.AddRange(source.ToLowerInvariant() switch
            {
                "mdl-skins" => ModelSkinNames(data),
                "mdl-simple" => ModelSimpleNames(data),
                "md3-simple" => Md3SimpleNames(data),
                "bsp-items" => BModelNames(data),
                "bsp-items-simple" => data.BrushModels.Keys.Select(b => $"simple_{b.ToLowerInvariant()}_0"),
                "fragfile" => data.TrackerImages.Select(n => n.ToLowerInvariant()),
                "gfx-wad-lumps" => data.WadLumps.Where(n => n != "conchars").Select(n => n.ToLowerInvariant()),
                "map-names" => data.MapNames,
                "progs-models" => data.MdlSkins.Keys.Concat(data.Md3Models).Concat(data.Sprites),
                _ => [],
            });
        }
        return new ValidNameSet(type, all.Distinct(), names.Patterns);
    }

    private static IEnumerable<string> ModelSkinNames(GameData data)
    {
        foreach (var (model, skins) in data.MdlSkins)
        {
            var name = model.ToLowerInvariant();
            for (var s = 0; s < skins.Count; s++)
            {
                yield return $"{name}_{s}";
                yield return $"{name}_{s}_luma";
                if (skins[s] > 1)
                {
                    for (var f = 0; f < skins[s]; f++)
                    {
                        yield return $"{name}_{s}_{f}";
                        yield return $"{name}_{s}_{f}_luma";
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ModelSimpleNames(GameData data)
    {
        foreach (var (model, skins) in data.MdlSkins)
            for (var s = 0; s < skins.Count && s < 5; s++)
                yield return $"simple_{model.ToLowerInvariant()}_{s}";
    }

    private static IEnumerable<string> Md3SimpleNames(GameData data)
    {
        foreach (var md3 in data.Md3Models)
            for (var s = 0; s < 5; s++)
                yield return $"simple_{md3.ToLowerInvariant()}_{s}";
    }

    private static IEnumerable<string> BModelNames(GameData data)
    {
        foreach (var (_, textures) in data.BrushModels)
        {
            foreach (var texture in textures)
            {
                var name = texture.Replace('*', '#');
                yield return name;
                if (!texture.StartsWith('*'))
                    yield return name + "_luma";
            }
        }
    }

    /// <summary>Images ezQuake ships in ezquake.pk3 textures/tracker, plus any named in fragfile.dat.</summary>
    public static readonly string[] DefaultTrackerNames =
    [
        "axe", "coil", "discharge", "drown", "fall", "gl", "lava", "lg", "ng", "q", "rail",
        "rl", "sg", "slime", "sng", "squish", "ssg", "stomp", "tele", "tkills", "trap",
    ];

    /// <summary>Lump names ezQuake requests from textures/wad (hardcoded in sbar.c, cl_screen.c and r_draw.c).</summary>
    public static IEnumerable<string> WadNames()
    {
        for (var i = 0; i < 10; i++)
        {
            yield return $"num_{i}";
            yield return $"anum_{i}";
        }
        foreach (var n in new[] { "num_minus", "anum_minus", "num_colon", "num_slash" })
            yield return n;

        string[] weapons = ["shotgun", "sshotgun", "nailgun", "snailgun", "rlaunch", "srlaunch", "lightng"];
        string[] prefixes = ["inv_", "inv2_", "inva1_", "inva2_", "inva3_", "inva4_", "inva5_"];
        foreach (var p in prefixes)
        foreach (var w in weapons)
            yield return p + w;

        string[] items =
        [
            "sb_shells", "sb_nails", "sb_rocket", "sb_cells",
            "sb_armor1", "sb_armor2", "sb_armor3",
            "sb_key1", "sb_key2", "sb_invis", "sb_invuln", "sb_suit", "sb_quad",
            "sb_sigil1", "sb_sigil2", "sb_sigil3", "sb_sigil4",
            "face1", "face2", "face3", "face4", "face5",
            "face_p1", "face_p2", "face_p3", "face_p4", "face_p5",
            "face_invis", "face_invul2", "face_inv2", "face_quad",
            "sbar", "ibar", "scorebar",
            "ram", "net", "turtle", "disc", "backtile",
        ];
        foreach (var i in items)
            yield return i;
    }

    /// <summary>Suggest the closest valid file name for an invalid one, keeping a valid extension.</summary>
    public string SuggestName(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            ext = _extensions.Contains(".png") ? ".png" : _extensions.FirstOrDefault() ?? ext;
        var stem = Normalize(Path.GetFileNameWithoutExtension(fileName));
        if (_baseNames.Count == 0 || AcceptsAnyName)
            return stem + ext;
        if (_baseNames.Contains(stem))
            return stem + ext;
        var best = _baseNames
            .Select(b => (Name: b, Distance: Levenshtein(stem, b.ToLowerInvariant())))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name.Length)
            .First();
        // A far-off match is worse than a tidied-up version of the original name.
        return (best.Distance <= Math.Max(2, stem.Length / 3) ? best.Name.ToLowerInvariant() : stem) + ext;
    }

    private static string Normalize(string stem)
    {
        var s = stem.ToLowerInvariant();
        foreach (var junk in new[] { " - copy", " copy", "(copy)", "_copy", "-copy" })
            s = s.Replace(junk, "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(\d+\)$", "");
        return s.Trim().Replace(' ', '_');
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
