using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;
using QuakeAssetManager.Online;

namespace QuakeAssetManager;

public partial class MainWindow : Window
{
    private readonly FolderWatcher _watcher;
    private AppSettings _settings = new();
    private string _tab = "models";

    public AssetManager? Manager { get; private set; }
    public AppSettings Settings => _settings;

    /// <summary>asset-types.json, names/*.json and app.json, in the same folder as settings.json.</summary>
    public AssetConfig Config { get; private set; } = new(AssetConfig.DefaultFolder);

    /// <summary>The website the Online tabs browse. Only the interface is used outside this property.</summary>
    public IOnlineCatalog Catalog { get; private set; } = null!;

    public DownloadHistory? DownloadHistory { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        _watcher = new FolderWatcher(OnFoldersChanged);

        TabArchives.Click += (_, _) => SelectTab("archives");
        TabArchives.Margin = new Avalonia.Thickness(0, 0, 4, 4);
        TestButton.Content = "▶  " + Loc.T("Test");
        TestButton.Click += OnTest;
        SettingsButton.Click += (_, _) => ShowSetup(mandatory: false);
        HelpButton.IsCheckedChanged += (_, _) => HelpMode.Set(HelpButton.IsChecked == true);
        AboutButton.Click += async (_, _) => await Dialogs.About(this);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        HelpMode.Changed += () => HelpMode.Apply(this);

        Setup.Init(this);
        Assets.Init(this);
        Archives.Init(this);
        Setup.Saved += OnSettingsSaved;
        Setup.Cancelled += () => SelectTab(_tab);

        Opened += (_, _) =>
        {
            Start();
#if DEBUG
            if (Environment.GetEnvironmentVariable("QAM_SNAPSHOT") is { Length: > 0 } snapshots)
                _ = RunSnapshots(snapshots);
#endif
        };
        Closed += (_, _) => _watcher.Dispose();
    }

    private void LoadConfig()
    {
        Config = AssetConfig.Load();
        Catalog = new GfxQuakeWorldCatalog(
            Path.Combine(AppPaths.CacheFolder, "online"),
            Config.App.OnlineSiteName, Config.App.OnlineSiteUrl,
            $"QuakeAssetManager/{typeof(MainWindow).Assembly.GetName().Version?.ToString(2)} (desktop app)");
        BuildTabs();
    }

    /// <summary>One header button per shown asset type, in the order of asset-types.json.</summary>
    private void BuildTabs()
    {
        foreach (var old in Tabs.Children.OfType<ToggleButton>().Where(t => t != TabArchives).ToList())
            Tabs.Children.Remove(old);
        var index = 0;
        foreach (var type in AssetType.All)
        {
            var tab = new ToggleButton { Content = type.DisplayName, Tag = type.Id };
            tab.Classes.Add("tab");
            ToolTip.SetTip(tab, $"{type.Description}: {type.ActiveRelativePath}");
            tab.Click += (_, _) => SelectTab(type.Id);
            tab.Margin = new Avalonia.Thickness(0, 0, 4, 4);
            Tabs.Children.Insert(index++, tab);
        }
    }

    private IEnumerable<ToggleButton> AllTabs => Tabs.Children.OfType<ToggleButton>();

    private void Start()
    {
        LoadConfig();
        _settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(_settings.Map))
            _settings.Map = "dm4";
        if (!SettingsValidator.Validate(_settings).IsValid)
            ShowSetup(mandatory: true);
        else
            _ = StartSession();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>A note about the current view, e.g. how many invalid files are hidden.</summary>
    public void SetHint(string text) => HintText.Text = text;

    private void ShowSetup(bool mandatory)
    {
        Setup.Load(_settings.Clone(), mandatory);
        ShowOnly(Setup);
        SetChromeEnabled(!mandatory);
        foreach (var tab in AllTabs)
            tab.IsChecked = false;
    }

