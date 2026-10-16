using System.Net;
using System.Text.RegularExpressions;

namespace QuakeAssetManager.Online;

/// <summary>
/// Reads gfx.quakeworld.nu pages. All knowledge of the site's HTML lives here; if the site changes,
/// this class and the saved pages in tests/TestData are what need updating.
/// </summary>
public static partial class GfxPageParser
{
    // <a href="/details/634/pak-dm/" title="pak DM (Models / Sets)"><img src="/files/634_thumb.jpg" ... /><em>pak DM</em></a>
    [GeneratedRegex(@"<a href=""(?<href>/details/(?<id>\d+)/(?<slug>[^/""]+)/)"" title=""(?<title>[^""]*)""[^>]*>\s*<img src=""(?<thumb>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemRegex();

    [GeneratedRegex("""<span id="currentpage">(\d+)</span>""", RegexOptions.IgnoreCase)]
    private static partial Regex CurrentPageRegex();

    [GeneratedRegex("""/page/(\d+)/""", RegexOptions.IgnoreCase)]
    private static partial Regex PageLinkRegex();

    [GeneratedRegex("""Submitted\s+(\d{4}-\d{2}-\d{2})""", RegexOptions.IgnoreCase)]
    private static partial Regex SubmittedRegex();

    [GeneratedRegex(""">\s*(\d+)\s+downloads?\s*<""", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadsRegex();

    [GeneratedRegex(@"<h3>\s*Description\s*</h3>(?<body>.*?)<p class=""download""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex("""<p class="download"[^>]*>\s*<a href="(?<href>/download/[^"]+)"[^>]*>(?<text>[^<]*)</a>""", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRegex();

    [GeneratedRegex("""\(([\d.,]+\s*[kmg]?b)\)""", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex("""<img src="(?<src>/files/\d+\.(?:jpe?g|png|gif))"[^>]*alt="Screenshot""", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenshotRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    public static CatalogPage ParseBrowse(string html, string siteUrl)
    {
        var items = new List<CatalogItem>();
        var seen = new HashSet<string>();
        foreach (Match m in ItemRegex().Matches(html))
        {
            var id = m.Groups["id"].Value;
            if (!seen.Add(id)) continue;
            var title = WebUtility.HtmlDecode(m.Groups["title"].Value);
            var (name, category) = SplitTitle(title);
            items.Add(new CatalogItem(id, m.Groups["slug"].Value, name, category,
                Absolute(siteUrl, m.Groups["thumb"].Value), Absolute(siteUrl, m.Groups["href"].Value)));
        }

        var page = CurrentPageRegex().Match(html) is { Success: true } cp ? int.Parse(cp.Groups[1].Value) : 1;
        var pageCount = PageLinkRegex().Matches(html).Select(x => int.Parse(x.Groups[1].Value)).DefaultIfEmpty(1).Max();
        pageCount = Math.Max(pageCount, page);

        // A list page always has either items or an "empty" layout with pagination; neither means the markup changed.
        if (items.Count == 0 && !html.Contains("class=\"items\"", StringComparison.OrdinalIgnoreCase))
            throw new OnlineException(OnlineProblem.Unrecognised, "The list page didn't contain any items the app recognises.");
        return new CatalogPage(items, page, pageCount);
    }

    public static CatalogItemDetails ParseDetails(string html, CatalogItem item, string siteUrl)
    {
        var download = DownloadRegex().Match(html);
        if (!download.Success)
            throw new OnlineException(OnlineProblem.Unrecognised, "The item page has no download link the app recognises.");

        var description = "";
        if (DescriptionRegex().Match(html) is { Success: true } d)
        {
            var text = BreakRegex().Replace(d.Groups["body"].Value, "\n");
            text = TagRegex().Replace(text, "");
            description = string.Join("\n", WebUtility.HtmlDecode(text).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        }

        var screenshot = ScreenshotRegex().Match(html) is { Success: true } s
            ? Absolute(siteUrl, s.Groups["src"].Value)
            : Absolute(siteUrl, $"/files/{item.Id}.jpg");

        return new CatalogItemDetails(
            item,
            SubmittedRegex().Match(html) is { Success: true } sub ? sub.Groups[1].Value : null,
            DownloadsRegex().Match(html) is { Success: true } dl ? int.Parse(dl.Groups[1].Value) : null,
            description,
            screenshot,
            Absolute(siteUrl, download.Groups["href"].Value),
            SizeRegex().Match(download.Groups["text"].Value) is { Success: true } size ? size.Groups[1].Value : null);
    }

    /// <summary>"pak DM (Models / Sets)" → ("pak DM", "Models / Sets").</summary>
    private static (string Name, string Category) SplitTitle(string title)
    {
        var open = title.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && title.EndsWith(')') ? (title[..open], title[(open + 2)..^1]) : (title, "");
    }

    private static string Absolute(string siteUrl, string path) =>
        path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : siteUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
