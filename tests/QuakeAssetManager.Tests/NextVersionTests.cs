using System.IO.Compression;
using System.Text;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;
using QuakeAssetManager.Online;

namespace QuakeAssetManager.Tests;

public class GfxParserTests
{
    private static string Page(string name) =>
        Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name)));

    [Fact]
    public void Browse_page_lists_items_with_thumbnails_and_page_count()
    {
        var page = GfxPageParser.ParseBrowse(Page("browse_models.html"), "https://gfx.quakeworld.nu");
        Assert.Equal(12, page.Items.Count);
        Assert.Equal((1, 4), (page.Page, page.PageCount));
        var dm = page.Items.Single(i => i.Id == "634");
        Assert.Equal("pak DM", dm.Name);
        Assert.Equal("Models / Sets", dm.Category);
        Assert.Equal("https://gfx.quakeworld.nu/files/634_thumb.jpg", dm.ThumbnailUrl);
        Assert.Equal("https://gfx.quakeworld.nu/details/634/pak-dm/", dm.DetailsUrl);
    }

    [Fact]
    public void Details_page_has_download_screenshot_and_description()
    {
        var item = new CatalogItem("634", "pak-dm", "pak DM", "Models / Sets", "", "");
        var d = GfxPageParser.ParseDetails(Page("details_634.html"), item, "https://gfx.quakeworld.nu");
        Assert.Equal("https://gfx.quakeworld.nu/download/634/pak-dm/", d.DownloadUrl);
        Assert.Equal("https://gfx.quakeworld.nu/files/634.jpg", d.ScreenshotUrl);
        Assert.Equal("2025-07-17", d.Submitted);
        Assert.Equal(318, d.Downloads);
        Assert.Equal("5725 kb", d.SizeText);
        Assert.Contains("My old models from 2006", d.Description);
    }

    [Fact]
    public void Unknown_markup_is_reported_as_unrecognised()
    {
        var e = Assert.Throws<OnlineException>(() => GfxPageParser.ParseBrowse("<html><body>maintenance</body></html>", "https://x"));
        Assert.Equal(OnlineProblem.Unrecognised, e.Problem);
    }

    [Fact]
    public void Default_asset_types_include_the_hardcoded_texture_lists_for_models()
    {
        var urls = Types.Models.Online.Select(s => s.Url).ToList();
        Assert.Contains("https://gfx.quakeworld.nu/browse/textures/armor/", urls);
        Assert.Contains("https://gfx.quakeworld.nu/browse/textures/backpack/", urls);
        Assert.Contains("https://gfx.quakeworld.nu/browse/textures/weapon/", urls);
        Assert.Contains("https://gfx.quakeworld.nu/browse/textures/sets/", urls);
        Assert.Empty(Types.BModels.Online);
    }
}

public class ImportTests
{
    private static (TempDir Dir, AssetManager Manager) NewQuake()
    {
        var dir = new TempDir();
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(t.ActiveFolder(Path.Combine(dir.Path, "quake")));
        var data = new GameData();
        data.MdlSkins["armor"] = [1, 1, 1];
        var settings = new AppSettings { QuakeFolder = Path.Combine(dir.Path, "quake"), AssetsFolder = Path.Combine(dir.Path, "assets") };
        var m = new AssetManager(settings, data);
        m.EnsureAssetsFolders();
        return (dir, m);
    }

