using System.Globalization;
using System.Security.Cryptography;
using QuakeAssetManager.Core.Archives;

namespace QuakeAssetManager.Core;

/// <param name="Backup">Assets folder holding a copy of this exact content, if any.</param>
public sealed record ActiveEntry(
    string Name, string FullPath, bool IsFolder, bool IsValid, string? Backup, int FileCount, DateTime Modified);

/// <summary>A subfolder found inside a game folder (e.g. qw/textures/models/original): usually alternative assets.</summary>
public sealed record FoundSubfolder(AssetType Type, string FullPath, string Name, int FileCount)
{
    /// <summary>Remembered in settings so the app doesn't ask twice.</summary>
    public string Key => $"{Type.Id}/{Name}";
}

public sealed record AssetFileEntry(string Name, string FullPath, bool IsValid, bool IsActive, DateTime Modified);

public sealed record AssetFolderEntry(
    string Name,
    string FullPath,
    bool IsValid,
    IReadOnlyList<AssetFolderEntry> Folders,
    IReadOnlyList<AssetFileEntry> Files,
    DateTime Modified);

public sealed record AssetsListing(IReadOnlyList<AssetFolderEntry> Packs, IReadOnlyList<AssetFileEntry> RootFiles);

public sealed record ImportResult(int Copied, int Skipped, IReadOnlyList<string> CreatedFolders)
{
    public static ImportResult operator +(ImportResult a, ImportResult b) =>
        new(a.Copied + b.Copied, a.Skipped + b.Skipped, [.. a.CreatedFolders, .. b.CreatedFolders]);

    public static readonly ImportResult Empty = new(0, 0, []);
}

public sealed record UseFolderPlan(
    IReadOnlyList<string> FilesToCopy,
    IReadOnlyList<string> UncoveredActiveFiles,
    int InvalidSkipped);

/// <summary>
/// All file operations on the active folders (qw/textures/...) and the assets folder.
/// Rules: the assets folder always keeps a copy; nothing lives directly in assets/&lt;type&gt;/;
/// files copied into the game are lower case; archives are never modified.
/// </summary>
public sealed class AssetManager
{
    private readonly Dictionary<(string Path, long Length, DateTime Modified, DateTime Created), string> _hashCache = [];
    private readonly object _hashLock = new();

    public AppSettings Settings { get; }
    public IReadOnlyDictionary<string, ValidNameSet> ValidNames { get; private set; }
    public GameData GameData { get; private set; }
    public AssetConfig? Config { get; }

    public AssetManager(AppSettings settings, GameData gameData, AssetConfig? config = null)
    {
        Settings = settings;
        GameData = gameData;
        Config = config;
        // Hidden types are built too, so dropped files can be recognised as belonging to them.
        ValidNames = AssetType.Known.ToDictionary(t => t.Id, t => ValidNameSet.Build(t, gameData, config?.LoadNames(t)), StringComparer.OrdinalIgnoreCase);
    }

    public string ActiveFolder(AssetType type) => type.ActiveFolder(Settings.QuakeFolder);
    public string AssetsFolder(AssetType type) => type.AssetsFolder(Settings.AssetsFolder);
    public bool ActiveFolderExists(AssetType type) => Directory.Exists(ActiveFolder(type));

    public void CreateActiveFolder(AssetType type) => Directory.CreateDirectory(ActiveFolder(type));

    public void EnsureAssetsFolders()
    {
        // 0.4: assets/conbacks became assets/gfx; the placed-files list is no longer used.
        try
        {
            var oldGfx = Path.Combine(Settings.AssetsFolder, "conbacks");
            var newGfx = Path.Combine(Settings.AssetsFolder, "gfx");
            if (Directory.Exists(oldGfx) && !Directory.Exists(newGfx))
                Directory.Move(oldGfx, newGfx);
            var placed = Path.Combine(Settings.AssetsFolder, ".qam", "placed.json");
            if (File.Exists(placed))
                File.Delete(placed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var t in AssetType.All)
            Directory.CreateDirectory(AssetsFolder(t));
    }

    public bool IsValid(AssetType type, string fileName) => ValidNames[type.Kind].IsValidFileName(fileName);

    // ---------- Listing ----------

    public IReadOnlyList<ActiveEntry> ListActive(AssetType type)
    {
        var folder = ActiveFolder(type);
        if (!Directory.Exists(folder))
            return [];
        var index = BuildIndex(type);
        var result = new List<ActiveEntry>();
        foreach (var dir in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var count = SafeCount(dir);
            result.Add(new ActiveEntry(Path.GetFileName(dir), dir, true, false, null, count, NewestWrite(dir)));
        }
        foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            var hash = Hash(file);
            var backup = FolderOf(type, name, hash, index);
            result.Add(new ActiveEntry(name, file, false, IsValid(type, name), backup, 0, File.GetLastWriteTime(file)));
        }
        return result;
    }