    private async void OnSettingsSaved(AppSettings saved)
    {
        var languageChanged = saved.Language != _settings.Language;
        _settings = saved;
        if (languageChanged)
        {
            try
            {
                _settings.Save();
                Config.SaveTypes();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Loc.Set(saved.Language);
            ((App)Avalonia.Application.Current!).ReopenMainWindow();
            return;
        }
        try
        {
            _settings.Save();
            Config.SaveTypes();
            LoadConfig();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Settings"), Loc.T("Your settings couldn't be saved:") + " " + e.Message);
        }
        await StartSession();
    }

    private async Task StartSession()
    {
        ShowOnly(null);
        LoadingText.IsVisible = true;
        SetChromeEnabled(false);
        _watcher.Watch([]);
        var settings = _settings;
        try
        {
            var (manager, result) = await Task.Run(() =>
            {
                var m = new AssetManager(settings, GameData.Load(settings.QuakeFolder), Config);
                var firstTime = !settings.ScannedQuakeFolders.Contains(settings.QuakeFolder, StringComparer.OrdinalIgnoreCase);
                var r = m.ImportAll(DateTime.Now);
                return (m, (Result: r, FirstTime: firstTime));
            });
            Manager = manager;
            DownloadHistory = new DownloadHistory(settings.AssetsFolder);
            try { _settings.Save(); } catch (IOException) { }
            SetStatus(ImportMessage(result.Result, result.FirstTime));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LoadingText.IsVisible = false;
            await Dialogs.Message(this, Loc.T("Couldn't read the Quake folder"), e.Message);
            ShowSetup(mandatory: true);
            return;
        }
        LoadingText.IsVisible = false;
        SetChromeEnabled(true);
        WatchFolders();
        if (Manager.GameData.Palette is null)
            SetStatus(StatusText.Text + " " + Loc.T("The Quake palette wasn't found, so PAK images can't be previewed."));
        if (Config.Problems.Count > 0)
            await Dialogs.Message(this, Loc.T("Configuration"), string.Join("\n", Config.Problems));
        SelectTab(_tab);
        await OfferLeftoverImports();
        await OfferSubfolders();
    }

    /// <summary>An import whose summary dialog was still open when the app closed: keep it or throw it away.</summary>
    private async Task OfferLeftoverImports()
    {
        if (Manager is null) return;
        foreach (var session in new Importer(Manager).FindLeftovers())
        {
            var keep = await Dialogs.Confirm(this, Loc.T("Unfinished import"),
                Loc.F("An import from \"{0}\" wasn't confirmed before the app closed ({1}). Add the files to Assets?", session.Title, Loc.Files(session.Files.Count)),
                Loc.T("Add to Assets"));
            if (keep) session.Commit(); else session.Revert();
        }
        RefreshCurrent();
    }

    // ---------- Drag and drop ----------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var ok = Manager is not null && e.Data.Contains(DataFormats.Files);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = ok;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (Manager is null) return;
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (paths.Count > 0)
            await ImportWithSummary(paths);
    }

    /// <summary>
    /// Stage dropped or downloaded files, show the summary, then add them to Assets (OK) or throw them away (Revert).
    /// After OK the app opens the asset type and folder that received the most files. Returns true when files were added.
    /// </summary>
    public Task<bool> ImportWithSummary(IReadOnlyList<string> paths, string? title = null) =>
        ImportWithSummary(manager => new Importer(manager).Prepare(paths, DateTime.Now, title));

