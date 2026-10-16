using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager.Tests;

/// <summary>Where each file goes (docs/version-0.4-plan.md, sections 9-12).</summary>
public class RoutingTests : IDisposable
{
    public void Dispose() => AssetType.Configure(AssetTypeDefaults.Create());

    private static AssetManager Manager(string quake = @"C:\Games\nQuake")
    {
        AssetType.Configure(AssetTypeDefaults.Create());
        var data = Directory.Exists(Path.Combine(quake, "id1")) ? GameData.Load(quake) : new GameData();
        if (data.MdlSkins.Count == 0) data.MdlSkins["armor"] = [1, 1, 1];
        return new AssetManager(new AppSettings { QuakeFolder = quake, AssetsFolder = Path.Combine(Path.GetTempPath(), "qam-routing") }, data);
    }

    [Theory]
    // A type folder in the path decides (shown or hidden types).
    [InlineData("textures/models/armor_0.png", false, "models", false)]
    [InlineData("pack/textures/wad/ibar.png", false, "wad", false)]
    [InlineData("gfx/ibar.png", true, "gfx", false)]                        // case 1: HUD name in gfx/ -> Gfx
    [InlineData("progs/b_g_key.png", true, "progs", false)]                 // md3 skin next to the model
    [InlineData("progs/beartrap.mdl", true, "progs", false)]                // any model name
    [InlineData("crosshairs/crosshair.txt", true, "crosshairs", false)]     // read by ezQuake
    [InlineData("textures/particles/bps.png", true, "particles", true)]     // renamed alternative: copied, invalid
    [InlineData("textures/models/player_3.png", true, "models", true)]
    [InlineData("textures/levelshots/aerowalk.jpg", true, "levelshots", false)] // any map name
    [InlineData("env/doom2016_vortex_rt.tga", true, "skyboxes", false)]
    public void Folder_in_the_path_decides(string path, bool fromArchive, string type, bool invalid)
    {
        var c = FileClassifier.Classify(path, Manager(), fromArchive);
        Assert.Equal(type, c.Type?.Id);
        Assert.Equal(invalid, c.Invalid);
    }

    [Theory]
    [InlineData("maps/b_bh100.bsp", "maps")]                    // item boxes belong in maps/, not Progs
    [InlineData("textures/med100.jpg", "textures")]             // case 2: map texture folder
    [InlineData("textures/#teleport.png", "textures")]
    [InlineData("textures/scrollbars/arrow_up.jpg", "textures/scrollbars")]
    [InlineData("radars/dm2.png", "radars")]
    [InlineData("lits/dm4.lit", "lits")]
    [InlineData("sound/weapons/coilgun.wav", "sound")]
    [InlineData("configs/dm.cfg", "configs")]
    public void Known_unmanaged_folders_are_left_out(string path, string folder)
    {
        var c = FileClassifier.Classify(path, Manager(), fromArchive: true);
        Assert.Null(c.Type);
        Assert.False(c.NeedsChoice);
        Assert.Equal(folder, c.OtherFolder);
    }

    [Fact]
    public void Archive_files_in_unknown_folders_are_asked_about_with_a_name_based_suggestion()
    {
        var m = Manager();
        var root = FileClassifier.Classify("armor_0.png", m, fromArchive: true);
        Assert.True(root.NeedsChoice);
        Assert.Equal("models", root.Suggested?.Id);
        var widget = FileClassifier.Classify("hud/widgets/StandardBox.tga", m, fromArchive: true);
        Assert.True(widget.NeedsChoice);
        Assert.Null(widget.Suggested);                          // any-name types are never suggested
        Assert.Null(FileClassifier.Classify("mycrosshair.png", m, fromArchive: true).Suggested);
    }

