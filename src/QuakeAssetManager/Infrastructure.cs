using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Imaging;

namespace QuakeAssetManager;

/// <summary>Help mode shows every Border with the "help" class.</summary>
public static class HelpMode
{
    public static bool IsOn { get; private set; }

    public static event Action? Changed;

    public static void Set(bool on)
    {
        IsOn = on;
        Changed?.Invoke();
    }

    public static void Apply(ILogical root)
    {
        foreach (var border in root.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("help")))
            border.IsVisible = IsOn;
    }
}

public sealed record Preview(Bitmap? Image, string Info, string? Text = null);

public static class Previews
{
    public static Preview FromFile(string path, QuakePalette? palette)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new Preview(null, Loc.T("File not found"));
            if (info.Length > 64 * 1024 * 1024)
                return new Preview(null, $"{Path.GetFileName(path)} · {Size(info.Length)} · " + Loc.T("too big to preview"));
            return FromBytes(Path.GetFileName(path), File.ReadAllBytes(path), palette);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Preview(null, e.Message);
        }
    }

    public static Preview FromBytes(string name, byte[] data, QuakePalette? palette, string? extension = null, byte lumpType = 0)
    {
        var ext = (extension ?? Path.GetExtension(name)).ToLowerInvariant();
        var size = Size(data.Length);
        if (ext == ".link")
        {
            var target = System.Text.Encoding.ASCII.GetString(data).Split('\n')[0].Trim();
            return new Preview(null, $"{name} · link", Loc.F("Points to {0}", "textures/" + target));
        }
        try
        {
            if (ImageDecoders.IsNativeFormat(ext))
            {
                using var ms = new MemoryStream(data);
                var bmp = new Bitmap(ms);
                return new Preview(bmp, $"{name} · {bmp.PixelSize.Width}×{bmp.PixelSize.Height} · {ext.TrimStart('.').ToUpperInvariant()} · {size}");
            }
            var lumpName = ext == ".wadlump" ? name : null;
            var img = ImageDecoders.Decode(ext, data, palette, lumpName, lumpType);
            if (img is not null)
                return new Preview(ToBitmap(img), $"{name} · {img.Width}×{img.Height} · {img.Format} · {size}");
            if ((ext is ".lmp" or ".mdl" or ".wadlump") && palette is null)
                return new Preview(null, $"{name} · {size}", Loc.T("No preview: the Quake palette (gfx/palette.lmp) wasn't found."));
        }
        catch (Exception)
        {
            return new Preview(null, $"{name} · {size}", Loc.T("This image couldn't be read."));
        }
        return new Preview(null, $"{name} · {size}", Loc.T("No preview for this file type."));
    }

    public static Bitmap ToBitmap(RgbaImage img)
    {
        var bmp = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        var srcStride = img.Width * 4;
        for (var y = 0; y < img.Height; y++)
            Marshal.Copy(img.Pixels, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        return bmp;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}

public static class Shell
{
    /// <summary>Open a folder in the system file manager; shows a message when it can't.</summary>
    public static async Task OpenFolder(Window owner, string path)
    {
        try
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);
            var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            using var p = Process.Start(new ProcessStartInfo(opener, [path]) { UseShellExecute = false });
            if (p is null) throw new InvalidOperationException(opener);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await Dialogs.Message(owner, Loc.T("Open folder"), Loc.F("This folder couldn't be opened: {0}", path) + "\n" + e.Message);
        }
    }

    /// <summary>Start the client from the Quake folder with +map &lt;map&gt;.</summary>
    public static void RunClient(AppSettings settings)
    {
        var exe = Path.Combine(settings.QuakeFolder, settings.Client);
        var start = new ProcessStartInfo(exe) { WorkingDirectory = settings.QuakeFolder, UseShellExecute = false };
        start.ArgumentList.Add("+map");
        start.ArgumentList.Add(settings.Map.Trim());
        Process.Start(start);
    }
}

/// <summary>Watches folders and calls back on the UI thread once changes settle.</summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _changedRoots = [];
    private readonly object _lock = new();

    public FolderWatcher(Action<IReadOnlyCollection<string>> onChanged)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            string[] roots;
            lock (_lock)
            {
                roots = [.. _changedRoots];
                _changedRoots.Clear();
            }
            onChanged(roots);
        };
    }

    public void Watch(IEnumerable<string> folders)
    {
        Clear();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                FileSystemEventHandler handler = (_, _) => Touch(folder);
                w.Changed += handler;
                w.Created += handler;
                w.Deleted += handler;
                w.Renamed += (_, _) => Touch(folder);
                w.Error += (_, _) => Touch(folder);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or PlatformNotSupportedException)
            {
                // Watching is a convenience; the lists still refresh after the app's own operations.
            }
        }
    }

    private void Touch(string root)
    {
        lock (_lock)
            _changedRoots.Add(root);
        Dispatcher.UIThread.Post(() =>
        {
            _timer.Stop();
            _timer.Start();
        });
    }

    private void Clear()
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
    }

    public void Dispose()
    {
        Clear();
        _timer.Stop();
    }
}

