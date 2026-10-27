using QuakeAssetManager.Core.Formats;

namespace QuakeAssetManager.Core.Imaging;

/// <summary>
/// Decoders for the formats the UI toolkit can't read itself: TGA, PCX and Quake palette images
/// (.mdl skins, .lmp pictures, WAD lumps). PNG and JPG are left to the UI.
/// </summary>
public static class ImageDecoders
{
    public static bool IsNativeFormat(string extension) => extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp";

    public static bool CanPreview(string extension) =>
        IsNativeFormat(extension) || extension.ToLowerInvariant() is ".tga" or ".pcx" or ".lmp" or ".mdl" or ".md3" or ".wadlump";

    /// <summary>Decode by extension. Returns null when the format isn't handled here or the data is broken.</summary>
    public static RgbaImage? Decode(string extension, byte[] data, QuakePalette? palette, string? lumpName = null, byte lumpType = 0)
    {
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".tga" => DecodeTga(data),
                ".pcx" => DecodePcx(data),
                ".lmp" => palette is null ? null : DecodeQPic(data, palette, "LMP"),
                ".mdl" => palette is null ? null : DecodeMdlSkin(data, palette),
                ".wadlump" => palette is null ? null : DecodeWadLump(data, palette, lumpName ?? "", lumpType),
                _ => null,
            };
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    public static RgbaImage? DecodeQPic(byte[] data, QuakePalette palette, string format)
    {
        if (data.Length < 8) return null;
        var w = BitConverter.ToInt32(data, 0);
        var h = BitConverter.ToInt32(data, 4);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || 8 + (long)w * h > data.Length)
            return null;
        return palette.ToImage(w, h, data.AsSpan(8, w * h), true, format);
    }

    public static RgbaImage? DecodeMdlSkin(byte[] data, QuakePalette palette)
    {
        var skin = MdlInfo.ReadFirstSkin(data);
        return skin is null ? null : palette.ToImage(skin.Value.Width, skin.Value.Height, skin.Value.Pixels, false, "MDL skin");
    }

    public static RgbaImage? DecodeWadLump(byte[] data, QuakePalette palette, string name, byte type)
    {
        switch (type)
        {
            case WadFile.TypeQPic:
                return DecodeQPic(data, palette, "WAD picture");
            case WadFile.TypeMipTex when data.Length >= 40:
            {
                var w = BitConverter.ToInt32(data, 16);
                var h = BitConverter.ToInt32(data, 20);
                var offset = BitConverter.ToInt32(data, 24);
                if (w > 0 && h > 0 && offset > 0 && offset + (long)w * h <= data.Length)
                    return palette.ToImage(w, h, data.AsSpan(offset, w * h), false, "WAD texture");
                break;
            }
        }
        // conchars is a raw 128x128 lump.
        if (data.Length == 128 * 128)
            return palette.ToImage(128, 128, data, name == "conchars", "WAD raw");
        return null;
    }

    public static RgbaImage? DecodePcx(byte[] data)
    {
        if (data.Length < 128 || data[0] != 0x0A || data[2] != 1)
            return null;
        var bpp = data[3];
        var xmin = BitConverter.ToUInt16(data, 4);
        var ymin = BitConverter.ToUInt16(data, 6);
        var xmax = BitConverter.ToUInt16(data, 8);
        var ymax = BitConverter.ToUInt16(data, 10);
        var planes = data[65];
        var bytesPerLine = BitConverter.ToUInt16(data, 66);
        var w = xmax - xmin + 1;
        var h = ymax - ymin + 1;
        if (bpp != 8 || w <= 0 || h <= 0 || (planes != 1 && planes != 3 && planes != 4))
            return null;

        var scan = new byte[bytesPerLine * planes * h];
        int p = 128, o = 0;
        while (o < scan.Length && p < data.Length)
        {
            var b = data[p++];
            if ((b & 0xC0) == 0xC0)
            {
                var run = b & 0x3F;
                if (p >= data.Length) break;
                var v = data[p++];
                for (var i = 0; i < run && o < scan.Length; i++) scan[o++] = v;
            }
            else
            {
                scan[o++] = b;
            }
        }

        var pixels = new byte[w * h * 4];
        if (planes == 1)
        {
            var palOffset = data.Length - 768;
            if (palOffset < 128 || data[palOffset - 1] != 0x0C)
                return null;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = scan[y * bytesPerLine + x];
                var d = (y * w + x) * 4;
                pixels[d] = data[palOffset + c * 3];
                pixels[d + 1] = data[palOffset + c * 3 + 1];
                pixels[d + 2] = data[palOffset + c * 3 + 2];
                pixels[d + 3] = 255;
            }
        }
        else
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var row = y * bytesPerLine * planes;
                var d = (y * w + x) * 4;
                pixels[d] = scan[row + x];
                pixels[d + 1] = scan[row + bytesPerLine + x];
                pixels[d + 2] = scan[row + 2 * bytesPerLine + x];
                pixels[d + 3] = planes == 4 ? scan[row + 3 * bytesPerLine + x] : (byte)255;
            }
        }
        return new RgbaImage(w, h, pixels, "PCX");
    }

    public static RgbaImage? DecodeTga(byte[] data)
    {
        if (data.Length < 18) return null;
        int idLength = data[0], colorMapType = data[1], imageType = data[2];
        int cmFirst = BitConverter.ToUInt16(data, 3), cmLength = BitConverter.ToUInt16(data, 5), cmBits = data[7];
        int w = BitConverter.ToUInt16(data, 12), h = BitConverter.ToUInt16(data, 14);
        int bits = data[16], descriptor = data[17];
        if (w == 0 || h == 0) return null;

        var rle = imageType is 9 or 10 or 11;
        var baseType = rle ? imageType - 8 : imageType;
        if (baseType is not (1 or 2 or 3)) return null;

        var p = 18 + idLength;
        byte[]? colorMap = null;
        var cmBytes = (cmBits + 7) / 8;
        if (colorMapType == 1)
        {
            colorMap = data.AsSpan(p, cmLength * cmBytes).ToArray();
            p += cmLength * cmBytes;
        }

        var bytesPerPixel = (bits + 7) / 8;
        if (bytesPerPixel is < 1 or > 4) return null;
        var count = w * h;
        var raw = new byte[count * bytesPerPixel];
        if (rle)
        {
            var o = 0;
            while (o < raw.Length)
            {
                var header = data[p++];
                var n = (header & 0x7F) + 1;
                if ((header & 0x80) != 0)
                {
                    for (var i = 0; i < n && o < raw.Length; i++)
                    {
                        Array.Copy(data, p, raw, o, bytesPerPixel);
                        o += bytesPerPixel;
                    }
                    p += bytesPerPixel;
                }
                else
                {
                    var len = Math.Min(n * bytesPerPixel, raw.Length - o);
                    Array.Copy(data, p, raw, o, len);
                    p += n * bytesPerPixel;
                    o += len;
                }
            }
        }
        else
        {
            Array.Copy(data, p, raw, 0, raw.Length);
        }

        var pixels = new byte[count * 4];
        var hasAlphaBits = (descriptor & 0x0F) > 0;
        for (var i = 0; i < count; i++)
        {
            var s = i * bytesPerPixel;
            byte r, g, b, a = 255;
            if (baseType == 3)
            {
                r = g = b = raw[s];
                if (bytesPerPixel == 2) a = raw[s + 1];
            }
            else
            {
                ReadOnlySpan<byte> src;
                int pb;
                if (baseType == 1)
                {
                    int idx = bytesPerPixel == 2 ? BitConverter.ToUInt16(raw, s) : raw[s];
                    idx -= cmFirst;
                    if (colorMap is null || idx < 0 || idx >= cmLength) { r = g = b = 0; goto store; }
                    src = colorMap.AsSpan(idx * cmBytes, cmBytes);
                    pb = cmBytes;
                }
                else
                {
                    src = raw.AsSpan(s, bytesPerPixel);
                    pb = bytesPerPixel;
                }
                if (pb == 2)
                {
                    var v = src[0] | (src[1] << 8);
                    r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                    g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                    b = (byte)((v & 0x1F) * 255 / 31);
                }
                else
                {
                    b = src[0];
                    g = src[1];
                    r = src[2];
                    if (pb == 4 && hasAlphaBits) a = src[3];
                }
            }
        store:
            var d = i * 4;
            pixels[d] = r;
            pixels[d + 1] = g;
            pixels[d + 2] = b;
            pixels[d + 3] = a;
        }

        // Origin: bit 5 set means top-left; otherwise bottom-left (flip rows). Bit 4 means right-to-left.
        if ((descriptor & 0x20) == 0)
            FlipVertical(pixels, w, h);
        if ((descriptor & 0x10) != 0)
            FlipHorizontal(pixels, w, h);
        return new RgbaImage(w, h, pixels, "TGA");
    }

    private static void FlipVertical(byte[] pixels, int w, int h)
    {
        var stride = w * 4;
        var tmp = new byte[stride];
        for (var y = 0; y < h / 2; y++)
        {
            var a = y * stride;
            var b = (h - 1 - y) * stride;
            Array.Copy(pixels, a, tmp, 0, stride);
            Array.Copy(pixels, b, pixels, a, stride);
            Array.Copy(tmp, 0, pixels, b, stride);
        }
    }

    private static void FlipHorizontal(byte[] pixels, int w, int h)
    {
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w / 2; x++)
        {
            var a = (y * w + x) * 4;
            var b = (y * w + (w - 1 - x)) * 4;
            for (var k = 0; k < 4; k++)
                (pixels[a + k], pixels[b + k]) = (pixels[b + k], pixels[a + k]);
        }
    }
}
