using System.Text;

namespace QuakeAssetManager.Core.Formats;

public sealed record WadLump(string Name, byte Type, int Offset, int Size);

/// <summary>Quake WAD2 file (e.g. gfx.wad). 32-byte directory entries.</summary>
public sealed class WadFile
{
    public const byte TypePalette = 0x40;
    public const byte TypeQPic = 0x42;
    public const byte TypeMipTex = 0x44;
    public const byte TypeConsolePic = 0x45;

    private readonly byte[] _data;
    public IReadOnlyList<WadLump> Lumps { get; }

    private WadFile(byte[] data, List<WadLump> lumps)
    {
        _data = data;
        Lumps = lumps;
    }

    public static bool TryParse(byte[] data, out WadFile? wad)
    {
        wad = null;
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "WAD2")
            return false;
        var count = BitConverter.ToInt32(data, 4);
        var dir = BitConverter.ToInt32(data, 8);
        if (count < 0 || dir < 12 || dir + (long)count * 32 > data.Length)
            return false;
        var lumps = new List<WadLump>(count);
        for (var i = 0; i < count; i++)
        {
            var p = dir + i * 32;
            var offset = BitConverter.ToInt32(data, p);
            var diskSize = BitConverter.ToInt32(data, p + 4);
            var type = data[p + 12];
            var compression = data[p + 13];
            var nameLen = Array.IndexOf(data, (byte)0, p + 16, 16) - (p + 16);
            if (nameLen < 0) nameLen = 16;
            var name = Encoding.ASCII.GetString(data, p + 16, nameLen).ToLowerInvariant();
            if (compression != 0 || offset < 0 || diskSize < 0 || offset + (long)diskSize > data.Length)
                continue;
            lumps.Add(new WadLump(name, type, offset, diskSize));
        }
        wad = new WadFile(data, lumps);
        return true;
    }

    public byte[] ReadLump(WadLump lump) => _data.AsSpan(lump.Offset, lump.Size).ToArray();
}
