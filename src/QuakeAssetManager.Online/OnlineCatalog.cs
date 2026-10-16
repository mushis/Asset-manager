namespace QuakeAssetManager.Online;

public sealed record CatalogItem(string Id, string Slug, string Name, string Category, string ThumbnailUrl, string DetailsUrl);

public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int Page, int PageCount);

public sealed record CatalogItemDetails(
    CatalogItem Item,
    string? Submitted,
    int? Downloads,
    string Description,
    string ScreenshotUrl,
    string DownloadUrl,
    string? SizeText);

public sealed record DownloadedFile(string Path, string FileName);

public enum OnlineProblem
{
    /// <summary>No connection, timeout, DNS or HTTP error.</summary>
    Unreachable,
    /// <summary>The page loaded but didn't look like the parser expects: the site has probably changed.</summary>
    Unrecognised,
}

public sealed class OnlineException(OnlineProblem problem, string message, Exception? inner = null) : Exception(message, inner)
{
    public OnlineProblem Problem { get; } = problem;
}

/// <summary>
/// A website the app can browse and download assets from. The UI only talks to this interface,
/// so the site-specific code (URLs, HTML parsing) can change or be replaced without touching the app.
/// </summary>
public interface IOnlineCatalog
{
    string Name { get; }
    string SiteUrl { get; }

    /// <summary>One page of a list on the site. The list URLs come from each asset type's "online" setting.</summary>
    Task<CatalogPage> BrowseAsync(string listUrl, int page, CancellationToken ct);

    Task<CatalogItemDetails> GetDetailsAsync(CatalogItem item, CancellationToken ct);

    Task<byte[]> GetImageAsync(string url, CancellationToken ct);

    /// <summary>Download the item's file into <paramref name="folder"/>.</summary>
    Task<DownloadedFile> DownloadAsync(CatalogItemDetails details, string folder, CancellationToken ct);
}
