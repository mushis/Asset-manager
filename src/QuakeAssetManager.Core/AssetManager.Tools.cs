using System.Security.Cryptography;
using System.Text.RegularExpressions;
using QuakeAssetManager.Core.Archives;

namespace QuakeAssetManager.Core;

/// <summary>A file whose name has upper-case letters (Settings &gt; Tools &gt; Convert to lower case).</summary>
/// <param name="Folder">Its folder, relative to the Quake folder or to the Assets folder's parent, for display.</param>
/// <param name="InAssets">It's in the Assets folder; otherwise in a game folder of the Quake folder.</param>
public sealed record CaseFix(string FullPath, string Folder, string Name, bool InAssets = false)
{
    public string NewName => Name.ToLowerInvariant();
}

/// <param name="Display">e.g. "qw/textures/abc.png" or "qw/nquake.pk3 → textures/abc.png".</param>
/// <param name="InUse">The copy the client loads (ezQuake's search order, see <see cref="AssetManager.FindDuplicates"/>).</param>
/// <param name="FilePath">The loose file, or null for an archive entry.</param>
/// <param name="ArchivePath">The pak/pk3 holding it, or null for a loose file.</param>
/// <param name="EntryPath">The path inside the archive.</param>
public sealed record DuplicateLocation(
    string Display, long Size, string Hash, bool InUse = false,
    string? FilePath = null, string? ArchivePath = null, string? EntryPath = null);

/// <summary>One path inside the game folders that exists in several places; only one of them is used.</summary>
public sealed record DuplicateGroup(string Path, IReadOnlyList<DuplicateLocation> Locations)
{
    public bool Identical => Locations.Select(l => l.Hash).Distinct().Count() == 1;
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>A copy of a file in the Assets folder (Find duplicates in the Local Assets folders).</summary>
/// <param name="Folder">Its folder relative to the Assets folder, e.g. "models/16 Oct 2026".</param>
public sealed record AssetCopy(string FullPath, string Folder, DateTime Modified);

/// <summary>Files in the Assets folder with the same name, size and contents: exact copies of each other.</summary>
public sealed record AssetCopyGroup(string Name, long Size, IReadOnlyList<AssetCopy> Copies);

/// <summary>Settings &gt; Tools: convert file names to lower case, and find files that hide or repeat each other.</summary>
public sealed partial class AssetManager
{
    // ---------- Convert to lower case ----------

