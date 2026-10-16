using System.Text;

namespace QuakeAssetManager.Core.Formats;

/// <summary>Skin layout of a Quake .mdl file: per skin, the number of frames (1 for a single skin).</summary>
public sealed record MdlInfo(int SkinWidth, int SkinHeight, IReadOnlyList<int> SkinFrames)
{
    private const int HeaderSize = 84;

    public static MdlInfo? TryRead(byte[] data)
    {
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != "IDPO")
            return null;
        var numSkins = BitConverter.ToInt32(data, 48);
        var width = BitConverter.ToInt32(data, 52);
        var height = BitConverter.ToInt32(data, 56);
        if (numSkins is < 0 or > 256 || width <= 0 || height <= 0 || width * (long)height > 16 * 1024 * 1024)
            return null;
        var frames = new List<int>(numSkins);
        long p = HeaderSize;
        var skinSize = width * height;
        for (var i = 0; i < numSkins; i++)
        {
            if (p + 4 > data.Length) return null;
            var group = BitConverter.ToInt32(data, (int)p);
            p += 4;
            if (group == 0)
            {
                frames.Add(1);
                p += skinSize;
            }
            else
            {
                if (p + 4 > data.Length) return null;
                var n = BitConverter.ToInt32(data, (int)p);
                if (n is <= 0 or > 256) return null;
                p += 4 + 4L * n + (long)n * skinSize;
                frames.Add(n);
            }
        }
        return p <= data.Length ? new MdlInfo(width, height, frames) : null;
    }

    /// <summary>Palette indexes of the first skin (first frame if it's a group).</summary>
    public static (int Width, int Height, byte[] Pixels)? ReadFirstSkin(byte[] data)
    {
        var info = TryRead(data);
        if (info is null || info.SkinFrames.Count == 0)
            return null;
        var size = info.SkinWidth * info.SkinHeight;
        var group = BitConverter.ToInt32(data, HeaderSize);
        var p = HeaderSize + 4;
        if (group != 0)
        {
            var n = BitConverter.ToInt32(data, p);
            p += 4 + 4 * n;
        }
        return (info.SkinWidth, info.SkinHeight, data.AsSpan(p, size).ToArray());
    }
}

/// <summary>Texture (miptex) names inside a Quake .bsp (BSP29, BSP2 and 2PSB share the lump table).</summary>
public static class BspInfo
{
    public static IReadOnlyList<string> ReadTextureNames(byte[] data)
    {
        if (data.Length < 4 + 15 * 8)
            return [];
        var version = BitConverter.ToInt32(data, 0);
        var magic = Encoding.ASCII.GetString(data, 0, 4);
        if (version is not (29 or 30) && magic is not ("BSP2" or "2PSB"))
            return [];
        var texOffset = BitConverter.ToInt32(data, 4 + 2 * 8);
        var texLength = BitConverter.ToInt32(data, 4 + 2 * 8 + 4);
        if (texOffset <= 0 || texLength < 4 || texOffset + (long)texLength > data.Length)
            return [];
        var count = BitConverter.ToInt32(data, texOffset);
        if (count < 0 || 4 + 4L * count > texLength)
            return [];
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var rel = BitConverter.ToInt32(data, texOffset + 4 + i * 4);
            if (rel < 0) continue;
            var p = texOffset + rel;
            if (p + 16 > data.Length) continue;
            var len = Array.IndexOf(data, (byte)0, p, 16) - p;
            if (len < 0) len = 16;
            var name = Encoding.ASCII.GetString(data, p, len);
            if (name.Length > 0)
                names.Add(name);
        }
        return names;
    }
}
