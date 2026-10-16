using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using QuakeAssetManager.Core;

namespace QuakeAssetManager;

/// <summary>A row on the Asset types tab of Settings.</summary>
public sealed class TypeRow(AssetType type) : Observable
{
    private bool _visible = type.Visible;

    public AssetType Type { get; } = type;
    public bool Visible { get => _visible; set => Set(ref _visible, value); }
    public string Title => Type.DisplayName;
    public string Description => Type.Description;
    public string Folders => Loc.F("Game folder: {0} · Assets folder: {1} · Names: {2}", Type.ActiveFolderPath, Type.AssetsFolderName, Type.NamesFile);
    public string ShowTip => Loc.T("Show this asset type as a tab");
}

/// <summary>Settings. All General fields are required; while any is missing this is the only screen.</summary>
public partial class SetupView : UserControl
{
    private static readonly string[] DefaultShown = ["models", "bmodels", "wad", "tracker", "particles"];

    private MainWindow _host = null!;
    private AppSettings _settings = new();
    private bool _loading;
    private List<TypeRow> _rows = [];

    public event Action<AppSettings>? Saved;
    public event Action? Cancelled;

    public SetupView()
    {
        InitializeComponent();
        QuakeBox.TextChanged += (_, _) => OnQuakeFolderChanged();
        AssetsBox.TextChanged += (_, _) => Revalidate();
        MapBox.TextChanged += (_, _) => Revalidate();
        ClientBox.SelectionChanged += (_, _) => Revalidate();
        QuakeBrowse.Click += async (_, _) => await Browse(QuakeBox, Loc.T("Choose your Quake folder"));
        AssetsBrowse.Click += async (_, _) => await Browse(AssetsBox, Loc.T("Choose the Assets folder"));
        CreateMissing.Click += (_, _) => CreateMissingFolders();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        OpenConfig.Click += async (_, _) => await Shell.OpenFolder(_host, _host.Config.Folder);
        RescanSubfolders.Click += async (_, _) => await _host.OfferSubfolders(includeHandled: true);
        ShowAll.Click += (_, _) => _rows.ForEach(r => r.Visible = true);
        ShowDefaults.Click += (_, _) => _rows.ForEach(r => r.Visible = DefaultShown.Contains(r.Type.Id));
        LanguageBox.ItemsSource = Loc.Languages;
        LanguageBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(Loc.Language.NativeName));
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CancelButton.IsVisible)
            {
                Cancelled?.Invoke();
                e.Handled = true;
            }
        };
    }

    public void Init(MainWindow host) => _host = host;

    public void SetDefaultButtonActive(bool active) => SaveButton.IsDefault = active;

    public void Load(AppSettings settings, bool mandatory)
    {
        _loading = true;
        _settings = settings;
        Title.Text = mandatory ? Loc.T("Set up Quake Asset Manager") : Loc.T("Settings");
        Subtitle.Text = mandatory
            ? Loc.T("All settings are required. You can use the app once every field is filled in.")
            : Loc.T("All settings are required.");
        SaveButton.Content = Loc.T("Save and continue");
        CancelButton.IsVisible = !mandatory;
        QuakeBox.Text = settings.QuakeFolder;
        AssetsBox.Text = settings.AssetsFolder;
        MapBox.Text = string.IsNullOrWhiteSpace(settings.Map) ? "dm4" : settings.Map;
        LanguageBox.SelectedItem = Loc.Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Loc.Languages[0];
        _rows = AssetType.Known.Select(t => new TypeRow(t)).ToList();
        TypeList.ItemsSource = _rows;
        ConfigPath.Text = Loc.F("Configuration folder: {0}", _host.Config.Folder);
        Tabs.SelectedIndex = 0;
        _loading = false;
        OnQuakeFolderChanged();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => QuakeBox.Focus());
    }

    private void OnQuakeFolderChanged()
    {
        if (_loading) return;
        var folder = QuakeBox.Text?.Trim() ?? "";
        var clients = SettingsValidator.FindClients(folder);
        var current = ClientBox.SelectedItem as string ?? _settings.Client;
        ClientBox.ItemsSource = clients;
        ClientBox.SelectedItem = clients.FirstOrDefault(c => c.Equals(current, StringComparison.OrdinalIgnoreCase))
                                 ?? clients.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(AssetsBox.Text) && Directory.Exists(Path.Combine(folder, "qw")))
            AssetsBox.Text = Path.Combine(folder, "assets");
        Revalidate();
    }

    private AppSettings Current() => new()
    {
        QuakeFolder = QuakeBox.Text?.Trim() ?? "",
        AssetsFolder = AssetsBox.Text?.Trim() ?? "",
        Client = ClientBox.SelectedItem as string ?? "",
        Map = MapBox.Text?.Trim() ?? "",
        Language = (LanguageBox.SelectedItem as Loc.Language)?.Code ?? "en",
        ScannedQuakeFolders = _settings.ScannedQuakeFolders,
    };

    private SettingsValidation Revalidate()
    {
        var s = Current();
        var v = SettingsValidator.Validate(s);
        Show(QuakeError, v.QuakeFolderError is null ? null : Loc.T(v.QuakeFolderError));
        QuakeOk.IsVisible = v.QuakeFolderError is null;
        Show(AssetsError, v.AssetsFolderError is null ? null : Loc.T(v.AssetsFolderError));
        AssetsNote.IsVisible = v.AssetsFolderError is null;
        AssetsNote.Text = Directory.Exists(s.AssetsFolder)
            ? Loc.T("Each asset type gets its own folder here.")
            : Loc.T("This folder will be created when you save.");
        Show(ClientError, ClientBox.ItemCount == 0 && v.QuakeFolderError is null
            ? Loc.T("No programs were found in the Quake folder.")
            : v.ClientError is null ? null : Loc.T(v.ClientError));
        Show(MapError, v.MapError is null ? null : Loc.T(v.MapError));
        CommandText.Text = Loc.F("Runs: {0} +map {1}", s.Client.Length > 0 ? s.Client : "<client>", s.Map.Length > 0 ? s.Map : "<map>");

        MissingPanel.IsVisible = v.MissingActiveFolders.Count > 0;
        MissingText.Text = Loc.T("These game folders don't exist yet, so their tabs can't show anything:") + "\n" +
                           string.Join("\n", v.MissingActiveFolders.Select(t => "• " + t.ActiveRelativePath));

        Banner.IsVisible = v.ErrorCount > 0;
        BannerText.Text = v.ErrorCount == 1 ? "⚠  " + Loc.T("1 setting still needs a value.") : "⚠  " + Loc.F("{0} settings still need a value.", v.ErrorCount);
        return v;
    }

    private static void Show(TextBlock block, string? error)
    {
        block.Text = error;
        block.IsVisible = error is not null;
    }

    private void CreateMissingFolders()
    {
        var s = Current();
        var v = SettingsValidator.Validate(s);
        if (v.QuakeFolderError is not null) return;
        try
        {
            foreach (var t in v.MissingActiveFolders)
                Directory.CreateDirectory(t.ActiveFolder(s.QuakeFolder));
            _host.SetStatus(Loc.F("Created {0}.", string.Join(", ", v.MissingActiveFolders.Select(t => t.ActiveRelativePath))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _host.SetStatus(Loc.T("Couldn't create the folders:") + " " + e.Message);
        }
        Revalidate();
    }

    private async void Save()
    {
        var v = Revalidate();
        if (!v.IsValid)
        {
            Tabs.SelectedIndex = 0;
            return;
        }
        var s = Current();
        try
        {
            Directory.CreateDirectory(s.AssetsFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Show(AssetsError, Loc.T("This folder can't be created:") + " " + e.Message);
            return;
        }
        // Asset type visibility goes to asset-types.json (saved by the main window).
        foreach (var row in _rows)
            row.Type.Visible = row.Visible;

        var missing = AssetType.All.Where(t => !Directory.Exists(t.ActiveFolder(s.QuakeFolder))).ToList();
        if (missing.Count > 0)
        {
            var create = await Dialogs.Confirm(_host, Loc.T("Missing game folders"),
                Loc.T("These game folders don't exist:") + "\n" + string.Join("\n", missing.Select(t => "• " + t.ActiveRelativePath)) +
                "\n\n" + Loc.T("Create them now? You can also create them later from each tab."), Loc.T("Create"));
            if (create)
            {
                foreach (var t in missing)
                    Directory.CreateDirectory(t.ActiveFolder(s.QuakeFolder));
            }
        }
        Saved?.Invoke(s);
    }

    private async Task Browse(TextBox target, string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        IStorageFolder? start = null;
        if (Directory.Exists(target.Text))
            start = await top.StorageProvider.TryGetFolderFromPathAsync(target.Text!);
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (result.Count > 0 && result[0].TryGetLocalPath() is { } path)
            target.Text = path;
    }

#if DEBUG
    public void SnapshotTab(int index) => Tabs.SelectedIndex = index;
#endif
}