/// <summary>
/// 32×32 thumbnails, kept in memory only (no files on disk): textures are small and decode in about a millisecond.
/// Capped at about 2,000 entries; the oldest are dropped first.
/// </summary>
public static class Thumbnails
{
    private const int Size = 32;
    private const int Capacity = 2000;
    private static readonly Dictionary<string, Bitmap?> Cache = [];
    private static readonly LinkedList<string> Order = new();
    private static readonly object Lock = new();
    private static readonly SemaphoreSlim Workers = new(2, 2);

    /// <summary>Load thumbnails for the given rows in the background. Cancel when the list is rebuilt.</summary>
    public static void Load(IEnumerable<ThumbItem> items, QuakePalette? palette, CancellationToken ct)
    {
        var list = items.Where(i => i.ThumbnailPath is not null || i.ThumbnailSource is not null).ToList();
        _ = Task.Run(async () =>
        {
            foreach (var item in list)
            {
                if (ct.IsCancellationRequested) return;
                await Workers.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var bmp = Get(item, palette);
                    if (bmp is not null)
                        Dispatcher.UIThread.Post(() => item.Thumbnail = bmp);
                }
                catch (Exception)
                {
                    // A broken image just keeps its icon.
                }
                finally
                {
                    Workers.Release();
                }
            }
        }, ct);
    }

    private static Bitmap? Get(ThumbItem item, QuakePalette? palette)
    {
        string key, name;
        string? ext;
        byte lumpType = 0;
        Func<byte[]> read;
        if (item.ThumbnailPath is { } path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 32 * 1024 * 1024 || !ImageDecoders.CanPreview(info.Extension) ||
                info.Extension.Equals(".mdl", StringComparison.OrdinalIgnoreCase))
                return null;
            key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            name = info.Name;
            ext = info.Extension;
            read = () => File.ReadAllBytes(path);
        }
        else
        {
            var source = item.ThumbnailSource!();
            key = $"bytes|{source.Name}|{source.Data.Length}|{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(source.Data))}";
            name = source.Name;
            ext = source.Extension ?? Path.GetExtension(source.Name);
            lumpType = source.LumpType;
            read = () => source.Data;
        }
        if (TryCached(key, out var cached)) return cached;
        return Store(key, Make(read(), name, ext ?? "", lumpType, palette));
    }

    private static Bitmap? Make(byte[] data, string name, string ext, byte lumpType, QuakePalette? palette)
    {
        if (ImageDecoders.IsNativeFormat(ext))
        {
            using var ms = new MemoryStream(data);
            var probe = new Bitmap(ms);
            var (w, h) = (probe.PixelSize.Width, probe.PixelSize.Height);
            if (w <= Size && h <= Size) return probe;
            probe.Dispose();
            ms.Position = 0;
            return w >= h ? Bitmap.DecodeToWidth(ms, Size) : Bitmap.DecodeToHeight(ms, Size);
        }
        var img = ImageDecoders.Decode(ext, data, palette, ext == ".wadlump" ? name : null, lumpType);
        return img is null ? null : Previews.ToBitmap(Shrink(img));
    }

    /// <summary>Box-filter an image down so its longer side is at most 32 pixels.</summary>
    private static RgbaImage Shrink(RgbaImage img)
    {
        var scale = Math.Max(img.Width, img.Height) / (double)Size;
        if (scale <= 1) return img;
        int w = Math.Max(1, (int)(img.Width / scale)), h = Math.Max(1, (int)(img.Height / scale));
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            int x0 = (int)(x * scale), x1 = Math.Max(x0 + 1, (int)((x + 1) * scale));
            int y0 = (int)(y * scale), y1 = Math.Max(y0 + 1, (int)((y + 1) * scale));
            long r = 0, g = 0, b = 0, a = 0, n = 0;
            for (var sy = y0; sy < y1 && sy < img.Height; sy++)
            for (var sx = x0; sx < x1 && sx < img.Width; sx++)
            {
                var i = (sy * img.Width + sx) * 4;
                var alpha = img.Pixels[i + 3];
                r += img.Pixels[i] * alpha; g += img.Pixels[i + 1] * alpha; b += img.Pixels[i + 2] * alpha; a += alpha; n++;
            }
            var d = (y * w + x) * 4;
            if (a > 0)
            {
                dst[d] = (byte)(r / a); dst[d + 1] = (byte)(g / a); dst[d + 2] = (byte)(b / a);
            }
            dst[d + 3] = (byte)(n == 0 ? 0 : a / n);
        }
        return new RgbaImage(w, h, dst, img.Format);
    }

    private static bool TryCached(string key, out Bitmap? bmp)
    {
        lock (Lock)
            return Cache.TryGetValue(key, out bmp);
    }

    private static Bitmap? Store(string key, Bitmap? bmp)
    {
        lock (Lock)
        {
            if (Cache.TryAdd(key, bmp))
            {
                Order.AddLast(key);
                while (Order.Count > Capacity)
                {
                    Cache.Remove(Order.First!.Value);
                    Order.RemoveFirst();
                }
            }
            return bmp;
        }
    }
}
