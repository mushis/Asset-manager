using System.IO.Compression;
using System.Text;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Imaging;

namespace QuakeAssetManager.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qam-tests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class CoreTests
{
    private static (TempDir Dir, AssetManager Manager) NewQuake()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "quake", "id1"));
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(t.ActiveFolder(System.IO.Path.Combine(dir.Path, "quake")));
        var settings = new AppSettings
        {
            QuakeFolder = System.IO.Path.Combine(dir.Path, "quake"),
            AssetsFolder = System.IO.Path.Combine(dir.Path, "assets"),
        };
        return (dir, new AssetManager(settings, new GameData()));
    }

    [Fact]
    public void Import_copies_loose_files_to_dated_folder_and_subfolders_by_name_without_duplicates()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("quake/qw/textures/wad/ibar.png", "A");
        dir.File("quake/qw/textures/wad/readme.txt", "B");
        dir.File("quake/qw/textures/wad/original/ibar.png", "C");
        dir.File("quake/qw/textures/wad/original/dup.png", "A"); // same content as ibar.png

        var now = new DateTime(2026, 10, 16);
        // 0.4: the scan copies loose files only; subfolders are offered separately.
        var first = m.ImportAll(now);
        Assert.Equal(2, first.Copied);
        Assert.True(File.Exists(System.IO.Path.Combine(dir.Path, "assets/wad/16 Oct 2026/ibar.png")));
        Assert.True(File.Exists(System.IO.Path.Combine(dir.Path, "assets/wad/16 Oct 2026/readme.txt")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(dir.Path, "assets/wad/original")));

        var found = Assert.Single(m.FindSubfolders());
        Assert.Equal(("wad", "original", 2), (found.Type.Id, found.Name, found.FileCount));
        var sub = m.ImportSubfolders([found], deleteOriginals: false);
        Assert.Equal((1, 1), (sub.Copied, sub.Skipped));                       // dup.png has the same content as ibar.png
        Assert.True(File.Exists(System.IO.Path.Combine(dir.Path, "assets/wad/original/ibar.png")));
        Assert.True(File.Exists(System.IO.Path.Combine(dir.Path, "quake/qw/textures/wad/original/ibar.png")));  // Copy keeps the original
        Assert.Empty(m.FindSubfolders());                                       // not asked again

        var second = m.ImportAll(now);
        Assert.Equal(0, second.Copied);

        dir.File("quake/qw/textures/wad/sbar.png", "D");
        var third = m.ImportAll(now);
        Assert.Equal(1, third.Copied);
        Assert.True(File.Exists(System.IO.Path.Combine(dir.Path, "assets/wad/16 Oct 2026 (2)/sbar.png")));
        Assert.Empty(Directory.GetFiles(System.IO.Path.Combine(dir.Path, "assets/wad")));
    }

    [Fact]
    public void UseFile_copies_lower_case_and_replaces_other_extensions_after_backing_them_up()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("quake/qw/textures/wad/ibar.tga", "OLD");
        var option = dir.File("assets/wad/pack/IBAR.png", "NEW");

        m.UseFile(Types.Wad, option);

        var active = Directory.GetFiles(System.IO.Path.Combine(dir.Path, "quake/qw/textures/wad")).Select(System.IO.Path.GetFileName).ToList();
        Assert.Equal(["ibar.png"], active);
        // The replaced tga was not in assets, so it was backed up first.
        var backups = Directory.GetFiles(System.IO.Path.Combine(dir.Path, "assets/wad"), "ibar.tga", SearchOption.AllDirectories);
        Assert.Single(backups);
    }

    [Fact]
    public void MoveActiveToAssets_moves_invalid_file_with_new_name()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        var junk = dir.File("quake/qw/textures/wad/ibar (copy).png", "J");
        m.MoveActiveToAssets(Types.Wad, junk, "junk", "ibar.png");
        Assert.False(File.Exists(junk));
        Assert.Equal("J", File.ReadAllText(System.IO.Path.Combine(dir.Path, "assets/wad/junk/ibar.png")));
    }

    [Fact]
    public void Wad_names_follow_ezquake_and_suggestions_strip_copy_suffix()
    {
        var set = ValidNameSet.Build(Types.Wad, new GameData());
        Assert.True(set.IsValidFileName("inv2_lightng.png"));
        Assert.True(set.IsValidFileName("sb_quad.tga"));
        Assert.False(set.IsValidFileName("inv_rocket.png"));
        Assert.False(set.IsValidFileName("ibar.jpeg"));
        Assert.False(set.IsValidFileName("ibar_luma.png"));
        Assert.Equal("ibar.png", set.SuggestName("ibar (copy).png"));
        Assert.Equal("sb_quad.tga", set.SuggestName("SB_Quad (2).tga"));
    }

    [Fact]
    public void Pak_and_pk3_entries_can_be_read()
    {
        using var dir = new TempDir();
        var pak = System.IO.Path.Combine(dir.Path, "test.pak");
        var payload = Encoding.ASCII.GetBytes("hello");
        using (var w = new BinaryWriter(File.Create(pak)))
        {
            w.Write(Encoding.ASCII.GetBytes("PACK"));
            w.Write(12 + payload.Length);
            w.Write(64);
            w.Write(payload);
            var name = new byte[56];
            Encoding.ASCII.GetBytes("gfx/hello.txt").CopyTo(name, 0);
            w.Write(name);
            w.Write(12);
            w.Write(payload.Length);
        }
        using (var a = GameArchive.Open(pak))
        {
            var e = Assert.Single(a.Entries);
            Assert.Equal("gfx/hello.txt", e.Path);
            Assert.Equal("hello", Encoding.ASCII.GetString(a.ReadEntry(e)));
        }

        var pk3 = System.IO.Path.Combine(dir.Path, "test.pk3");
        using (var z = ZipFile.Open(pk3, ZipArchiveMode.Create))
        {
            z.CreateEntry("textures/models/");
            using var s = z.CreateEntry("textures/models/armor_0.png").Open();
            s.Write(payload);
        }
        using (var a = GameArchive.Open(pk3))
        {
            var e = Assert.Single(a.Entries);
            Assert.Equal(Types.Models, AssetType.FromArchivePath(e.Path));
            Assert.Equal("hello", Encoding.ASCII.GetString(a.ReadEntry(e)));
        }
    }

    [Fact]
    public void Tga_uncompressed_bottom_left_is_flipped_to_top_down()
    {
        // 1x2 image, 24-bit, bottom-left origin: first stored pixel is the bottom row.
        byte[] tga = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, 0, 24, 0,
            /* bottom: blue */ 255, 0, 0, /* top: red */ 0, 0, 255];
        var img = ImageDecoders.DecodeTga(tga)!;
        Assert.Equal((1, 2), (img.Width, img.Height));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels[..4]); // top row is red
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Pixels[4..]);
    }

    [Fact]
    public void Tga_rle_is_decoded()
    {
        // 3x1, RLE, top-left origin: one run packet of 3 green pixels.
        byte[] tga = [0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 1, 0, 24, 0x20, 0x82, 0, 255, 0];
        var img = ImageDecoders.DecodeTga(tga)!;
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(255, img.Pixels[i * 4 + 1]));
    }

    [Fact]
    public void Settings_require_id1_and_qw()
    {
        using var dir = new TempDir();
        var s = new AppSettings { QuakeFolder = dir.Path, AssetsFolder = System.IO.Path.Combine(dir.Path, "assets"), Client = "x", Map = "dm4" };
        Assert.NotNull(SettingsValidator.Validate(s).QuakeFolderError);
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "id1"));
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "qw"));
        var v = SettingsValidator.Validate(s);
        Assert.Null(v.QuakeFolderError);
        Assert.Equal(AssetType.All.Count, v.MissingActiveFolders.Count);
        Assert.NotNull(v.ClientError); // "x" doesn't exist
    }
}

