using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuakeAssetManager.Online;

/// <summary>
/// https://gfx.quakeworld.nu: plain HTML, no API. The lists to browse come from each asset type's "online" entries
/// in asset-types.json. Requests go one at a time and are cached for a day.
/// </summary>
public sealed class GfxQuakeWorldCatalog : IOnlineCatalog, IDisposable
{
    public const string DefaultSiteUrl = "https://gfx.quakeworld.nu";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _cacheDir;

    public string Name { get; }
    public string SiteUrl { get; }

    public GfxQuakeWorldCatalog(string cacheDir, string name = "gfx.quakeworld.nu", string siteUrl = DefaultSiteUrl, string userAgent = "QuakeAssetManager")
    {
        _cacheDir = cacheDir;
        Name = name;
        SiteUrl = siteUrl.TrimEnd('/');
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<CatalogPage> BrowseAsync(string listUrl, int page, CancellationToken ct)
    {
        var url = page <= 1 ? listUrl : listUrl.TrimEnd('/') + $"/page/{page}/";
        var html = Encoding.Latin1.GetString(await GetAsync(url, cache: true, ct));
        return GfxPageParser.ParseBrowse(html, SiteUrl);
    }

    public async Task<CatalogItemDetails> GetDetailsAsync(CatalogItem item, CancellationToken ct)
    {
        var html = Encoding.Latin1.GetString(await GetAsync(item.DetailsUrl, cache: true, ct));
        return GfxPageParser.ParseDetails(html, item, SiteUrl);
    }

    public Task<byte[]> GetImageAsync(string url, CancellationToken ct) => GetAsync(url, cache: true, ct);

    public async Task<DownloadedFile> DownloadAsync(CatalogItemDetails details, string folder, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var response = await _http.GetAsync(details.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var name = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName
                       ?? $"{details.Item.Slug}.zip";
            name = string.Concat(name.Trim('"').Split(Path.GetInvalidFileNameChars()));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            await using (var file = File.Create(path))
                await response.Content.CopyToAsync(file, ct);
            return new DownloadedFile(path, name);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OnlineException(OnlineProblem.Unreachable, $"The download failed: {e.Message}", e);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> GetAsync(string url, bool cache, CancellationToken ct)
    {
        var cacheFile = Path.Combine(_cacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
        if (cache && File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < TimeSpan.FromDays(1))
            return await File.ReadAllBytesAsync(cacheFile, ct);

        await _gate.WaitAsync(ct);
        try
        {
            var bytes = await _http.GetByteArrayAsync(url, ct);
            if (cache)
            {
                try
                {
                    Directory.CreateDirectory(_cacheDir);
                    await File.WriteAllBytesAsync(cacheFile, bytes, ct);
                }
                catch (IOException) { }
            }
            return bytes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Offline: an old cached copy is better than nothing.
            if (cache && File.Exists(cacheFile))
                return await File.ReadAllBytesAsync(cacheFile, ct);
            throw new OnlineException(OnlineProblem.Unreachable, e.Message, e);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}

/// <summary>Ids of items already downloaded, kept in assets/.qam/online.json.</summary>
public sealed class DownloadHistory(string assetsRoot)
{
    private readonly string _path = Path.Combine(assetsRoot, ".qam", "online.json");
    private HashSet<string>? _ids;

    private HashSet<string> Ids
    {
        get
        {
            if (_ids is not null) return _ids;
            try
            {
                _ids = File.Exists(_path) ? JsonSerializer.Deserialize(File.ReadAllText(_path), OnlineJson.Default.HashSetString) ?? [] : [];
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                _ids = [];
            }
            return _ids;
        }
    }

    public bool Contains(string id) => Ids.Contains(id);

    public void Add(string id)
    {
        if (!Ids.Add(id)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Ids, OnlineJson.Default.HashSetString));
        }
        catch (IOException) { }
    }
}

/// <summary>JSON code generated at build time (safe to trim).</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(HashSet<string>))]
internal sealed partial class OnlineJson : System.Text.Json.Serialization.JsonSerializerContext;
