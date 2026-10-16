using System.IO.Compression;
using System.Text;

namespace QuakeAssetManager.Core.Archives;

/// <param name="Modified">Only pk3 (zip) entries store a date.</param>
public sealed record ArchiveEntry(string Path, long Size, DateTime? Modified = null)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
}

/// <summary>Read-only view of a Quake .pak or .pk3 file. Entries use '/' separators.</summary>
public abstract class GameArchive : IDisposable
{
    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public abstract IReadOnlyList<ArchiveEntry> Entries { get; }

    protected GameArchive(string filePath) => FilePath = filePath;

    public abstract byte[] ReadEntry(ArchiveEntry entry);

    public abstract void Dispose();

    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pak" or ".pk3";
    }

    public static GameArchive Open(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pak" => new PakArchive(path),
            ".pk3" => new Pk3Archive(path),
            _ => throw new NotSupportedException($"Not an archive: {path}"),
        };
}

/// <summary>Quake 1 PACK format: "PACK", dir offset, dir length; 64-byte entries (56-byte name, offset, size).</summary>
public sealed class PakArchive : GameArchive
{
    private readonly FileStream _stream;
    private readonly List<ArchiveEntry> _entries = [];
    private readonly Dictionary<ArchiveEntry, (int Offset, int Size)> _locations = [];

    public PakArchive(string path) : base(path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            using var reader = new BinaryReader(_stream, Encoding.ASCII, leaveOpen: true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "PACK")
                throw new InvalidDataException("Not a PAK file.");
            var dirOffset = reader.ReadInt32();
            var dirLength = reader.ReadInt32();
            if (dirOffset < 12 || dirLength < 0 || dirOffset + (long)dirLength > _stream.Length)
                throw new InvalidDataException("Corrupt PAK directory.");
            _stream.Position = dirOffset;
            for (var i = 0; i < dirLength / 64; i++)
            {
                var nameBytes = reader.ReadBytes(56);
                var offset = reader.ReadInt32();
                var size = reader.ReadInt32();
                var len = Array.IndexOf(nameBytes, (byte)0);
                var name = Encoding.ASCII.GetString(nameBytes, 0, len < 0 ? 56 : len).Replace('\\', '/');
                if (name.Length == 0 || offset < 0 || size < 0 || offset + (long)size > _stream.Length)
                    continue;
                var entry = new ArchiveEntry(name, size);
                _entries.Add(entry);
                _locations[entry] = (offset, size);
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public override IReadOnlyList<ArchiveEntry> Entries => _entries;

    public override byte[] ReadEntry(ArchiveEntry entry)
    {
        var (offset, size) = _locations[entry];
        var buffer = new byte[size];
        lock (_stream)
        {
            _stream.Position = offset;
            _stream.ReadExactly(buffer);
        }
        return buffer;
    }

    public override void Dispose() => _stream.Dispose();
}

/// <summary>A .pk3 is a zip file.</summary>
public sealed class Pk3Archive : GameArchive
{
    private readonly ZipArchive _zip;
    private readonly List<ArchiveEntry> _entries = [];
    private readonly Dictionary<ArchiveEntry, ZipArchiveEntry> _zipEntries = [];

    public Pk3Archive(string path) : base(path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            _zip = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        foreach (var z in _zip.Entries)
        {
            if (z.FullName.EndsWith('/') || z.FullName.EndsWith('\\'))
                continue; // directory entry
            var entry = new ArchiveEntry(z.FullName.Replace('\\', '/'), z.Length, z.LastWriteTime.LocalDateTime);
            _entries.Add(entry);
            _zipEntries[entry] = z;
        }
    }

    public override IReadOnlyList<ArchiveEntry> Entries => _entries;

    public override byte[] ReadEntry(ArchiveEntry entry)
    {
        lock (_zip)
        {
            using var s = _zipEntries[entry].Open();
            using var ms = new MemoryStream((int)Math.Max(0, entry.Size));
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public override void Dispose() => _zip.Dispose();
}
