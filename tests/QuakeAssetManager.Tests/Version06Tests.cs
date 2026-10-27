using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Formats;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager.Tests;

/// <summary>docs/version-0.6-plan.md</summary>
[Collection("config")]
public class Version06Tests : IDisposable
{
    public void Dispose() => AssetType.Configure(AssetTypeDefaults.Create());

    private static (TempDir Dir, AssetManager Manager) NewQuake()
    {
        AssetType.Configure(AssetTypeDefaults.Create());
        var dir = new TempDir();
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(t.ActiveFolder(Path.Combine(dir.Path, "quake")));
        var m = new AssetManager(new AppSettings { QuakeFolder = Path.Combine(dir.Path, "quake"), AssetsFolder = Path.Combine(dir.Path, "assets") },
            new GameData { MdlSkins = { ["armor"] = [1, 1, 1] } });
        m.EnsureAssetsFolders();
        return (dir, m);
    }

    [Fact]
    public void Copies_are_found_by_contents_whatever_their_names()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("assets/models/a/armor_0.png", "SAME");
        dir.File("assets/models/b/my armor.png", "SAME");      // other name, same contents
        dir.File("assets/models/c/armor_0.png", "DIFF");
        var g = Assert.Single(m.FindAssetCopies());
        Assert.Equal(2, g.Copies.Count);

        // The Local list marks them, with the other copy's path.
        var files = m.ListAssets(Types.Models).Packs.SelectMany(p => p.Files).ToList();
        var a = files.Single(f => f.FullPath.Contains(Path.Combine("a", "armor_0.png")));
        Assert.Equal(Path.Combine(m.AssetsFolder(Types.Models), "b", "my armor.png"), Assert.Single(a.DuplicateOf!));
        Assert.Null(files.Single(f => f.FullPath.Contains(Path.Combine("c", "armor_0.png"))).DuplicateOf);
    }

    [Fact]
    public void Imports_skip_known_contents_unless_asked_to_add_them()
    {
        var (dir, m) = NewQuake();
        using var _ = dir;
        dir.File("assets/models/old/armor_0.png", "SAME");
        var importer = new Importer(m);
        var files = new[] { new SourceFile("textures/models/armor_0.png", () => "SAME"u8.ToArray(), InArchive: true) };
        var skipped = importer.Stage(importer.Prepare("p.pk3", "p", files));
        Assert.Equal(1, skipped.Duplicates);
        Assert.Empty(skipped.Files);
        skipped.Revert();
        var added = importer.Stage(importer.Prepare("p.pk3", "p", files), addDuplicates: true);
        Assert.Equal(1, added.AddedDuplicates);
        Assert.Single(added.Files);
        added.Revert();
    }

    [Fact]
    public void Md3_models_are_read_and_drawn()
    {
        const string pk3 = @"C:\Games\nQuake\qw\nquake.pk3";
        if (!File.Exists(pk3)) return;
        using var archive = GameArchive.Open(pk3);
        var entry = archive.Entries.First(e => e.Path.EndsWith("b_g_key.md3", StringComparison.OrdinalIgnoreCase));
        var model = Md3Model.TryRead(archive.ReadEntry(entry));
        Assert.NotNull(model);
        Assert.True(model.TriangleCount > 0);
        var img = model.Render(160, 160, -35, 15);
        var drawn = Enumerable.Range(0, img.Width * img.Height).Count(i => img.Pixels[i * 4 + 3] != 0);
        Assert.InRange(drawn, 500, 160 * 160);
        if (Environment.GetEnvironmentVariable("QAM_RENDER_OUT") is { Length: > 0 } outFile)
            File.WriteAllBytes(outFile, img.Pixels);
        Assert.Null(Md3Model.TryRead("not a model"u8.ToArray()));
    }
}