    public AssetsListing ListAssets(AssetType type)
    {
        var root = AssetsFolder(type);
        if (!Directory.Exists(root))
            return new AssetsListing([], []);
        var activeHashes = ActiveHashesByName(type);
        var packs = Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase)
            .Select(d => ListFolder(type, d, isPack: true, activeHashes)).ToList();
        var rootFiles = Directory.EnumerateFiles(root).Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => new AssetFileEntry(Path.GetFileName(f), f, false, false, File.GetLastWriteTime(f))).ToList();
        return new AssetsListing(packs, rootFiles);
    }

    private AssetFolderEntry ListFolder(AssetType type, string dir, bool isPack, Dictionary<string, string> activeHashes)
    {
        // Only files directly inside a pack folder can be used; deeper folders are shown as invalid.
        var folders = Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase)
            .Select(d => ListFolder(type, d, isPack: false, activeHashes)).ToList();
        var files = Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase).Select(f =>
        {
            var name = Path.GetFileName(f);
            var valid = isPack && IsValid(type, name);
            var active = valid && activeHashes.TryGetValue(name.ToLowerInvariant(), out var h) && h == Hash(f);
            return new AssetFileEntry(name, f, valid, active, File.GetLastWriteTime(f));
        }).ToList();
        return new AssetFolderEntry(Path.GetFileName(dir), dir, isPack, folders, files, NewestWrite(dir));
    }

    private Dictionary<string, string> ActiveHashesByName(AssetType type)
    {
        var map = new Dictionary<string, string>();
        var folder = ActiveFolder(type);
        if (!Directory.Exists(folder))
            return map;
        foreach (var f in Directory.EnumerateFiles(folder))
            map[Path.GetFileName(f).ToLowerInvariant()] = Hash(f);
        return map;
    }

    private static DateTime NewestWrite(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime).DefaultIfEmpty(Directory.GetLastWriteTime(dir)).Max();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Directory.GetLastWriteTime(dir);
        }
    }

    /// <summary>The Assets folder that holds a copy of this content (preferring one with the same file name).</summary>
    private string? FolderOf(AssetType type, string name, string hash, Dictionary<string, List<string>> index)
    {
        if (!index.TryGetValue(hash, out var copies) || copies.Count == 0)
            return null;
        var root = AssetsFolder(type);
        var best = copies.FirstOrDefault(c => Path.GetFileName(c).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? copies[0];
        var relative = Path.GetRelativePath(root, best);
        var sep = relative.IndexOfAny(['/', '\\']);
        return sep < 0 ? null : relative[..sep];
    }

    private static int SafeCount(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    // ---------- Hashing ----------

    public string Hash(string file)
    {
        var info = new FileInfo(file);
        // Creation time too: a replaced file can have the same size and (copied) modified time.
        var key = (info.FullName, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
        lock (_hashLock)
        {
            if (_hashCache.TryGetValue(key, out var cached))
                return cached;
        }
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        lock (_hashLock)
            _hashCache[key] = hash;
        return hash;
    }

    /// <summary>
    /// Drop the cached hash of a path the app just wrote, moved or deleted. Timestamps alone can't be trusted:
    /// Windows gives a file re-created under the same name its old creation time ("file tunneling").
    /// </summary>
    private void Forget(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_hashLock)
            foreach (var key in _hashCache.Keys.Where(k => k.Path.StartsWith(full, StringComparison.OrdinalIgnoreCase)).ToList())
                _hashCache.Remove(key);
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Content hash → every copy under assets/&lt;type&gt;/.</summary>
    public Dictionary<string, List<string>> BuildIndex(AssetType type)
    {
        var index = new Dictionary<string, List<string>>();
        var root = AssetsFolder(type);
        if (!Directory.Exists(root))
            return index;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string h;
            try { h = Hash(file); }
            catch (IOException) { continue; }
            if (!index.TryGetValue(h, out var list))
                index[h] = list = [];
            list.Add(file);
        }
        return index;
    }

    // ---------- Import (scan) ----------

    public static string DatedFolderName(DateTime date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Copy the loose files of every active folder into the assets folder (a new dated folder), skipping content
    /// that is already there. Subfolders are not copied here: see <see cref="FindSubfolders"/>.
    /// </summary>
    public ImportResult ImportAll(DateTime now)
    {
        EnsureAssetsFolders();
        var total = ImportResult.Empty;
        foreach (var type in AssetType.All)
            total += Import(type, now);
        if (!Settings.ScannedQuakeFolders.Contains(Settings.QuakeFolder, StringComparer.OrdinalIgnoreCase))
            Settings.ScannedQuakeFolders.Add(Settings.QuakeFolder);
        return total;
    }

    public ImportResult Import(AssetType type, DateTime now)
    {
        var active = ActiveFolder(type);
        var root = AssetsFolder(type);
        if (!Directory.Exists(active))
            return ImportResult.Empty;
        Directory.CreateDirectory(root);
        var index = BuildIndex(type);
        int copied = 0, skipped = 0;
        var created = new List<string>();
        string? datedFolder = null;

        foreach (var file in Directory.EnumerateFiles(active))
        {
            if (IsJunk(file))
                continue;
            string hash;
            try { hash = Hash(file); }
            catch (IOException) { continue; }
            if (index.ContainsKey(hash))
            {
                skipped++;
                continue;
            }

            datedFolder ??= UniqueFolderName(root, DatedFolderName(now));
            var targetDir = Path.Combine(root, datedFolder);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                created.Add(Path.GetRelativePath(root, targetDir));
            }
            var target = UniqueFilePath(targetDir, Path.GetFileName(file));
            File.Copy(file, target);
            index[hash] = [target];
            copied++;
        }
        return new ImportResult(copied, skipped, created);
    }

    private static bool IsJunk(string file) =>
        Path.GetFileName(file).ToLowerInvariant() is "thumbs.db" or "desktop.ini" or ".ds_store";

    /// <summary>Subfolders of the shown types' game folders that haven't been imported or dismissed yet.</summary>
    public IReadOnlyList<FoundSubfolder> FindSubfolders()
    {
        var handled = HandledSubfolders();
        var result = new List<FoundSubfolder>();
        foreach (var type in AssetType.All)
        {
            var active = ActiveFolder(type);
            if (!Directory.Exists(active)) continue;
            foreach (var dir in Directory.EnumerateDirectories(active).Order(StringComparer.OrdinalIgnoreCase))
            {
                var found = new FoundSubfolder(type, dir, Path.GetFileName(dir), SafeCount(dir));
                if (found.FileCount > 0 && !handled.Contains(found.Key))
                    result.Add(found);
            }
        }
        return result;
    }

    private List<string> HandledSubfolders()
    {
        if (!Settings.HandledSubfolders.TryGetValue(Settings.QuakeFolder, out var list))
            Settings.HandledSubfolders[Settings.QuakeFolder] = list = [];
        return list;
    }

    /// <summary>Don't ask about these again (they were imported, or the user said so).</summary>
    public void MarkSubfoldersHandled(IEnumerable<FoundSubfolder> folders)
    {
        var list = HandledSubfolders();
        foreach (var f in folders)
            if (!list.Contains(f.Key, StringComparer.OrdinalIgnoreCase))
                list.Add(f.Key);
    }

    /// <summary>Forget the answers, so every subfolder is offered again.</summary>
    public void ResetHandledSubfolders() => Settings.HandledSubfolders.Remove(Settings.QuakeFolder);

    /// <summary>
    /// Copy subfolders to assets/&lt;type&gt;/&lt;same name&gt; (a suffix if that exists), skipping content already in
    /// Assets. With <paramref name="deleteOriginals"/> the originals then go to the Recycle Bin.
    /// </summary>
    public ImportResult ImportSubfolders(IEnumerable<FoundSubfolder> folders, bool deleteOriginals)
    {
        var total = ImportResult.Empty;
        foreach (var f in folders)
        {
            var root = AssetsFolder(f.Type);
            Directory.CreateDirectory(root);
            var index = BuildIndex(f.Type);
            var name = UniqueFolderName(root, f.Name);
            int copied = 0, skipped = 0;
            foreach (var file in Directory.EnumerateFiles(f.FullPath, "*", SearchOption.AllDirectories))
            {
                if (IsJunk(file)) continue;
                var hash = Hash(file);
                if (index.ContainsKey(hash))
                {
                    skipped++;
                    continue;
                }
                var target = Path.Combine(root, name, Path.GetRelativePath(f.FullPath, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                target = UniqueFilePathFor(target);
                File.Copy(file, target);
                index[hash] = [target];
                copied++;
            }
            total += new ImportResult(copied, skipped, copied > 0 ? [Path.Combine(f.Type.FolderName, name)] : []);
            if (deleteOriginals)
                Trash.Send(f.FullPath);
        }
        MarkSubfoldersHandled(folders);
        return total;
    }

    /// <summary>Every file in Assets (this type) with the same content as <paramref name="file"/>.</summary>
    public IReadOnlyList<string> FindCopiesInAssets(AssetType type, string file)
    {
        if (!File.Exists(file)) return [];
        var hash = Hash(file);
        return BuildIndex(type).TryGetValue(hash, out var copies) ? copies : [];
    }

    // ---------- Use ----------

    /// <summary>Copy one asset file into the game, lower case, replacing any version with the same name.</summary>
    public string UseFile(AssetType type, string assetFile)
    {
        var active = ActiveFolder(type);
        Directory.CreateDirectory(active);
        var name = Path.GetFileName(assetFile).ToLowerInvariant();
        RemoveActiveVersions(type, name);
        var target = Path.Combine(active, name);
        File.Copy(assetFile, target, overwrite: true);
        Forget(target);
        return target;
    }

    public UseFolderPlan PlanUseFolder(AssetType type, string assetFolder)
    {
        var files = Directory.EnumerateFiles(assetFolder).ToList();
        var valid = files.Where(f => IsValid(type, Path.GetFileName(f))).ToList();
        var covered = new HashSet<string>(valid.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
        var uncovered = new List<string>();
        if (Directory.Exists(ActiveFolder(type)))
        {
            uncovered.AddRange(Directory.EnumerateFiles(ActiveFolder(type))
                .Where(f => IsValid(type, Path.GetFileName(f)) && !covered.Contains(Path.GetFileNameWithoutExtension(f)))
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        return new UseFolderPlan(valid, uncovered, files.Count - valid.Count);
    }

    /// <summary>Copy every valid file of a folder into the game and remove the active files the user chose.</summary>
    public int UseFolder(AssetType type, UseFolderPlan plan, IEnumerable<string> activeFilesToRemove)
    {
        foreach (var file in activeFilesToRemove)
            RemoveFromGame(type, file);
        foreach (var file in plan.FilesToCopy)
            UseFile(type, file);
        return plan.FilesToCopy.Count;
    }

    /// <summary>Delete an active file after making sure the assets folder has a copy.</summary>
    public void RemoveFromGame(AssetType type, string activeFile)
    {
        if (!File.Exists(activeFile))
            return;
        EnsureBackedUp(type, activeFile);
        File.Delete(activeFile);
        Forget(activeFile);
    }

    /// <summary>
    /// Before copying <paramref name="name"/> into the game: remove the same name, and for images the same name with
    /// another image extension (the client would pick the .tga over a new .png). Models are only replaced by name,
    /// so using b_g_key.mdl never removes b_g_key.md3 or its skin.
    /// </summary>
    private void RemoveActiveVersions(AssetType type, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var isImage = ValidNameSet.Extensions.Contains(Path.GetExtension(name).ToLowerInvariant());
        foreach (var existing in Directory.EnumerateFiles(ActiveFolder(type)))
        {
            var existingName = Path.GetFileName(existing);
            var sameName = existingName.Equals(name, StringComparison.OrdinalIgnoreCase);
            var sameImage = isImage && Path.GetFileNameWithoutExtension(existing).Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                            ValidNameSet.Extensions.Contains(Path.GetExtension(existing).ToLowerInvariant());
            if (sameName || sameImage)
                RemoveFromGame(type, existing);
        }
    }

    private void EnsureBackedUp(AssetType type, string activeFile)
    {
        var index = BuildIndex(type);
        if (index.ContainsKey(Hash(activeFile)))
            return;
        var root = AssetsFolder(type);
        var dir = Path.Combine(root, UniqueFolderName(root, DatedFolderName(DateTime.Now), reuseExisting: true));
        Directory.CreateDirectory(dir);
        File.Copy(activeFile, UniqueFilePath(dir, Path.GetFileName(activeFile)));
    }

    // ---------- Move, rename, delete ----------

    /// <summary>Move an active file or folder into an assets subfolder. The active folder ends up without it.</summary>
    public void MoveActiveToAssets(AssetType type, string activePath, string targetFolder, string? targetFileName)
    {
        Forget(activePath);
        ValidateFolderName(targetFolder);
        var destDir = Path.Combine(AssetsFolder(type), targetFolder);
        Directory.CreateDirectory(destDir);
        if (Directory.Exists(activePath))
        {
            foreach (var file in Directory.EnumerateFiles(activePath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(activePath, file);
                var dest = Path.Combine(destDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest) && Hash(dest) == Hash(file))
                    continue;
                File.Copy(file, UniqueFilePath(Path.GetDirectoryName(dest)!, Path.GetFileName(dest)));
            }
            Directory.Delete(activePath, recursive: true);
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(targetFileName) ? Path.GetFileName(activePath) : targetFileName.Trim();
            ValidateFileName(name);
            var dest = Path.Combine(destDir, name);
            if (!(File.Exists(dest) && Hash(dest) == Hash(activePath)))
                File.Copy(activePath, UniqueFilePath(destDir, name));
            File.Delete(activePath);
        }
    }

    /// <summary>Move a file or folder inside the assets folder into another pack folder.</summary>
    public string MoveWithinAssets(AssetType type, string assetPath, string targetFolder)
    {
        Forget(assetPath);
        ValidateFolderName(targetFolder);
        var destDir = Path.Combine(AssetsFolder(type), targetFolder);
        Directory.CreateDirectory(destDir);
        var name = Path.GetFileName(assetPath.TrimEnd(Path.DirectorySeparatorChar));
        if (Directory.Exists(assetPath))
        {
            var dest = UniqueFolderPath(destDir, name);
            Directory.Move(assetPath, dest);
            return dest;
        }
        var destFile = UniqueFilePath(destDir, name);
        File.Move(assetPath, destFile);
        return destFile;
    }

    /// <summary>Rename a file or folder in Assets; a renamed option folder keeps its "from" labels.</summary>
    public string RenameAsset(AssetType type, string path, string newName)
    {
        var isPack = Directory.Exists(path) &&
                     string.Equals(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)), AssetsFolder(type), StringComparison.OrdinalIgnoreCase);
        return Rename(path, newName);
    }

    public string Rename(string path, string newName)
    {
        newName = newName.Trim();
        Forget(path);
        var isFolder = Directory.Exists(path);
        if (isFolder) ValidateFolderName(newName);
        else ValidateFileName(newName);
        var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar))!;
        var dest = Path.Combine(parent, newName);
        var sameIgnoringCase = string.Equals(Path.GetFullPath(dest), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        if (!sameIgnoringCase && (File.Exists(dest) || Directory.Exists(dest)))
            throw new IOException($"\"{newName}\" already exists here.");
        if (isFolder)
        {
            if (sameIgnoringCase)
            {
                // Case-only rename on a case-insensitive filesystem needs a temporary name.
                var temp = dest + ".renaming";
                Directory.Move(path, temp);
                Directory.Move(temp, dest);
            }
            else
            {
                Directory.Move(path, dest);
            }
        }
        else
        {
            File.Move(path, dest, overwrite: sameIgnoringCase);
        }
        return dest;
    }

    public void Delete(string path)
    {
        Trash.Send(path);
        Forget(path);
    }

    public IReadOnlyList<string> PackFolderNames(AssetType type)
    {
        var root = AssetsFolder(type);
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    public string NewDatedFolderName(AssetType type) =>
        UniqueFolderName(AssetsFolder(type), DatedFolderName(DateTime.Now), reuseExisting: true);

    // ---------- Archives ----------

    /// <summary>Archives in the Quake folder, grouped by game folder (id1, qw, ezquake, ...).</summary>
    public IReadOnlyList<(string GameDir, IReadOnlyList<string> Archives)> ListArchives()
    {
        var result = new List<(string, IReadOnlyList<string>)>();
        if (!Directory.Exists(Settings.QuakeFolder))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(Settings.QuakeFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            List<string> archives;
            try { archives = Directory.EnumerateFiles(dir).Where(GameArchive.IsArchive).Order(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (UnauthorizedAccessException) { continue; }
            if (archives.Count > 0)
                result.Add((Path.GetFileName(dir), archives));
        }
        return result;
    }

    /// <summary>Only qw archives can be extracted into the assets folder.</summary>
    public bool CanExtract(string archivePath) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(archivePath)), "qw", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copy texture entries (textures/models|bmodels|wad/...) to assets/&lt;type&gt;/&lt;archive name&gt;/.
    /// Content already in the assets folder is skipped.
    /// </summary>
    public ImportResult ExtractFromArchive(GameArchive archive, IEnumerable<ArchiveEntry> entries)
    {
        if (!CanExtract(archive.FilePath))
            throw new InvalidOperationException("Only archives in the qw folder can be extracted.");
        var folderName = Path.GetFileNameWithoutExtension(archive.FilePath);
        var indexes = new Dictionary<string, Dictionary<string, List<string>>>();
        int copied = 0, skipped = 0;
        var created = new List<string>();
        foreach (var entry in entries)
        {
            var type = AssetType.FromArchivePath(entry.Path);
            if (type is null)
                continue;
            if (!indexes.TryGetValue(type.Kind, out var index))
                indexes[type.Kind] = index = BuildIndex(type);
            var data = archive.ReadEntry(entry);
            var hash = Hash(data);
            if (index.ContainsKey(hash))
            {
                skipped++;
                continue;
            }
            var inner = entry.Path[type.ArchivePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var targetDir = Path.Combine(AssetsFolder(type), folderName, Path.GetDirectoryName(inner) ?? "");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                created.Add(Path.GetRelativePath(Settings.AssetsFolder, targetDir));
            }
            var target = UniqueFilePath(targetDir, Path.GetFileName(inner));
            File.WriteAllBytes(target, data);
            index[hash] = [target];
            copied++;
        }
        return new ImportResult(copied, skipped, created);
    }

    // ---------- Helpers ----------

    private static readonly char[] InvalidNameChars = [.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public static string? NameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name.Trim() is "." or "..")
            return "That name isn't allowed.";
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            return "A name can't contain / \\ : * ? \" < > |";
        return null;
    }

    private static void ValidateFileName(string name)
    {
        if (NameError(name) is { } error)
            throw new ArgumentException(error);
    }

    private static void ValidateFolderName(string name) => ValidateFileName(name);

    private static string UniqueFolderName(string root, string name, bool reuseExisting = false)
    {
        if (reuseExisting || !Directory.Exists(Path.Combine(root, name)))
            return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!Directory.Exists(Path.Combine(root, candidate)))
                return candidate;
        }
    }

    private static string UniqueFolderPath(string parent, string name) => Path.Combine(parent, UniqueFolderName(parent, name));

    public static string UniqueFilePathFor(string fullPath) =>
        UniqueFilePath(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath));

    private static string UniqueFilePath(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        if (!File.Exists(path))
            return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(path))
                return path;
        }
    }
}