    [Fact]
    public void Loose_files_use_fixed_name_lists_and_otherwise_ask()
    {
        var m = Manager();
        Assert.Equal("models", FileClassifier.Classify("armor_0.png", m).Type?.Id);
        Assert.Equal("wad", FileClassifier.Classify("inv_rocket.png", m).Type?.Id);  // all gfx.wad names are HUD names
        Assert.Equal("progs", FileClassifier.Classify("fryguy.mdl", m).Type?.Id);
        Assert.True(FileClassifier.Classify("mycrosshair.png", m).NeedsChoice);
        Assert.True(FileClassifier.Classify("arrow_up.jpg", m).NeedsChoice);           // no longer a skybox by name
        Assert.Equal("system file", FileClassifier.Classify("textures/wad/Thumbs.db", m).Reason);
    }

    [Fact]
    public void Questions_group_by_folder_and_answers_decide()
    {
        using var dir = new TempDir();
        var m = new AssetManager(new AppSettings { QuakeFolder = dir.Path, AssetsFolder = Path.Combine(dir.Path, "assets") }, new GameData { MdlSkins = { ["armor"] = [1, 1, 1] } });
        AssetType.Configure(AssetTypeDefaults.Create());
        var files = new[]
        {
            new SourceFile("armor_0.png", () => "A"u8.ToArray(), InArchive: true),
            new SourceFile("armor_1.png", () => "B"u8.ToArray(), InArchive: true),
            new SourceFile("hud/widgets/box.tga", () => "C"u8.ToArray(), InArchive: true),
            new SourceFile("textures/wad/ibar.png", () => "D"u8.ToArray(), InArchive: true),
        };
        var importer = new Importer(m);
        var plan = importer.Prepare("pack.pk3", "pack", files);
        Assert.Equal(2, plan.Questions.Count);
        var root = plan.Questions.Single(q => q.Folder == "");
        Assert.Equal("models", root.Suggested?.Id);
        var widgets = plan.Questions.Single(q => q.Folder == "hud/widgets");
        var session = importer.Stage(plan, new Dictionary<string, AssetType?> { [root.Key] = root.Suggested, [widgets.Key] = null });
        Assert.Equal(2, session.AddedCount("models"));
        Assert.Equal(1, session.AddedCount("wad"));
        Assert.Contains(session.Ignored, i => i.Contains("hud/widgets/box.tga"));
        session.Revert();
    }

    /// <summary>Every file of the real nQuake archives, read-only. Skipped when the install isn't there.</summary>
    [Fact]
    public void Real_nquake_archives_route_as_planned()
    {
        const string quake = @"C:\Games\nQuake";
        if (!File.Exists(Path.Combine(quake, "qw", "nquake.pk3")))
            return;
        var m = Manager(quake);
        Dictionary<string, int> Count(string archive)
        {
            using var a = GameArchive.Open(Path.Combine(quake, archive));
            return a.Entries
                .Select(e => FileClassifier.Classify(e.Path, m, fromArchive: true))
                .GroupBy(c => c.Type?.Id ?? (c.NeedsChoice ? "ASK" : c.OtherFolder ?? "IGNORED"))
                .ToDictionary(g => g.Key, g => g.Count());
        }
        var nquake = Count(@"qw\nquake.pk3");
        Assert.Equal(43, nquake["gfx"]);
        Assert.Equal(28, nquake["progs"]);
        Assert.Equal(7, nquake["levelshots"]);
        Assert.Equal(21, nquake["tracker"]);
        Assert.Equal(10, nquake["lits"]);
        Assert.Equal(4, nquake["textures/scrollbars"]);
        Assert.Equal(13, nquake["sound"]);
        Assert.Equal(4, nquake["configs"]);
        Assert.False(nquake.ContainsKey("ASK"));

        var models = Count(@"qw\models.pk3");
        Assert.Equal(42, models["progs"]);
        Assert.Equal(18, models["models"]);

        var pak0 = Count(@"id1\pak0.pak");
        Assert.False(pak0.ContainsKey("bmodels"));      // maps/b_*.bsp are not textures
        Assert.True(pak0["maps"] > 0);
        Assert.False(pak0.ContainsKey("skyboxes"));
    }
}
