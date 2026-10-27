using System.IO.Compression;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager.Tests;

/// <summary>docs/version-0.5-plan.md</summary>
[Collection("config")]
public class Version05Tests : IDisposable
{
    public void Dispose() => AssetType.Configure(AssetTypeDefaults.Create());

    private static (TempDir Dir, AssetManager Manager, AssetConfig Config) NewQuake()
    {
        var dir = new TempDir();
        var config = AssetConfig.Load(Path.Combine(dir.Path, "config"));
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(t.ActiveFolder(Path.Combine(dir.Path, "quake")));
        var settings = new AppSettings { QuakeFolder = Path.Combine(dir.Path, "quake"), AssetsFolder = Path.Combine(dir.Path, "assets") };
        var m = new AssetManager(settings, new GameData { MdlSkins = { ["armor"] = [1, 1, 1] } }, config);
        m.EnsureAssetsFolders();
        return (dir, m, config);
    }

    [Fact]
    public void Mark_as_valid_adds_the_name_to_the_names_file()
    {
        var (dir, m, config) = NewQuake();
        using var _ = dir;
        Assert.False(m.IsValid(Types.Models, "armor_9.png"));
        var added = m.MarkValid(Types.Models, ["armor_9.png", "armor_9.tga"]);
        Assert.Equal(["armor_9"], added);
        Assert.True(m.IsValid(Types.Models, "armor_9.png"));
        Assert.False(m.IsValid(Types.Models, "armor_9.txt"));   // the extension still has to be one the type uses
        Assert.Contains("armor_9", config.LoadNames(Types.Models).Names);

        // A new manager (next start) reads it from the json.
        var next = new AssetManager(m.Settings, new GameData(), AssetConfig.Load(config.Folder));
        Assert.True(next.IsValid(AssetType.Get("models"), "armor_9.png"));
    }

    [Fact]
    public void Nested_folders_can_be_used_and_moved_to_the_root()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("assets/models/pack/extra/armor_0.png", "N");
        var listing = m.ListAssets(Types.Models);
        var nested = listing.Packs.Single().Folders.Single();
        Assert.True(nested.Files.Single().IsValid);              // 0.5: nested files count
        var plan = m.PlanUseFolder(Types.Models, nested.FullPath);
        Assert.Single(plan.FilesToCopy);