    /// <summary>
    /// Files with upper-case letters in the game folders of every asset type (shown or hidden), and anywhere in
    /// the Assets folder.
    /// </summary>
    public IReadOnlyList<CaseFix> FindUpperCaseNames()
    {
        var result = new List<CaseFix>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in AssetType.Known)
        {
            var active = ActiveFolder(type);
            if (!Directory.Exists(active) || !seen.Add(Path.GetFullPath(active))) continue;
            foreach (var file in Directory.EnumerateFiles(active).Order(StringComparer.OrdinalIgnoreCase))
                AddIfUpper(file, Settings.QuakeFolder, inAssets: false);
        }
        if (Directory.Exists(Settings.AssetsFolder))
        {
            var staging = Path.Combine(Settings.AssetsFolder, ".qam");
            foreach (var file in Directory.EnumerateFiles(Settings.AssetsFolder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                if (!file.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    AddIfUpper(file, Path.GetDirectoryName(Settings.AssetsFolder.TrimEnd(Path.DirectorySeparatorChar)) ?? Settings.AssetsFolder, inAssets: true);
        }
        return result;

        void AddIfUpper(string file, string root, bool inAssets)
        {
            var name = Path.GetFileName(file);
            if (name != name.ToLowerInvariant())
                result.Add(new CaseFix(file, Path.GetRelativePath(root, Path.GetDirectoryName(file)!), name, inAssets));
        }
    }

    /// <summary>
    /// Rename the files to lower case. A file is skipped when a different file already has the lower-case name.
    /// Returns how many were renamed and the skipped ones.
    /// </summary>
    public (int Renamed, IReadOnlyList<CaseFix> Skipped) ConvertToLowerCase(IEnumerable<CaseFix> fixes)
    {
        var renamed = 0;
        var skipped = new List<CaseFix>();
        foreach (var fix in fixes)
        {
            var dir = Path.GetDirectoryName(fix.FullPath)!;
            if (!File.Exists(fix.FullPath))
                continue;
            // On Windows File.Exists ignores case, so look for an exact, different entry.
            var clash = Directory.EnumerateFileSystemEntries(dir).Any(e => Path.GetFileName(e) == fix.NewName);
            if (clash)
            {
                skipped.Add(fix);
                continue;
            }
            Forget(fix.FullPath);
            var temp = Path.Combine(dir, fix.NewName + ".renaming-" + Guid.NewGuid().ToString("N")[..6]);
            File.Move(fix.FullPath, temp);
            File.Move(temp, Path.Combine(dir, fix.NewName));
            renamed++;
        }
        return (renamed, skipped);
    }

    // ---------- Find duplicates in the Quake folder ----------

    private sealed record Found(string GameDir, string Display, long Size, string? File, string? Archive, ArchiveEntry? Entry);

    /// <summary>The game folders ezQuake loads, highest priority first (fs.c: id1, then ezquake, then qw, each put in front).</summary>
    public static readonly string[] LoadedGameDirs = ["qw", "ezquake", "id1"];

    [GeneratedRegex(@"^pak(\d+)\.(pak|pk3)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedPak();

    /// <summary>
    /// Priority inside one game folder, highest first: the loose file, then the other .pk3 files (later names first),
    /// pakN.pk3 (higher numbers first), the other .pak files, then pakN.pak.
    /// </summary>
    private static (int Group, int Number, string Name) AddOrder(string? archive)
    {
        if (archive is null) return (4, 0, ""); // loose files take precedence over archives
        var name = Path.GetFileName(archive);
        var pk3 = name.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase);
        var m = NumberedPak().Match(name);
        return m.Success
            ? (pk3 ? 2 : 0, int.Parse(m.Groups[1].Value), "")
            : (pk3 ? 3 : 1, 0, name.ToLowerInvariant());
    }

    /// <summary>
    /// The copy ezQuake loads among several with the same path: the highest game folder (qw, then ezquake, then id1;
    /// other folders only load with -game), and inside it the loose file, then the archives in the order above.
    /// </summary>
    private static Found? InUse(IReadOnlyList<Found> found)
    {
        var loaded = found.Where(f => Array.FindIndex(LoadedGameDirs, d => d.Equals(f.GameDir, StringComparison.OrdinalIgnoreCase)) >= 0).ToList();
        if (loaded.Count == 0) return null;
        var bestDir = loaded.Min(f => Array.FindIndex(LoadedGameDirs, d => d.Equals(f.GameDir, StringComparison.OrdinalIgnoreCase)));
        return loaded
            .Where(f => f.GameDir.Equals(LoadedGameDirs[bestDir], StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => AddOrder(f.Archive).Group)
            .ThenByDescending(f => AddOrder(f.Archive).Number)
            .ThenByDescending(f => AddOrder(f.Archive).Name, StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// Paths that exist more than once inside the game folders of the Quake folder (id1, qw, ezquake, mods), as
    /// loose files or inside .pak/.pk3 files, with the copy the client uses marked. The Assets folder is left out.
    /// Paths are compared ignoring case.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> FindDuplicates(CancellationToken ct = default)
    {
        var byPath = new Dictionary<string, List<Found>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Settings.QuakeFolder))
            return [];
        var skip = new[] { Settings.AssetsFolder, AppContext.BaseDirectory }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar))
            .ToList();

        foreach (var gameDir in Directory.EnumerateDirectories(Settings.QuakeFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var dirName = Path.GetFileName(gameDir);
            var full = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
            if (dirName.StartsWith('.') || skip.Any(s => Inside(full, s)))
                continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories).Where(f => !skip.Any(s => Inside(f, s))).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(gameDir, file).Replace('\\', '/');
                if (GameArchive.IsArchive(file) && !relative.Contains('/'))
                {
                    try
                    {
                        using var archive = GameArchive.Open(file);
                        foreach (var entry in archive.Entries)
                            Add(entry.Path, new Found(dirName, $"{dirName}/{relative} → {entry.Path}", entry.Size, null, file, entry));
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
                    continue;
                }
                long size;
                try { size = new FileInfo(file).Length; }
                catch (IOException) { continue; }
                Add(relative, new Found(dirName, $"{dirName}/{relative}", size, file, null, null));
            }
        }

        var groups = new List<DuplicateGroup>();
        var open = new Dictionary<string, GameArchive>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (path, found) in byPath.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var used = InUse(found);
                var locations = new List<DuplicateLocation>();
                foreach (var f in found)
                {
                    string hash;
                    try
                    {
                        if (f.File is not null)
                        {
                            hash = Hash(f.File);
                        }
                        else
                        {
                            if (!open.TryGetValue(f.Archive!, out var archive))
                                open[f.Archive!] = archive = GameArchive.Open(f.Archive!);
                            hash = Convert.ToHexString(SHA256.HashData(archive.ReadEntry(f.Entry!)));
                        }
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        hash = "?";
                    }
                    locations.Add(new DuplicateLocation(f.Display, f.Size, hash, f == used, f.File, f.Archive, f.Entry?.Path));
                }
                groups.Add(new DuplicateGroup(path.ToLowerInvariant(), locations));
            }
        }
        finally
        {
            foreach (var a in open.Values)
                a.Dispose();
        }
        return groups;

        void Add(string path, Found found)
        {
            if (!byPath.TryGetValue(path, out var list))
                byPath[path] = list = [];
            list.Add(found);
        }
    }