/// <summary>Read-only checks against the real install, skipped when it isn't there.</summary>
public class RealInstallTests
{
    private const string Quake = @"C:\Games\nQuake";

    [Fact]
    public void Game_data_yields_known_names()
    {
        if (!Directory.Exists(Path.Combine(Quake, "id1")))
            return;
        var data = GameData.Load(Quake);
        Assert.NotNull(data.Palette);
        Assert.Equal(3, data.MdlSkins["armor"].Count);
        var models = ValidNameSet.Build(Types.Models, data);
        Assert.True(models.IsValidFileName("armor_2.png"));
        Assert.True(models.IsValidFileName("simple_armor_0.tga"));
        Assert.True(models.IsValidFileName("backpack_0_luma.png"));
        var bmodels = ValidNameSet.Build(Types.BModels, data);
        Assert.True(bmodels.IsValidFileName("med3_0_luma.png"));
        Assert.True(bmodels.IsValidFileName("+0_box_side.jpg"));
        Assert.True(bmodels.IsValidFileName("simple_b_batt0_0.tga"));
    }

    [Fact]
    public void Real_archives_open_and_palette_images_decode()
    {
        var pak0 = Path.Combine(Quake, "id1", "pak0.pak");
        if (!File.Exists(pak0))
            return;
        var data = GameData.Load(Quake);
        using var pak = GameArchive.Open(pak0);
        var armor = pak.Entries.First(e => e.Path == "progs/armor.mdl");
        var skin = ImageDecoders.Decode(".mdl", pak.ReadEntry(armor), data.Palette);
        Assert.NotNull(skin);
        var wad = pak.Entries.First(e => e.Path == "gfx.wad");
        Assert.True(QuakeAssetManager.Core.Formats.WadFile.TryParse(pak.ReadEntry(wad), out var w));
        var ibar = w!.Lumps.First(l => l.Name == "ibar");
        Assert.NotNull(ImageDecoders.Decode(".wadlump", w.ReadLump(ibar), data.Palette, ibar.Name, ibar.Type));
    }
}