    /// <summary>
    /// Read and classify, ask where unclear folders should go, stage, then show the summary (OK or Revert).
    /// </summary>
    public async Task<bool> ImportWithSummary(Func<AssetManager, ImportPlan> prepare)
    {
        if (Manager is null) return false;
        var manager = Manager;
        SetStatus(Loc.T("Reading the files…"));
        ImportSession session;
        try
        {
            var plan = await Task.Run(() => prepare(manager));
            IReadOnlyDictionary<string, AssetType?>? answers = null;
            if (plan.Questions.Count > 0)
            {
                answers = await Dialogs.WhereShouldTheseGo(this, plan.Title, plan.Questions);
                if (answers is null)
                {
                    SetStatus(Loc.F("Nothing was added from {0}.", plan.Title));
                    return false;
                }
            }
            session = await Task.Run(() => new Importer(manager).Stage(plan, answers));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("");
            await Dialogs.Message(this, Loc.T("Import"), Loc.T("The files couldn't be read:") + " " + ex.Message);
            return false;
        }
        var commit = await Dialogs.ImportSummary(this, session);
        try
        {
            if (commit)
            {
                var added = session.Commit();
                SetStatus(Loc.F("Added {0} from {1} to Assets.", Loc.Files(added), session.Title));
            }
            else
            {
                session.Revert();
                SetStatus(Loc.F("Nothing was added from {0}.", session.Title));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Import"), Loc.T("Some files couldn't be moved into Assets:") + " " + ex.Message);
        }
        if (commit && session.Busiest() is { } target)
        {
            ShowFolder(target.Kind, target.FolderPath);
        }
        else
        {
            if (commit && session.Files.Count > 0)
            {
                // Everything went to hidden types: say where, since there's no tab to open.
                var where = string.Join(", ", session.Files.Select(f => f.Kind).Distinct()
                    .Select(k => AssetType.ById(k)).OfType<AssetType>().Select(t => $"assets{Path.DirectorySeparatorChar}{t.FolderName} ({t.DisplayName}, {Loc.T("hidden")})"));
                SetStatus(Loc.F("Added {0} from {1} to Assets.", Loc.Files(session.Files.Count), session.Title) + " " + where);
            }
            RefreshCurrent();
        }
        return commit && session.Files.Count > 0;
    }

    /// <summary>Open an asset type's Local tab with one of its folders selected.</summary>
    public void ShowFolder(string typeId, string folderPath)
    {
        if (AssetType.ById(typeId) is not { Visible: true } type) return;
        SelectTab(type.Id);
        Assets.SelectFolderAfterRefresh(folderPath);
    }

    /// <summary>After the scan: offer the subfolders found in the game folders (usually alternative asset sets).</summary>
    public async Task OfferSubfolders(bool includeHandled = false)
    {
        if (Manager is null) return;
        if (includeHandled) Manager.ResetHandledSubfolders();
        var found = Manager.FindSubfolders();
        if (found.Count == 0)
        {
            if (includeHandled) SetStatus(Loc.T("No subfolders found in the game folders."));
            return;
        }
        var choice = await Dialogs.ChooseSubfolders(this, found);
        if (choice is null) return;
        var (selected, deleteOriginals, dontAskOthers) = choice.Value;
        if (deleteOriginals && selected.Count > 0 &&
            !await Dialogs.Confirm(this, Loc.T("Copy and delete"),
                Loc.T("After copying, these folders will be moved from your Quake folder to the Recycle Bin:") + "\n" +
                string.Join("\n", selected.Select(f => "• " + Path.GetRelativePath(_settings.QuakeFolder, f.FullPath))), Loc.T("Copy and delete"), danger: true))
            return;
        var manager = Manager;
        try
        {
            var result = await Task.Run(() => manager.ImportSubfolders(selected, deleteOriginals));
            if (dontAskOthers)
                manager.MarkSubfoldersHandled(found.Except(selected));
            try { _settings.Save(); } catch (IOException) { }
            SetStatus(Loc.F("copied {0} to Assets ({1} already there).", Loc.Files(result.Copied), result.Skipped));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(this, Loc.T("Something went wrong"), e.Message);
        }
        RefreshCurrent();
    }

    private static string ImportMessage(ImportResult r, bool firstTime)
    {
        var prefix = firstTime ? Loc.T("First scan of this Quake folder:") + " " : Loc.T("Scan:") + " ";
        if (r.Copied == 0)
            return prefix + Loc.T("everything is already in Assets.");
        var folders = r.CreatedFolders.Count > 0 ? " " + Loc.F("New folders: {0}.", string.Join(", ", r.CreatedFolders.Distinct())) : "";
        return prefix + Loc.F("copied {0} to Assets ({1} already there).", Loc.Files(r.Copied), r.Skipped) + folders;
    }

    public void WatchFolders()
    {
        if (Manager is null) return;
        var folders = AssetType.All.SelectMany(t => new[] { Manager.ActiveFolder(t), Manager.AssetsFolder(t) });
        _watcher.Watch(folders);
    }

    private async void OnFoldersChanged(IReadOnlyCollection<string> roots)
    {
        if (Manager is null) return;
        var manager = Manager;
        // New files in a game folder are copied into Assets so a copy always exists.
        var changedActive = AssetType.All.Where(t => roots.Any(r => PathEquals(r, manager.ActiveFolder(t)))).ToList();
        if (changedActive.Count > 0)
        {
            try
            {
                var result = await Task.Run(() => changedActive.Aggregate(ImportResult.Empty, (acc, t) => acc + manager.Import(t, DateTime.Now)));
                if (result.Copied > 0)
                    SetStatus(Loc.F("Copied {0} from the game folder to Assets.", Loc.Files(result.Copied)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SetStatus(Loc.T("Couldn't copy new files to Assets:") + " " + e.Message);
            }
        }
        RefreshCurrent();
    }

    public void RefreshCurrent()
    {
        if (Assets.IsVisible) Assets.Refresh();
        if (Archives.IsVisible) Archives.RefreshTree();
    }

    /// <summary>Called after a folder is created, so the watcher can see it.</summary>
    public void FoldersCreated() => WatchFolders();

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void SelectTab(string tab)
    {
        if (Manager is null)
        {
            ShowSetup(mandatory: true);
            return;
        }
        _tab = tab;
        var type = AssetType.All.FirstOrDefault(t => t.Id.Equals(tab, StringComparison.OrdinalIgnoreCase));
        if (tab != "archives" && type is null)
        {
            type = AssetType.All.FirstOrDefault();
            tab = type?.Id ?? "archives";
            _tab = tab;
        }
        foreach (var t in AllTabs)
            t.IsChecked = (string)t.Tag! == tab;
        SetHint("");
        if (type is null)
        {
            ShowOnly(Archives);
            Archives.Show();
        }
        else
        {
            ShowOnly(Assets);
            Assets.Show(type);
        }
    }

    private void ShowOnly(Control? view)
    {
        Setup.IsVisible = view == Setup;
        Assets.IsVisible = view == Assets;
        Archives.IsVisible = view == Archives;
        Setup.SetDefaultButtonActive(view == Setup);
        HelpMode.Apply(this);
    }

    private void SetChromeEnabled(bool enabled)
    {
        Tabs.IsEnabled = enabled;
        TestButton.IsEnabled = enabled;
        SettingsButton.IsEnabled = enabled;
    }

    private async void OnTest(object? sender, RoutedEventArgs e)
    {
        var v = SettingsValidator.Validate(_settings);
        if (v.ClientError is not null || v.MapError is not null || v.QuakeFolderError is not null)
        {
            await Dialogs.Message(this, Loc.T("Test"), Loc.T("Check your settings first:") + " " + Loc.T((v.ClientError ?? v.MapError ?? v.QuakeFolderError)!));
            return;
        }
        try
        {
            Shell.RunClient(_settings);
            SetStatus(Loc.F("Started {0}", $"{_settings.Client} +map {_settings.Map}"));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            await Dialogs.Message(this, Loc.T("Test"), Loc.F("{0} couldn't be started:", _settings.Client) + " " + ex.Message);
        }
    }
}
