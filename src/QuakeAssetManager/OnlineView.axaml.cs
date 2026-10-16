using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using QuakeAssetManager.Core;
using QuakeAssetManager.Online;

namespace QuakeAssetManager;

/// <summary>Browse the online catalog for one asset type. Talks to the site only through IOnlineCatalog.</summary>
public partial class OnlineView : UserControl
{
    private MainWindow _host = null!;
    private PreviewPanel _preview = null!;
    private AssetType? _type;
    private CancellationTokenSource? _cts;
    private int _page = 1;
    private int _pageCount = 1;
    private int _version;
    private readonly ObservableCollection<OnlineItem> _items = [];

    public OnlineView()
    {
        InitializeComponent();
        Grid.ItemsSource = _items;
        SourceBox.SelectionChanged += (_, _) => { _page = 1; _ = LoadPage(); };
        PrevButton.Click += (_, _) => { if (_page > 1) { _page--; _ = LoadPage(); } };
        NextButton.Click += (_, _) => { if (_page < _pageCount) { _page++; _ = LoadPage(); } };
        RetryButton.Click += (_, _) => _ = LoadPage();
        OpenSiteButton.Click += async (_, _) => await OpenUrl(Source?.Url ?? Catalog.SiteUrl);
        Grid.SelectionChanged += (_, _) => _ = ShowDetails(Grid.SelectedItem as OnlineItem);
    }

    private IOnlineCatalog Catalog => _host.Catalog;
    private OnlineLink? Source => SourceBox.SelectedItem as OnlineLink;

    public void Init(MainWindow host, PreviewPanel preview)
    {
        _host = host;
        _preview = preview;
    }

    public void Show(AssetType type)
    {
        var changed = _type != type || SourceBox.ItemCount == 0;
        _type = type;
        HelpText.Text = Loc.F("Online: {0} assets from {1}. Select one to see its details, then Download to assets. Nothing is added until you press OK in the summary.", _type.DisplayName, Catalog.Name);
        _preview.Clear(Loc.T("Select an item to see its details."));
        if (!changed && _items.Count > 0)
            return;
        var sources = type.Online;
        SourceBox.ItemsSource = sources;
        SourceBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(OnlineLink.Label));
        if (sources.Count == 0)
        {
            _items.Clear();
            ShowMessage(Loc.F("No online source for {0} yet.", _type.DisplayName),
                Loc.F("{0} has no list for {1}. Lists can be added to \"online\" for this type in asset-types.json.", Catalog.Name, _type.DisplayName), retry: false);
            SourceBox.IsEnabled = PrevButton.IsEnabled = NextButton.IsEnabled = false;
            PageText.Text = "";
            return;
        }
        SourceBox.IsEnabled = true;
        _page = 1;
        if (SourceBox.SelectedIndex == 0)
            _ = LoadPage();
        else
            SourceBox.SelectedIndex = 0;
    }

    private async Task LoadPage()
    {
        if (Source is not { } source) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var version = ++_version;
        MessagePanel.IsVisible = false;
        LoadingText.IsVisible = true;
        _items.Clear();
        try
        {
            var page = await Catalog.BrowseAsync(source.Url, _page, ct);
            if (version != _version) return;
            _pageCount = page.PageCount;
            _page = page.Page;
            foreach (var item in page.Items)
                _items.Add(new OnlineItem(item, _host.DownloadHistory?.Contains(item.Id) == true));
            PageText.Text = $"{_page} / {_pageCount}";
            PrevButton.IsEnabled = _page > 1;
            NextButton.IsEnabled = _page < _pageCount;
            if (page.Items.Count == 0)
                ShowMessage(Loc.T("Nothing here yet."), Loc.T("This list on the website is empty."), retry: false);
            _ = LoadThumbnails(_items.ToList(), ct);
        }
        catch (OnlineException e) when (version == _version)
        {
            ShowProblem(e);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == _version) LoadingText.IsVisible = false;
        }
    }

    private async Task LoadThumbnails(List<OnlineItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            try
            {
                var bytes = await Catalog.GetImageAsync(item.Item.ThumbnailUrl, ct);
                using var ms = new MemoryStream(bytes);
                item.Thumbnail = new Bitmap(ms);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* keep the empty frame */ }
        }
    }

    private async Task ShowDetails(OnlineItem? item)
    {
        if (item is null) return;
        _preview.Show(new Preview(null, item.Category, Loc.T("Loading details…")), title: item.Name);
        CatalogItemDetails details;
        try
        {
            details = await Catalog.GetDetailsAsync(item.Item, _cts?.Token ?? default);
        }
        catch (OnlineException e)
        {
            _preview.Show(new Preview(null, item.Category, Problem(e).Message), title: item.Name,
                actions: [new PreviewAction(Loc.T("Open page in browser"), () => _ = OpenUrl(item.Item.DetailsUrl))]);
            return;
        }
        catch (OperationCanceledException) { return; }
        if (Grid.SelectedItem != item) return;

        var facts = new List<string> { item.Category };
        if (details.Submitted is not null) facts.Add(details.Submitted);
        if (details.Downloads is not null) facts.Add(Loc.F("{0} downloads", details.Downloads));
        if (details.SizeText is not null) facts.Add(details.SizeText);
        _preview.Show(new Preview(null, string.Join(" · ", facts), details.Description), title: item.Name, actions:
        [
            new PreviewAction(item.Downloaded ? Loc.T("Download to assets again") : Loc.T("Download to assets"), () => _ = Download(item, details)),
            new PreviewAction(Loc.T("Open page in browser"), () => _ = OpenUrl(item.Item.DetailsUrl)),
        ]);
        try
        {
            var bytes = await Catalog.GetImageAsync(details.ScreenshotUrl, default);
            if (Grid.SelectedItem != item) return;
            using var ms = new MemoryStream(bytes);
            _preview.SetImage(new Bitmap(ms));
        }
        catch (Exception) { /* no screenshot: text only */ }
    }

    private async Task Download(OnlineItem item, CatalogItemDetails details)
    {
        _host.SetStatus(Loc.F("Downloading {0}…", item.Name));
        var folder = Path.Combine(Path.GetTempPath(), "QuakeAssetManager", "downloads", Guid.NewGuid().ToString("N"));
        try
        {
            var file = await Catalog.DownloadAsync(details, folder, default);
            var committed = await _host.ImportWithSummary([file.Path], $"{item.Name} ({file.FileName})");
            if (committed)
            {
                _host.DownloadHistory?.Add(item.Item.Id);
                item.Downloaded = true;
            }
        }
        catch (OnlineException e)
        {
            await Dialogs.Message(_host, Loc.T("Download"), Problem(e).Title + " " + Problem(e).Message);
            _host.SetStatus("");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private (string Title, string Message) Problem(OnlineException e) => e.Problem == OnlineProblem.Unreachable
        ? (Loc.F("{0} can't be reached right now.", Catalog.Name), Loc.T("Check your internet connection and try again."))
        : (Loc.F("{0} returned a page the app doesn't recognise.", Catalog.Name), Loc.T("The site may have changed; the app needs an update to read it."));

    private void ShowProblem(OnlineException e)
    {
        var (title, message) = Problem(e);
        ShowMessage(title, message, retry: true);
        PageText.Text = "";
    }

    private void ShowMessage(string title, string text, bool retry)
    {
        MessageTitle.Text = title;
        MessageText.Text = text;
        RetryButton.IsVisible = retry;
        MessagePanel.IsVisible = true;
    }

    private async Task OpenUrl(string url)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(new Uri(url));
    }
}
