namespace QuakeAssetManager.Core.Imaging;

/// <summary>Decoded image, 8-bit RGBA, rows top to bottom, not premultiplied.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels, string Format);

/// <summary>Quake's 256-colour palette (gfx/palette.lmp, 768 bytes).</summary>
public sealed class QuakePalette
{
    private readonly byte[] _rgb;

    public QuakePalette(byte[] rgb)
    {
        if (rgb.Length < 768)
            throw new InvalidDataException("A palette needs 768 bytes.");
        _rgb = rgb[..768];
    }

    public RgbaImage ToImage(int width, int height, ReadOnlySpan<byte> indexes, bool index255Transparent, string format)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height && i < indexes.Length; i++)
        {
            var c = indexes[i];
            pixels[i * 4] = _rgb[c * 3];
            pixels[i * 4 + 1] = _rgb[c * 3 + 1];
            pixels[i * 4 + 2] = _rgb[c * 3 + 2];
            pixels[i * 4 + 3] = (byte)(index255Transparent && c == 255 ? 0 : 255);
        }
        return new RgbaImage(width, height, pixels, format);
    }
}
