using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using QuakeAssetManager.Core.Archives;

namespace QuakeAssetManager.Core.Import;

/// <summary>A file found in something the user dropped, downloaded or picked in an archive. Path uses '/' separators.</summary>
/// <param name="InArchive">It came from inside a pak/pk3/zip: its folder there decides, and unknown folders are asked about.</param>
public sealed record SourceFile(string Path, Func<byte[]> Read, bool InArchive = false);

/// <summary>What happened to one import, file by file, before it is committed.</summary>
public sealed class ImportSession
{
    /// <param name="Kind">The asset type id, or <see cref="Importer.Other"/> for files kept in assets/other.</param>
    public sealed record Staged(string Kind, string StagedPath, string FinalPath, string Folder, bool Invalid = false);

    public required string Title { get; init; }
    public required string StagingDir { get; init; }
    public List<Staged> Files { get; set; } = [];
    public int Duplicates { get; set; }
    /// <summary>Files in known folders the app doesn't manage (sound, maps, map textures...), by folder.</summary>
    public SortedDictionary<string, int> NotManaged { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"path — reason" for every file that was left out.</summary>
    public List<string> Ignored { get; set; } = [];
    public List<string> Errors { get; set; } = [];

    public int AddedCount(string kind) => Files.Count(f => f.Kind == kind);
    public int InvalidCount(string kind) => Files.Count(f => f.Kind == kind && f.Invalid);

    /// <summary>The shown asset type and folder that received the most files (where the app jumps after OK).</summary>
    public (string Kind, string FolderPath)? Busiest() => Files
        .Where(f => AssetType.ById(f.Kind)?.Visible == true)
        .GroupBy(f => (f.Kind, FolderPath: FolderOf(f)))
        .OrderByDescending(g => g.Count())
        .Select(g => ((string Kind, string FolderPath)?)g.Key)
        .FirstOrDefault();

    private static string FolderOf(Staged f)
    {
        // FinalPath = assets/<type>/<folder>/<relative...>; walk up to the folder named f.Folder.
        var dir = Path.GetDirectoryName(f.FinalPath)!;
        while (!string.Equals(Path.GetFileName(dir), f.Folder, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(dir) is { } parent)
            dir = parent;
        return dir;
    }

    public IReadOnlyList<string> FoldersFor(string kind) =>
        Files.Where(f => f.Kind == kind).Select(f => f.Folder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Move the staged files into their Assets folders.</summary>
    public int Commit()
    {
        var moved = 0;
        foreach (var f in Files)
        {
            if (!File.Exists(f.StagedPath))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(f.FinalPath)!);
            var target = AssetManager.UniqueFilePathFor(f.FinalPath);
            File.Move(f.StagedPath, target);
            moved++;
        }
        DeleteStaging();
        return moved;
    }

    /// <summary>Throw the staged files away. Nothing reaches the Assets folders.</summary>
    public void Revert() => DeleteStaging();

    private void DeleteStaging()
    {
        try
        {
            if (Directory.Exists(StagingDir))
                Directory.Delete(StagingDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal void SaveManifest() =>
        File.WriteAllText(Path.Combine(StagingDir, "session.json"), JsonSerializer.Serialize(this, PlainJson.Files.ImportSession));

    public static ImportSession? LoadManifest(string stagingDir)
    {
        try
        {
            var path = Path.Combine(stagingDir, "session.json");
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), PlainJson.Files.ImportSession) : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>Files that need the user to say which asset type they belong to, one per folder.</summary>
/// <param name="Key">Identifies the folder; pass it back in the answers.</param>
/// <param name="Folder">The folder inside the archive or drop ("" for the root).</param>
/// <param name="Suggested">The one type the file names point to, pre-selected; null means Skip.</param>
public sealed record ImportQuestion(string Key, string Source, string Folder, IReadOnlyList<string> FileNames, AssetType? Suggested);

/// <summary>Everything found and classified, before anything is written. Answer the questions, then stage.</summary>
public sealed class ImportPlan
{
    internal sealed record Item(SourceFile File, Classification Class, string QuestionKey);
    internal sealed record Group(string Name, bool Dated, List<Item> Items);

    public required string Title { get; init; }
    internal List<Group> Groups { get; } = [];

    /// <summary>The Assets subfolder the files go to (the archive or folder name), unless the user picks another.</summary>
    public string? FolderName => Groups.FirstOrDefault(g => !g.Dated)?.Name;
    internal List<string> Ignored { get; } = [];
    internal List<string> Errors { get; } = [];

    public IReadOnlyList<ImportQuestion> Questions => Groups
        .SelectMany(g => g.Items.Where(i => i.Class.NeedsChoice).Select(i => (Group: g, Item: i)))
        .GroupBy(x => x.Item.QuestionKey)
        .Select(q =>
        {
            var suggestions = q.Select(x => x.Item.Class.Suggested?.Id).Distinct().ToList();
            var suggested = suggestions.Count == 1 && suggestions[0] is { } id ? AssetType.ById(id) : null;
            var first = q.First();
            var folder = Path.GetDirectoryName(first.Item.File.Path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            return new ImportQuestion(q.Key, first.Group.Name, folder, q.Select(x => Path.GetFileName(x.Item.File.Path)).ToList(), suggested);
        })
        .ToList();
}

/// <summary>
/// Imports dropped files, folders and archives (zip, pk3, pak, rar, 7z, tar, tar.gz) and files picked in the
/// Archives tab into the Assets folders. Everything is staged in assets/.qam/staging first so it can be reverted.
/// </summary>
public sealed class Importer(AssetManager manager)
{
    /// <summary>The answer, and the staged kind, for files kept in assets/other.</summary>
    public const string Other = "other";

    public static readonly string[] ArchiveExtensions = [".zip", ".pk3", ".pak", ".rar", ".7z", ".tar", ".tar.gz", ".tgz"];

    public string StagingRoot => Path.Combine(manager.Settings.AssetsFolder, ".qam", "staging");

    public static bool IsArchive(string path) => ArchiveExtension(path) is not null;

    private static string? ArchiveExtension(string path)
    {
        var lower = path.ToLowerInvariant();
        return ArchiveExtensions.OrderByDescending(e => e.Length).FirstOrDefault(lower.EndsWith);
    }

    /// <summary>Folder name for an archive: its file name without the archive extension.</summary>
    public static string ArchiveBaseName(string path)
    {
        var name = Path.GetFileName(path);
        var ext = ArchiveExtension(name);
        return ext is null ? Path.GetFileNameWithoutExtension(name) : name[..^ext.Length];
    }

    /// <summary>Read and classify what was dropped. Nothing is written yet.</summary>
    public ImportPlan Prepare(IReadOnlyList<string> droppedPaths, DateTime now, string? title = null)
    {
        var plan = new ImportPlan
        {
            Title = title ?? (droppedPaths.Count == 1 ? Path.GetFileName(droppedPaths[0].TrimEnd(Path.DirectorySeparatorChar, '/')) : $"{droppedPaths.Count} items"),
        };
        var datedFolder = AssetManager.DatedFolderName(now);
        foreach (var dropped in droppedPaths)
        {
            if (Directory.Exists(dropped))
                AddGroup(plan, Path.GetFileName(dropped.TrimEnd(Path.DirectorySeparatorChar, '/')), false, FolderFiles(dropped));
            else if (File.Exists(dropped) && IsArchive(dropped))
                AddGroup(plan, ArchiveBaseName(dropped), false, ArchiveFiles(dropped, plan.Errors));
            else if (File.Exists(dropped))
                AddGroup(plan, datedFolder, true, [new SourceFile(Path.GetFileName(dropped), () => File.ReadAllBytes(dropped))]);
            else
                plan.Ignored.Add($"{dropped} — not found");
        }
        return plan;
    }

    /// <summary>Files picked in the Archives tab; they go to assets/&lt;type&gt;/&lt;group&gt;.</summary>
    public ImportPlan Prepare(string title, string group, IEnumerable<SourceFile> files)
    {
        var plan = new ImportPlan { Title = title };
        AddGroup(plan, group, false, files);
        return plan;
    }

    private void AddGroup(ImportPlan plan, string name, bool dated, IEnumerable<SourceFile> files)
    {
        var group = new ImportPlan.Group(SafeFolderName(name), dated, []);
        foreach (var file in files)
        {
            var c = FileClassifier.Classify(file.Path, manager, file.InArchive);
            var folder = Path.GetDirectoryName(file.Path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            group.Items.Add(new ImportPlan.Item(file, c, $"{group.Name}|{folder}"));
        }
        plan.Groups.Add(group);
    }

    /// <summary>
    /// Stage the plan. <paramref name="answers"/> maps each question key to the chosen type id, <see cref="Other"/>,
    /// or null (or missing) to skip. <paramref name="folder"/> replaces the archive or folder name as the Assets subfolder.
    /// Call Commit or Revert on the result.
    /// </summary>
    public ImportSession Stage(ImportPlan plan, IReadOnlyDictionary<string, string?>? answers = null, string? folder = null)
    {
        var session = new ImportSession
        {
            Title = plan.Title,
            StagingDir = Path.Combine(StagingRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]),
        };
        Directory.CreateDirectory(session.StagingDir);
        session.Ignored.AddRange(plan.Ignored);
        session.Errors.AddRange(plan.Errors);
        var indexes = AssetType.Known.ToDictionary(t => t.Id, t => manager.BuildIndex(t).Keys.ToHashSet(), StringComparer.OrdinalIgnoreCase);
        var otherIndex = manager.BuildOtherIndex().Keys.ToHashSet();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chosenFolder = string.IsNullOrWhiteSpace(folder) ? null : SafeFolderName(folder.Trim());

        foreach (var group in plan.Groups)
        {
            var groupName = !group.Dated && chosenFolder is not null ? chosenFolder : group.Name;
            var folderByKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in group.Items)
            {
                var c = item.Class;
                var type = c.Type;
                var relative = c.RelativePath;
                var invalid = c.Invalid;
                var otherPath = c.OtherPath;
                if (c.NeedsChoice)
                {
                    var answer = answers is not null && answers.TryGetValue(item.QuestionKey, out var chosen) ? chosen : null;
                    if (answer == Other)
                    {
                        type = null;
                        otherPath = item.File.Path.Replace('\\', '/').TrimStart('/');
                    }
                    else
                    {
                        type = answer is null ? null : AssetType.ById(answer);
                        if (type is null)
                        {
                            session.Ignored.Add($"{item.File.Path} — skipped");
                            continue;
                        }
                        relative = Path.GetFileName(item.File.Path);
                        invalid = manager.ValidNames.TryGetValue(type.Id, out var set) && !set.IsValidFileName(relative);
                    }
                }
                if (type is null)
                {
                    if (otherPath is not null)
                        StageOther(session, item.File, otherPath, c.OtherFolder ?? Other, otherIndex);
                    else
                        session.Ignored.Add($"{item.File.Path} — {c.Reason}");
                    continue;
                }

                byte[] data;
                try { data = item.File.Read(); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    session.Errors.Add($"{item.File.Path}: {e.Message}");
                    continue;
                }
                var hash = Convert.ToHexString(SHA256.HashData(data));
                if (!indexes.TryGetValue(type.Id, out var index))
                    indexes[type.Id] = index = [];
                if (!index.Add(hash))
                {
                    session.Duplicates++;
                    continue;
                }

                if (!folderByKind.TryGetValue(type.Id, out var target))
                {
                    target = group.Dated ? group.Name : UniqueFolder(manager.AssetsFolder(type), groupName, type.Id, reserved);
                    folderByKind[type.Id] = target;
                }
                var rel = relative.Replace('/', Path.DirectorySeparatorChar);
                var staged = Path.Combine(session.StagingDir, type.FolderName, target, rel);
                if (File.Exists(staged))
                    staged = AssetManager.UniqueFilePathFor(staged);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.WriteAllBytes(staged, data);
                session.Files.Add(new ImportSession.Staged(type.Id, staged, Path.Combine(manager.AssetsFolder(type), target, rel), target, invalid));
            }
        }
        session.SaveManifest();
        return session;
    }

    /// <summary>
    /// A file the app doesn't manage (sound, maps, map textures...): kept in assets/other with the same path.
    /// Identical content is skipped; a different file with the same path gets a "(2)" suffix when committed.
    /// </summary>
    private void StageOther(ImportSession session, SourceFile file, string otherPath, string folder, HashSet<string> index)
    {
        var parts = otherPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(SafeFolderName).ToArray();
        if (parts.Length == 0) return;
        byte[] data;
        try { data = file.Read(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            session.Errors.Add($"{file.Path}: {e.Message}");
            return;
        }
        if (!index.Add(Convert.ToHexString(SHA256.HashData(data))))
        {
            session.Duplicates++;
            return;
        }
        var rel = Path.Combine(parts);
        var staged = Path.Combine(session.StagingDir, Other, rel);
        if (File.Exists(staged))
            staged = AssetManager.UniqueFilePathFor(staged);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllBytes(staged, data);
        session.Files.Add(new ImportSession.Staged(Other, staged, Path.Combine(manager.OtherFolder, rel), folder));
        session.NotManaged[folder] = session.NotManaged.GetValueOrDefault(folder) + 1;
    }

    /// <summary>Prepare and stage in one go; questions are skipped (tests, and imports that have none).</summary>
    public ImportSession Stage(IReadOnlyList<string> droppedPaths, DateTime now, string? title = null) =>
        Stage(Prepare(droppedPaths, now, title));

    /// <summary>Same, for files picked in an archive.</summary>
    public ImportSession StageFiles(string title, string group, IEnumerable<SourceFile> files) =>
        Stage(Prepare(title, group, files));

    /// <summary>Import sessions left behind when the app closed with the summary dialog open.</summary>
    public IReadOnlyList<ImportSession> FindLeftovers()
    {
        if (!Directory.Exists(StagingRoot))
            return [];
        var result = new List<ImportSession>();
        foreach (var dir in Directory.EnumerateDirectories(StagingRoot))
        {
            var session = ImportSession.LoadManifest(dir);
            if (session is not null && session.Files.Any(f => File.Exists(f.StagedPath)))
                result.Add(session);
            else
                try { Directory.Delete(dir, true); } catch (IOException) { }
        }
        return result;
    }

    private static string UniqueFolder(string root, string name, string kind, HashSet<string> reserved)
    {
        var candidate = name;
        for (var i = 2; Directory.Exists(Path.Combine(root, candidate)) || reserved.Contains($"{kind}/{candidate}"); i++)
            candidate = $"{name} ({i})";
        reserved.Add($"{kind}/{candidate}");
        return candidate;
    }

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "import" : clean;
    }

    private static IEnumerable<SourceFile> FolderFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (file.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var inner in ZipFiles(File.ReadAllBytes(file), relative + "/"))
                    yield return inner;
                continue;
            }
            var path = file;
            yield return new SourceFile(relative, () => File.ReadAllBytes(path));
        }
    }

    /// <summary>Entries of an archive. Zip and pk3 files inside it are opened too, one level deep.</summary>
    private static IEnumerable<SourceFile> ArchiveFiles(string path, List<string> errors)
    {
        List<SourceFile> files;
        try
        {
            files = ReadArchive(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or InvalidOperationException
                                      or SharpCompress.Common.ArchiveException or FormatException)
        {
            errors.Add($"{Path.GetFileName(path)} couldn't be opened: {e.Message}");
            return [];
        }

        var result = new List<SourceFile>();
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.Path).ToLowerInvariant();
            if (ext is ".pk3" or ".zip")
            {
                try
                {
                    result.AddRange(ZipFiles(f.Read(), f.Path + "/"));
                }
                catch (InvalidDataException e)
                {
                    errors.Add($"{f.Path} couldn't be opened: {e.Message}");
                }
                continue;
            }
            result.Add(f);
        }
        return result;
    }

    private static List<SourceFile> ReadArchive(string path)
    {
        var ext = ArchiveExtension(path);
        switch (ext)
        {
            case ".zip" or ".pk3":
                return ZipFiles(File.ReadAllBytes(path), "").ToList();
            case ".pak":
            {
                using var pak = new PakArchive(path);
                return pak.Entries.Select(e =>
                {
                    var bytes = pak.ReadEntry(e);
                    return new SourceFile(e.Path, () => bytes, InArchive: true);
                }).ToList();
            }
            case ".tar" or ".tar.gz" or ".tgz":
            {
                var files = new List<SourceFile>();
                using var file = File.OpenRead(path);
                using Stream stream = ext == ".tar" ? file : new GZipStream(file, CompressionMode.Decompress);
                using var reader = new TarReader(stream);
                while (reader.GetNextEntry() is { } entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                        continue;
                    using var ms = new MemoryStream();
                    entry.DataStream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    files.Add(new SourceFile(entry.Name.Replace('\\', '/').TrimStart('.', '/'), () => bytes, InArchive: true));
                }
                return files;
            }
            default:
            {
                // rar and 7z
                var files = new List<SourceFile>();
                using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(path, new SharpCompress.Readers.ReaderOptions());
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    using var s = entry.OpenEntryStream();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    var bytes = ms.ToArray();
                    files.Add(new SourceFile((entry.Key ?? "").Replace('\\', '/'), () => bytes, InArchive: true));
                }
                return files;
            }
        }
    }

    private static IEnumerable<SourceFile> ZipFiles(byte[] zipBytes, string prefix)
    {
        var files = new List<SourceFile>();
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            files.Add(new SourceFile(prefix + entry.FullName.Replace('\\', '/'), () => bytes, InArchive: true));
        }
        return files;
    }
}
