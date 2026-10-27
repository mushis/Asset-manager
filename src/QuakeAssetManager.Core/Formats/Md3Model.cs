using System.Text;
using QuakeAssetManager.Core.Imaging;

namespace QuakeAssetManager.Core.Formats;

/// <summary>
/// A Quake 3 / ezQuake .md3 model, first frame only, and a small software renderer for the preview.
/// Format: "IDP3", version 15; surfaces of triangles, texture coordinates and vertices (1/64 units).
/// </summary>
public sealed class Md3Model
{
    public sealed record Surface(string Name, string Shader, float[] Xyz, int[] Triangles, float[] St);

    public required string Name { get; init; }
    public required IReadOnlyList<Surface> Surfaces { get; init; }
    public int FrameCount { get; init; }

    public int VertexCount => Surfaces.Sum(s => s.Xyz.Length / 3);
    public int TriangleCount => Surfaces.Sum(s => s.Triangles.Length / 3);

    public static Md3Model? TryRead(byte[] d)
    {
        try
        {
            if (d.Length < 108 || Encoding.ASCII.GetString(d, 0, 4) != "IDP3" || BitConverter.ToInt32(d, 4) != 15)
                return null;
            var name = CString(d, 8, 64);
            var frames = BitConverter.ToInt32(d, 76);
            var numSurfaces = BitConverter.ToInt32(d, 84);
            var ofsSurfaces = BitConverter.ToInt32(d, 100);
            var surfaces = new List<Surface>();
            var at = ofsSurfaces;
            for (var s = 0; s < numSurfaces && s < 64; s++)
            {
                if (at + 108 > d.Length || Encoding.ASCII.GetString(d, at, 4) != "IDP3") break;
                var sname = CString(d, at + 4, 64);
                var numShaders = BitConverter.ToInt32(d, at + 76);
                var numVerts = BitConverter.ToInt32(d, at + 80);
                var numTris = BitConverter.ToInt32(d, at + 84);
                var ofsTris = BitConverter.ToInt32(d, at + 88);
                var ofsShaders = BitConverter.ToInt32(d, at + 92);
                var ofsSt = BitConverter.ToInt32(d, at + 96);
                var ofsXyz = BitConverter.ToInt32(d, at + 100);
                var ofsEnd = BitConverter.ToInt32(d, at + 104);
                if (numVerts <= 0 || numVerts > 65536 || numTris <= 0 || numTris > 131072) { at += ofsEnd; continue; }
                var shader = numShaders > 0 ? CString(d, at + ofsShaders, 64) : "";
                var tris = new int[numTris * 3];
                for (var i = 0; i < tris.Length; i++)
                {
                    tris[i] = BitConverter.ToInt32(d, at + ofsTris + i * 4);
                    if (tris[i] < 0 || tris[i] >= numVerts) tris[i] = 0;
                }
                var st = new float[numVerts * 2];
                for (var i = 0; i < st.Length; i++)
                    st[i] = BitConverter.ToSingle(d, at + ofsSt + i * 4);
                var xyz = new float[numVerts * 3];
                for (var v = 0; v < numVerts; v++)
                    for (var k = 0; k < 3; k++)
                        xyz[v * 3 + k] = BitConverter.ToInt16(d, at + ofsXyz + v * 8 + k * 2) / 64f;
                surfaces.Add(new Surface(sname, shader, xyz, tris, st));
                if (ofsEnd <= 0) break;
                at += ofsEnd;
            }
            return surfaces.Count == 0 ? null : new Md3Model { Name = name, Surfaces = surfaces, FrameCount = frames };
        }
        catch (ArgumentException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static string CString(byte[] d, int at, int max)
    {
        var len = 0;
        while (len < max && at + len < d.Length && d[at + len] != 0) len++;
        return Encoding.ASCII.GetString(d, at, len);
    }

    /// <summary>
    /// Draw the model, turned by <paramref name="yaw"/> and <paramref name="pitch"/> (degrees), with each surface's
    /// texture when <paramref name="texture"/> finds one (by surface), otherwise plain shading.
    /// </summary>
    public RgbaImage Render(int width, int height, float yaw, float pitch, Func<Surface, RgbaImage?>? texture = null)
    {
        var px = new byte[width * height * 4];
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);

        // Fit the model in the picture.
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var s in Surfaces)
            for (var i = 0; i < s.Xyz.Length; i += 3)
            {
                minX = Math.Min(minX, s.Xyz[i]); maxX = Math.Max(maxX, s.Xyz[i]);
                minY = Math.Min(minY, s.Xyz[i + 1]); maxY = Math.Max(maxY, s.Xyz[i + 1]);
                minZ = Math.Min(minZ, s.Xyz[i + 2]); maxZ = Math.Max(maxZ, s.Xyz[i + 2]);
            }
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
        var radius = MathF.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ)) / 2;
        var scale = Math.Min(width, height) * 0.48f / Math.Max(radius, 0.001f);
        float ya = yaw * MathF.PI / 180, pa = pitch * MathF.PI / 180;
        float cosY = MathF.Cos(ya), sinY = MathF.Sin(ya), cosP = MathF.Cos(pa), sinP = MathF.Sin(pa);

        foreach (var s in Surfaces)
        {
            var tex = texture?.Invoke(s);
            var n = s.Xyz.Length / 3;
            var sx = new float[n]; var sy = new float[n]; var sz = new float[n];
            var rx = new float[n]; var ry = new float[n]; var rz = new float[n];
            for (var v = 0; v < n; v++)
            {
                // Quake: z is up. Turn around z (yaw), then tilt around the screen's x axis (pitch).
                float x = s.Xyz[v * 3] - cx, y = s.Xyz[v * 3 + 1] - cy, z = s.Xyz[v * 3 + 2] - cz;
                float x1 = x * cosY - y * sinY, y1 = x * sinY + y * cosY;
                float y2 = y1 * cosP - z * sinP, z2 = y1 * sinP + z * cosP;
                rx[v] = x1; ry[v] = y2; rz[v] = z2;
                sx[v] = width / 2f + x1 * scale;
                sy[v] = height / 2f - z2 * scale;
                sz[v] = y2;   // distance from the viewer, who looks along +y
            }
            for (var t = 0; t < s.Triangles.Length; t += 3)
            {
                int a = s.Triangles[t], b = s.Triangles[t + 1], c = s.Triangles[t + 2];
                // Light from the viewer: brightness from the face normal.
                float ux = rx[b] - rx[a], uy = ry[b] - ry[a], uz = rz[b] - rz[a];
                float vx = rx[c] - rx[a], vy = ry[c] - ry[a], vz = rz[c] - rz[a];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                var light = len > 0 ? 0.35f + 0.65f * MathF.Abs((-ny * 0.8f + nz * 0.6f) / len) : 0.7f;
                Fill(a, b, c, light);
            }

            void Fill(int a, int b, int c, float light)
            {
                var x0 = (int)MathF.Max(0, MathF.Floor(MathF.Min(sx[a], MathF.Min(sx[b], sx[c]))));
                var x1 = (int)MathF.Min(width - 1, MathF.Ceiling(MathF.Max(sx[a], MathF.Max(sx[b], sx[c]))));
                var y0 = (int)MathF.Max(0, MathF.Floor(MathF.Min(sy[a], MathF.Min(sy[b], sy[c]))));
                var y1 = (int)MathF.Min(height - 1, MathF.Ceiling(MathF.Max(sy[a], MathF.Max(sy[b], sy[c]))));
                var area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sx[c] - sx[a]) * (sy[b] - sy[a]);
                if (MathF.Abs(area) < 1e-6f) return;
                for (var py = y0; py <= y1; py++)
                for (var pxl = x0; pxl <= x1; pxl++)
                {
                    float qx = pxl + 0.5f, qy = py + 0.5f;
                    var w0 = ((sx[b] - qx) * (sy[c] - qy) - (sx[c] - qx) * (sy[b] - qy)) / area;
                    var w1 = ((sx[c] - qx) * (sy[a] - qy) - (sx[a] - qx) * (sy[c] - qy)) / area;
                    var w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    var zv = w0 * sz[a] + w1 * sz[b] + w2 * sz[c];
                    var i = py * width + pxl;
                    if (zv >= depth[i]) continue;
                    byte r = 196, g = 160, bl = 110;
                    if (tex is not null)
                    {
                        var u = w0 * s.St[a * 2] + w1 * s.St[b * 2] + w2 * s.St[c * 2];
                        var vv = w0 * s.St[a * 2 + 1] + w1 * s.St[b * 2 + 1] + w2 * s.St[c * 2 + 1];
                        var tx = (int)((u - MathF.Floor(u)) * tex.Width) % tex.Width;
                        var ty = (int)((vv - MathF.Floor(vv)) * tex.Height) % tex.Height;
                        var ti = (ty * tex.Width + tx) * 4;
                        if (tex.Pixels[ti + 3] < 128) continue;   // see-through texels
                        r = tex.Pixels[ti]; g = tex.Pixels[ti + 1]; bl = tex.Pixels[ti + 2];
                    }
                    depth[i] = zv;
                    px[i * 4] = (byte)Math.Min(255, r * light);
                    px[i * 4 + 1] = (byte)Math.Min(255, g * light);
                    px[i * 4 + 2] = (byte)Math.Min(255, bl * light);
                    px[i * 4 + 3] = 255;
                }
            }
        }
        return new RgbaImage(width, height, px, "MD3");
    }
}
