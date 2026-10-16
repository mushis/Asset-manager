using System.Text.Json;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager.Tests;

/// <summary>These tests change the global asset type list, so they don't run in parallel with the others.</summary>
[CollectionDefinition("config", DisableParallelization = true)]
public class ConfigCollection;

[Collection("config")]
public class ConfigTests : IDisposable
{
    public void Dispose() => AssetType.Configure(AssetTypeDefaults.Create());

    [Fact]
    public void First_run_writes_all_known_types_with_only_the_five_shown()
    {
        using var dir = new TempDir();
        var config = AssetConfig.Load(dir.Path);
        Assert.True(File.Exists(config.TypesFile));
        Assert.True(File.Exists(Path.Combine(dir.Path, "names", "wad.json")));
        Assert.Equal(["models", "bmodels", "wad", "tracker", "particles"], AssetType.All.Select(t => t.Id).ToArray());
        Assert.Contains(AssetType.Known, t => t.Id == "crosshairs" && !t.Visible);
        Assert.Contains(AssetType.Known, t => t.Id == "skyboxes" && t.ActiveFolderPath == "qw/env");
    }

    [Fact]
    public void Visibility_and_new_names_come_from_the_json_files()
    {
        using var dir = new TempDir();
        var config = AssetConfig.Load(dir.Path);
        AssetType.Get("crosshairs").Visible = true;
        AssetType.Get("particles").Visible = false;
        config.SaveTypes();

        // Add a name to HUD by editing names/wad.json.
        var wadNames = Path.Combine(dir.Path, "names", "wad.json");
        var names = JsonSerializer.Deserialize<NamesConfig>(File.ReadAllText(wadNames), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        names.Names.Add("my_custom_icon");
        File.WriteAllText(wadNames, JsonSerializer.Serialize(names, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var reloaded = AssetConfig.Load(dir.Path);
        Assert.Contains(AssetType.All, t => t.Id == "crosshairs");
        Assert.DoesNotContain(AssetType.All, t => t.Id == "particles");
        var m = new AssetManager(new AppSettings { QuakeFolder = dir.Path, AssetsFolder = Path.Combine(dir.Path, "assets") }, new GameData(), reloaded);
        Assert.True(m.IsValid(AssetType.Get("wad"), "my_custom_icon.png"));
        Assert.True(m.IsValid(AssetType.Get("crosshairs"), "anything.tga"));
    }

    [Fact]
    public void A_new_asset_type_can_be_added_in_json()
    {
        using var dir = new TempDir();
        var config = AssetConfig.Load(dir.Path);
        var json = File.ReadAllText(config.TypesFile).Replace("\"assetTypes\": [", """
            "assetTypes": [
              {
                "id": "scrollbars", "displayName": "Scrollbars", "visible": true,
                "activeFolderPath": "qw/textures/scrollbars", "assetsFolderName": "scrollbars",
                "extensions": [".png", ".tga"], "namesFile": "names/scrollbars.json",
                "archivePrefixes": ["textures/scrollbars/"]
              },
            """);
        File.WriteAllText(config.TypesFile, json);
        Directory.CreateDirectory(Path.Combine(dir.Path, "names"));
        File.WriteAllText(Path.Combine(dir.Path, "names", "scrollbars.json"), """{ "names": ["scrollbar_up"] }""");

        var reloaded = AssetConfig.Load(dir.Path);
        var type = AssetType.Get("scrollbars");
        Assert.Equal(Path.Combine(dir.Path, "qw", "textures", "scrollbars"), type.ActiveFolder(dir.Path));
        var m = new AssetManager(new AppSettings { QuakeFolder = dir.Path, AssetsFolder = Path.Combine(dir.Path, "assets") }, new GameData(), reloaded);
        Assert.True(m.IsValid(type, "scrollbar_up.png"));
        Assert.False(m.IsValid(type, "scrollbar_up.jpg"));
        Assert.Equal(type, FileClassifier.Classify("pack/textures/scrollbars/scrollbar_up.png", m).Type);
    }

    [Fact]
    public void Files_of_hidden_types_are_reported_not_added()
    {
        using var dir = new TempDir();
        AssetType.Configure(AssetTypeDefaults.Create());
        var m = new AssetManager(new AppSettings { QuakeFolder = dir.Path, AssetsFolder = Path.Combine(dir.Path, "assets") }, new GameData());
        var c = FileClassifier.Classify("cool/crosshairs/dot.png", m);
        Assert.Equal("crosshairs", c.Type?.Id);   // 0.4: files of hidden types are copied too
        Assert.False(c.Type!.Visible);
        // Case 1 (0.4): a HUD name inside gfx/ goes to Gfx, the folder it came in.
        Assert.Equal("gfx", FileClassifier.Classify("pack/gfx/ibar.png", m).Type?.Id);
    }
}

[Collection("config")]
public class ImportAndUseTests
{
    private static (TempDir Dir, AssetManager Manager) NewQuake()
    {
        AssetType.Configure(AssetTypeDefaults.Create());
        var dir = new TempDir();
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(t.ActiveFolder(Path.Combine(dir.Path, "quake")));
        var settings = new AppSettings { QuakeFolder = Path.Combine(dir.Path, "quake"), AssetsFolder = Path.Combine(dir.Path, "assets") };
        var m = new AssetManager(settings, new GameData());
        m.EnsureAssetsFolders();
        return (dir, m);
    }

    [Fact]
    public void Archive_entries_are_staged_to_their_type_folders_and_busiest_folder_is_known()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        var files = new List<SourceFile>
        {
            new("textures/wad/ibar.png", () => "A"u8.ToArray()),
            new("textures/wad/sbar.png", () => "B"u8.ToArray()),
            new("textures/tracker/rl.png", () => "C"u8.ToArray()),
            new("progs/armor.mdl", () => "D"u8.ToArray()),
        };
        var session = new Importer(m).StageFiles("models.pk3", "models", files);
        Assert.Equal(2, session.AddedCount("wad"));
        Assert.Equal(1, session.AddedCount("tracker"));
        Assert.Equal(("wad", Path.Combine(m.AssetsFolder(Types.Wad), "models")), session.Busiest());
        session.Commit();
        Assert.True(File.Exists(Path.Combine(m.AssetsFolder(Types.Tracker), "models", "rl.png")));
    }

    [Fact]
    public void Use_these_copies_the_folder_and_leaves_other_game_files_alone()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("quake/qw/textures/wad/sbar.png", "KEEP");
        dir.File("assets/wad/pack/ibar.png", "NEW");
        var plan = m.PlanUseFolder(Types.Wad, Path.Combine(m.AssetsFolder(Types.Wad), "pack"));
        m.UseFolder(Types.Wad, plan, []);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(m.ActiveFolder(Types.Wad), "ibar.png")));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(m.ActiveFolder(Types.Wad), "sbar.png")));
    }
}
