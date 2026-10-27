using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace QuakeAssetManager;

/// <summary>
/// Check GitHub for a newer release, and install it: download the package for this system, unpack it, swap in the
/// new executable and files (config/ and cache/ are never touched), then restart. A running executable can't be
/// overwritten but can be renamed, so the old one becomes QuakeAssetManager(.exe).old and is deleted next start.
/// </summary>
public static class Updates
{
    public const string Repository = "mushis/Asset-manager";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    public sealed record Release(Version Version, string Tag, string PageUrl, string Notes, string? AssetName, string? DownloadUrl);

    public static Version Current
    {
        get
        {
            var v = typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static string PackageSuffix => OperatingSystem.IsWindows() ? "-win-x64.zip" : "-linux-x64.tar.gz";

    private static string AppDir => AppContext.BaseDirectory;
    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppDir, OperatingSystem.IsWindows() ? "QuakeAssetManager.exe" : "QuakeAssetManager");

    /// <summary>Only the single-file release can replace itself; a build from source has many files next to it.</summary>
    public static bool CanInstall => !File.Exists(Path.Combine(AppDir, "QuakeAssetManager.dll"));

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"QuakeAssetManager/{Current}");
        return http;
    }

    /// <summary>The latest release on GitHub when it's newer than this version; null when this one is up to date.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        using var http = NewClient();
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.GetAsync(LatestApi, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            return null;
        string? assetName = null, url = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(PackageSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    url = a.GetProperty("browser_download_url").GetString();
                }
            }
        var release = new Release(new Version(version.Major, version.Minor, Math.Max(0, version.Build)), tag,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? ReleasesPage : ReleasesPage,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            assetName, url);
        return release.Version > Current ? release : null;
    }

    /// <summary>Download, unpack and swap in the new version. Call <see cref="Restart"/> afterwards.</summary>
    public static async Task InstallAsync(Release release, IProgress<int>? progress, CancellationToken ct = default)
    {
        if (release.DownloadUrl is null || release.AssetName is null)
            throw new InvalidOperationException(Loc.T("This release has no package for your system."));
        if (!release.DownloadUrl.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected download address: " + release.DownloadUrl);

        var work = Path.Combine(Path.GetTempPath(), "QuakeAssetManager", "update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var package = Path.Combine(work, release.AssetName);
            using (var http = NewClient())
            using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var from = await response.Content.ReadAsStreamAsync(ct);
                await using var to = File.Create(package);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await from.ReadAsync(buffer, ct)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
            }

            var unpacked = Path.Combine(work, "unpacked");
            Directory.CreateDirectory(unpacked);
            if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(package, unpacked);
            else
            {
                await using var file = File.OpenRead(package);
                await using var gz = new GZipStream(file, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, unpacked, overwriteFiles: true, ct);
            }

            var exeName = Path.GetFileName(ExePath);
            var newExe = Directory.EnumerateFiles(unpacked, exeName, SearchOption.AllDirectories).FirstOrDefault()
                         ?? throw new InvalidDataException(Loc.T("The downloaded package doesn't contain the app."));
            var source = Path.GetDirectoryName(newExe)!;

            // Swap: the running executable is renamed (allowed while it runs), the new one takes its place.
            var old = ExePath + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(ExePath, old);
            try
            {
                File.Copy(newExe, ExePath);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(ExePath, File.GetUnixFileMode(old));
            }
            catch
            {
                if (!File.Exists(ExePath)) File.Move(old, ExePath);   // put the working version back
                throw;
            }
            // Everything else in the package (lang/, Assets/, README, .desktop), never config/ or cache/.
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var top = relative.Split(Path.DirectorySeparatorChar)[0];
                if (relative == exeName || top is "config" or "cache") continue;
                var target = Path.Combine(AppDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Start the (new) executable and close this one.</summary>
    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = AppDir })?.Dispose();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Delete the previous version left by an update.</summary>
    public static void CleanUp()
    {
        try
        {
            var old = ExePath + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