    private static string Zip(TempDir dir, string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(dir.Path, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, c) in entries)
        {
            using var s = z.CreateEntry(p).Open();
            s.Write(Encoding.ASCII.GetBytes(c));
        }
        return path;
    }

    [Fact]
    public void Classifier_uses_path_then_name_then_known_folders()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        Assert.Equal(Types.Models, FileClassifier.Classify("pack/textures/models/armor_0.png", m).Type);
        Assert.Equal(Types.Models, FileClassifier.Classify("whatever/armor_1.tga", m).Type);
        Assert.Equal(Types.Wad, FileClassifier.Classify("ibar.png", m).Type);
        Assert.Equal(Types.Tracker, FileClassifier.Classify("icons/rl.png", m).Type);
        Assert.Equal(Types.Particles, FileClassifier.Classify("particlefont.tga", m).Type);
        Assert.Equal("progs", FileClassifier.Classify("progs/armor.mdl", m).Type?.Id);     // hidden types are copied too
        Assert.Equal("skins", FileClassifier.Classify("qw/skins/base.png", m).Type?.Id);
        Assert.Equal("sound", FileClassifier.Classify("weapons/r_exp3.wav", m).OtherFolder);
        Assert.NotNull(FileClassifier.Classify("readme.txt", m).Reason);
    }

    [Fact]
    public void Dropped_zip_is_staged_then_committed_into_a_folder_named_after_it()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        var zip = Zip(dir, "cool pack.zip", ("textures/models/armor_0.png", "A"), ("armor_1.png", "B"), ("hud/ibar.png", "C"),
            ("progs/armor.mdl", "M"), ("docs/readme.txt", "R"), ("dup/armor_2.png", "A"));
        // Files at the zip's root are asked about; accept the suggestions (what the dialog pre-selects).
        var importer = new Importer(m);
        var plan = importer.Prepare([zip], new DateTime(2026, 10, 16));
        var session = importer.Stage(plan, plan.Questions.ToDictionary(q => q.Key, q => q.Suggested?.Id));

        Assert.Equal(2, session.AddedCount("models"));
        Assert.Equal(1, session.AddedCount("wad"));
        Assert.Equal(1, session.Duplicates); // armor_2 has the same content as armor_0
        Assert.Equal(1, session.AddedCount("progs"));
        Assert.Contains(session.Ignored, i => i.StartsWith("docs/readme.txt"));
        Assert.Empty(Directory.GetDirectories(m.AssetsFolder(Types.Models))); // nothing visible before OK

        session.Commit();
        Assert.True(File.Exists(Path.Combine(m.AssetsFolder(Types.Models), "cool pack", "armor_0.png")));
        Assert.True(File.Exists(Path.Combine(m.AssetsFolder(Types.Wad), "cool pack", "ibar.png")));
        Assert.False(Directory.Exists(session.StagingDir));
    }

    [Fact]
    public void Revert_leaves_assets_untouched()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        var file = dir.File("drop/ibar.png", "X");
        var session = new Importer(m).Stage([file], new DateTime(2026, 10, 16));
        Assert.Equal(1, session.AddedCount("wad"));
        session.Revert();
        Assert.Empty(Directory.GetFileSystemEntries(m.AssetsFolder(Types.Wad)));
        Assert.False(Directory.Exists(session.StagingDir));
    }

    [Fact]
    public void Single_files_go_to_the_dated_folder()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        var file = dir.File("drop/armor_0.png", "X");
        new Importer(m).Stage([file], new DateTime(2026, 10, 16)).Commit();
        Assert.True(File.Exists(Path.Combine(m.AssetsFolder(Types.Models), "16 Oct 2026", "armor_0.png")));
    }

    [Fact]
    public void Active_file_reports_where_its_backup_is_and_assets_marks_the_one_in_game()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("quake/qw/textures/models/armor_0.png", "OLD");
        m.ImportAll(new DateTime(2026, 10, 16));
        Assert.Equal("16 Oct 2026", m.ListActive(Types.Models).Single().Backup);

        var option = dir.File("assets/models/simple/armor_0.png", "NEW");
        m.UseFile(Types.Models, option);
        var inGame = m.ListAssets(Types.Models).Packs.SelectMany(p => p.Files).Where(f => f.IsActive).ToList();
        Assert.Equal(Path.GetFullPath(option), Path.GetFullPath(Assert.Single(inGame).FullPath));
        Assert.Single(m.FindCopiesInAssets(Types.Models, m.ListActive(Types.Models).Single().FullPath));
    }

    [Fact]
    public void Tracker_names_come_from_fragfile_weapon_classes()
    {
        var text = "#DEFINE WEAPON_CLASS ROCKET_LAUNCHER \"rocket launcher\" \"rl\" \"\\rl\\\"\n" +
                   "#DEFINE WEAPON_CLASS Q_LG \"q lg\" \"lg\" \"\\lg\\ \\q\\\" // quad\n" +
                   "//#DEFINE WEAPON_CLASS NOPE \"x\" \"x\" \"\\nope\\\"\n" +
                   "#DEFINE WC CUSTOM \"custom\" \"c\" \"\\mygun\\\"\n";
        Assert.Equal(["rl", "lg", "q", "mygun"], GameData.FragfileImages(text).ToArray());
        var data = new GameData();
        data.TrackerImages.Add("mygun");
        var set = ValidNameSet.Build(Types.Tracker, data);
        Assert.True(set.IsValidFileName("mygun.png"));
        Assert.True(set.IsValidFileName("tkills.tga"));
        Assert.True(ValidNameSet.Build(Types.Particles, data).IsValidFileName("particlefont.png"));
    }
}