        dir.File("assets/models/extra/keep.txt", "taken");
        var moved = m.MoveFolderToRoot(Types.Models, nested.FullPath);
        Assert.Equal(Path.Combine(m.AssetsFolder(Types.Models), "extra (2)"), moved);
        Assert.True(File.Exists(Path.Combine(moved, "armor_0.png")));
    }

    [Fact]
    public void Invalid_files_can_be_copied_to_the_game_and_renamed_there_in_lower_case()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("assets/models/pack/Armor_Gold.PNG", "G");
        var copied = m.CopyToGame(Types.Models, Path.Combine(m.AssetsFolder(Types.Models), "pack", "Armor_Gold.PNG"));
        Assert.Equal("Armor_Gold.PNG", Path.GetFileName(copied));   // name unchanged
        var renamed = m.RenameInGame(copied, "Armor_0.PNG");
        Assert.Equal("armor_0.png", Path.GetFileName(renamed));
        Assert.Equal("G", File.ReadAllText(renamed));
    }

    [Fact]
    public void Lower_case_tool_renames_and_skips_clashes()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("quake/qw/textures/models/Armor_0.PNG", "A");
        dir.File("assets/models/pack/Ibar.png", "B");
        dir.File("assets/models/pack/x/ABC.png", "C");
        // Only a case-sensitive file system can hold both ABC.png and abc.png.
        var clash = !OperatingSystem.IsWindows();
        if (clash) dir.File("assets/models/pack/x/abc.png", "D");
        var found = m.FindUpperCaseNames();
        Assert.Equal(3, found.Count);
        var (renamed, skipped) = m.ConvertToLowerCase(found);
        Assert.Equal(clash ? 2 : 3, renamed);
        if (clash) Assert.Equal("ABC.png", Assert.Single(skipped).Name);
        else Assert.Empty(skipped);
        Assert.Contains("armor_0.png", Directory.GetFiles(m.ActiveFolder(Types.Models)).Select(Path.GetFileName));
        Assert.Contains("ibar.png", Directory.GetFiles(Path.Combine(m.AssetsFolder(Types.Models), "pack")).Select(Path.GetFileName));
    }

    [Fact]
    public void Duplicates_are_the_same_path_in_several_places_including_archives()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("quake/id1/textures/abc.png", "same");
        dir.File("quake/qw/textures/abc.png", "same");
        dir.File("quake/qw/gfx/conback.png", "loose");
        using (var zip = ZipFile.Open(Path.Combine(dir.Path, "quake", "qw", "pack.pk3"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("gfx/conback.png").Open()))
            w.Write("packed");
        dir.File("assets/models/pack/abc.png", "ignored: the Assets folder is left out");

        var groups = m.FindDuplicates();
        Assert.Equal(["gfx/conback.png", "textures/abc.png"], groups.Select(g => g.Path).ToArray());
        Assert.True(groups[1].Identical);
        Assert.False(groups[0].Identical);
        Assert.Contains(groups[0].Locations, l => l.Display == "qw/pack.pk3 → gfx/conback.png");

        // qw before id1, and inside a folder the loose file before a pk3.
        Assert.Equal("qw/gfx/conback.png", groups[0].Locations.Single(l => l.InUse).Display);
        Assert.Equal("qw/textures/abc.png", groups[1].Locations.Single(l => l.InUse).Display);
        Assert.Equal("packed", System.Text.Encoding.ASCII.GetString(AssetManager.ReadDuplicate(groups[0].Locations.Single(l => !l.InUse))));
    }

    [Fact]
    public void In_use_follows_ezquake_search_order()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        void Pk3(string path, string text)
        {
            using var zip = ZipFile.Open(Path.Combine(dir.Path, "quake", path), ZipArchiveMode.Create);
            using var w = new StreamWriter(zip.CreateEntry("textures/x.png").Open());
            w.Write(text);
        }
        Directory.CreateDirectory(Path.Combine(dir.Path, "quake", "ezquake"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "quake", "mymod"));
        Pk3("ezquake/ezquake.pk3", "ez");
        Pk3("mymod/mod.pk3", "mod");          // only loaded with -game mymod
        Pk3("qw/a.pk3", "a");
        Pk3("qw/b.pk3", "b");                 // added after a.pk3, so searched first
        Pk3("qw/pak0.pk3", "p0");
        var g = Assert.Single(m.FindDuplicates());
        Assert.Equal("qw/b.pk3 → textures/x.png", g.Locations.Single(l => l.InUse).Display);
        dir.File("quake/qw/textures/x.png", "loose");      // a loose file takes precedence
        Assert.Equal("qw/textures/x.png", Assert.Single(m.FindDuplicates()).Locations.Single(l => l.InUse).Display);
    }

    [Fact]
    public void Exact_copies_in_assets_need_the_same_contents()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("assets/models/a/armor_0.png", "SAME");
        dir.File("assets/models/b/armor_0.png", "SAME");
        dir.File("assets/other/textures/armor_0.png", "SAME");
        dir.File("assets/models/c/armor_0.png", "DIFF");     // same size, other contents
        dir.File("assets/models/d/armor_1.png", "SAME");     // other name, same contents: a copy too (0.6)
        var g = Assert.Single(m.FindAssetCopies());
        Assert.Equal(4, g.Copies.Count);
        Assert.Equal(3, m.DeleteAssetCopies(g.Copies.Skip(1).Select(c => c.FullPath)));
        Assert.Empty(m.FindAssetCopies());
    }

    [Fact]
    public void Lower_case_tool_says_whether_a_file_is_in_the_quake_or_assets_folder()
    {
        var (dir, m, _) = NewQuake();
        using var __ = dir;
        dir.File("quake/qw/crosshairs/Dot.png", "A");          // a hidden type's folder is checked too
        dir.File("assets/crosshairs/x/Dot.png", "A");
        var found = m.FindUpperCaseNames();
        Assert.Contains(found, f => !f.InAssets && f.Folder == Path.Combine("qw", "crosshairs"));
        Assert.Contains(found, f => f.InAssets && f.Folder == Path.Combine("assets", "crosshairs", "x"));
    }

    [Fact]
    public void Hints_name_the_setting_that_decides_whether_a_file_is_used()
    {
        AssetType.Configure(AssetTypeDefaults.Create());
        Assert.Equal("gl_simpleitems", Types.Models.HintFor("simple_armor_0.png")?.Cvar);
        Assert.Equal("gl_lumatextures", Types.Models.HintFor("armor_0_luma.png")?.Cvar);
        Assert.Null(Types.Models.HintFor("armor_0.png"));
        var crosshairs = AssetType.Get("crosshairs");
        Assert.Equal("crosshair", crosshairs.HintFor("crosshair.txt")?.Cvar);
        Assert.Equal("chosen", crosshairs.HintFor("dot.png")?.Kind);
        Assert.Equal("cl_useimagesinfraglog", Types.Tracker.HintFor("rl.png")?.Cvar);
    }

    [Fact]
    public void Config_from_0_4_gets_hints_and_the_unezquake_types()
    {
        using var dir = new TempDir();
        // A 0.4 file: version 2, no hints, no sprays/emoticons, Crosshairs switched on.
        File.WriteAllText(Path.Combine(dir.Path, "asset-types.json"), """
            { "configVersion": 2, "assetTypes": [
              { "id": "models", "displayName": "Models", "visible": true, "activeFolderPath": "qw/textures/models", "assetsFolderName": "models" },
              { "id": "crosshairs", "displayName": "Crosshairs", "visible": true, "activeFolderPath": "qw/crosshairs", "assetsFolderName": "crosshairs" }
            ] }
            """);

        var reloaded = AssetConfig.Load(dir.Path);
        Assert.True(AssetType.Get("crosshairs").Visible);        // still shown
        Assert.NotEmpty(AssetType.Get("models").Hints);
        Assert.False(AssetType.Get("sprays").Visible);
        Assert.Equal("qw/textures/emoticons", AssetType.Get("emoticons").ActiveFolderPath);
        Assert.Contains("\"configVersion\": 3", File.ReadAllText(reloaded.TypesFile));
        var m = new AssetManager(new AppSettings { QuakeFolder = dir.Path, AssetsFolder = Path.Combine(dir.Path, "assets") }, new GameData(), reloaded);
        Assert.True(m.IsValid(AssetType.Get("emoticons"), "fire.png"));
        Assert.True(m.IsValid(AssetType.Get("sprays"), "anything.jpeg"));
        Assert.Equal("sprays", FileClassifier.Classify("pack/sprays/tag.png", m).Type?.Id);
    }

    [Fact]
    public void Settings_keep_theme_online_switch_and_handled_subfolders()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new AppSettings { QuakeFolder = "q", Theme = "slate", ShowOnline = false };
        s.HandledSubfolders["Q"] = ["models/original"];
        s.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal("slate", loaded.Theme);
        Assert.False(loaded.ShowOnline);
        Assert.Equal(["models/original"], loaded.HandledSubfolders["q"]);   // ignores case
        Assert.True(AppSettings.Load(Path.Combine(dir.Path, "missing.json")).ShowOnline);
    }
}