    /// <summary>The contents of one duplicate (a loose file or an archive entry), e.g. for a preview.</summary>
    public static byte[] ReadDuplicate(DuplicateLocation location)
    {
        if (location.FilePath is not null)
            return File.ReadAllBytes(location.FilePath);
        using var archive = GameArchive.Open(location.ArchivePath!);
        var entry = archive.Entries.First(e => e.Path.Equals(location.EntryPath, StringComparison.OrdinalIgnoreCase));
        return archive.ReadEntry(entry);
    }

    private static bool Inside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // ---------- Find duplicates in the Local Assets folders ----------

    /// <summary>
    /// Exact copies anywhere in the Assets folder (every type and folder, including assets/other): files with the
    /// same name, extension and size whose contents are identical too.
    /// </summary>
    public IReadOnlyList<AssetCopyGroup> FindAssetCopies(CancellationToken ct = default)
    {
        if (!Directory.Exists(Settings.AssetsFolder))
            return [];
        var staging = Path.Combine(Settings.AssetsFolder, ".qam");
        var candidates = Directory.EnumerateFiles(Settings.AssetsFolder, "*", SearchOption.AllDirectories)
            .Where(f => !Inside(f, staging))
            .Select(f => new FileInfo(f))
            .GroupBy(f => (Name: f.Name.ToLowerInvariant(), f.Length))
            .Where(g => g.Count() > 1);
        var groups = new List<AssetCopyGroup>();
        foreach (var g in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Same name and size isn't enough: the contents must match too.
            foreach (var same in g.GroupBy(f => { try { return Hash(f.FullName); } catch (IOException) { return "?" + f.FullName; } }).Where(s => s.Count() > 1))
            {
                var copies = same
                    .OrderBy(f => f.LastWriteTime)
                    .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new AssetCopy(f.FullName, Path.GetRelativePath(Settings.AssetsFolder, f.DirectoryName!), f.LastWriteTime))
                    .ToList();
                groups.Add(new AssetCopyGroup(same.First().Name, g.Key.Length, copies));
            }
        }
        return groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Send the given copies to the Recycle Bin (or trash). Returns how many were deleted.</summary>
    public int DeleteAssetCopies(IEnumerable<string> paths)
    {
        var n = 0;
        foreach (var p in paths.Where(File.Exists))
        {
            Delete(p);
            n++;
        }
        return n;
    }
}
