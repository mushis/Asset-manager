using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using QuakeAssetManager.Core;

namespace QuakeAssetManager;

/// <summary>One asset type: the Active folder (what the game loads), the Assets folder (options) or Online, and a preview.</summary>
public partial class AssetView : UserControl
{
    private MainWindow _host = null!;
    private AssetType _type = null!;
    private string? _selectAfterRefresh;
    private int _refreshToken;
    private CancellationTokenSource? _thumbs;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ActiveEntry> _activeEntries = [];
    private AssetsListing _assets = new([], []);
    private static bool _showInvalid;
    private bool _online;

    private static string TrashName => OperatingSystem.IsWindows() ? Loc.T("Recycle Bin") : Loc.T("trash");

    private static string? NameErr(string n) => AssetManager.NameError(n) is { } e ? Loc.T(e) : null;

    public AssetView()
    {
        InitializeComponent();
        ShowInvalid.IsCheckedChanged += (_, _) =>
        {
            _showInvalid = ShowInvalid.IsChecked == true;
            Rebuild();
        };
        ActiveList.SelectionChanged += (_, _) => OnActiveSelected();
        AssetsTree.SelectionChanged += (_, _) => OnAssetSelected();
        ActiveList.ContextRequested += OnActiveContext;
        AssetsTree.ContextRequested += OnAssetsContext;
        OpenActive.Click += async (_, _) => await Shell.OpenFolder(Owner, Manager.ActiveFolder(_type));
        OpenAssets.Click += async (_, _) => await Shell.OpenFolder(Owner, Manager.AssetsFolder(_type));
        NewFolder.Click += async (_, _) => await CreatePackFolder();
        AssetsTab.Click += (_, _) => SetOnline(false);
        OnlineTab.Click += (_, _) => SetOnline(true);
        CreateFolder.Click += (_, _) => Run(() =>
        {
            Manager.CreateActiveFolder(_type);
            _host.FoldersCreated();
            _host.SetStatus(Loc.F("Created {0}.", _type.ActiveRelativePath));
        });
    }

    private AssetManager Manager => _host.Manager!;

    public void Init(MainWindow host)
    {
        _host = host;
        Online.Init(host, Preview);
    }

    /// <summary>Show a type from the header: always opens on its Local tab.</summary>
    public void Show(AssetType type)
    {
        _online = false;
        if (_type != type)
        {
            _expanded.Clear();
            Preview.Clear();
        }
        _type = type;
        ShowInvalid.IsChecked = _showInvalid;
        ActiveHeader.Text = $"{Loc.T("Active")} · {_type.ActiveRelativePath}";
        AssetsHeader.Text = $"assets{Path.DirectorySeparatorChar}{_type.FolderName} · {_type.Description}";
        ToolTip.SetTip(AssetsHeader, Manager.AssetsFolder(_type));
        ToolTip.SetTip(ActiveHeader, Manager.ActiveFolder(_type));
        SetOnline(_online);
        Refresh();
    }

    private void SetOnline(bool online)
    {
        _online = online;
        AssetsTab.IsChecked = !online;
        OnlineTab.IsChecked = online;
        AssetsPanel.IsVisible = !online;
        Online.IsVisible = online;
        OpenAssets.IsVisible = NewFolder.IsVisible = !online;
        if (online)
        {
            AssetsTree.SelectedItem = null;
            Online.Show(_type);
        }
        else
        {
            Preview.Clear();
        }
        HelpMode.Apply(this);
    }

    public async void Refresh()
    {
        if (_host.Manager is null) return;
        var token = ++_refreshToken;
        var type = _type;
        var manager = Manager;
        try
        {
            var (active, assets) = await Task.Run(() => (manager.ListActive(type), manager.ListAssets(type)));
            if (token != _refreshToken) return;
            _activeEntries = active;
            _assets = assets;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _host.SetStatus(Loc.T("Couldn't read the folders:") + " " + e.Message);
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_host.Manager is null) return;
        var exists = Manager.ActiveFolderExists(_type);
        MissingFolder.IsVisible = !exists;
        MissingText.Text = Loc.F("The game folder {0} doesn't exist yet, so there's nothing loaded for {1}.", _type.ActiveRelativePath, _type.DisplayName);
        ActiveList.IsVisible = exists;
        OpenActive.IsEnabled = exists;

        // Active list
        var selectedActive = (ActiveList.SelectedItem as ActiveItem)?.Entry.FullPath;
        var activeItems = _activeEntries.Where(e => _showInvalid || e.IsValid).Select(e => new ActiveItem(e)).ToList();
        ActiveList.ItemsSource = activeItems;
        ActiveList.SelectedItem = activeItems.FirstOrDefault(i => i.Entry.FullPath == selectedActive);

        // Assets tree
        var selectedAsset = (AssetsTree.SelectedItem as AssetNode)?.FullPath;
        var nodes = new ObservableCollection<AssetNode>();
        foreach (var file in _assets.RootFiles)
            nodes.Add(FileNode(file));
        foreach (var pack in _assets.Packs)
            nodes.Add(FolderNode(pack, isPack: true));
        AssetsTree.ItemsSource = nodes;
        if (_selectAfterRefresh is { } wanted)
        {
            _selectAfterRefresh = null;
            selectedAsset = wanted;
            if (nodes.FirstOrDefault(n => PathEquals(n.FullPath, wanted)) is { } folder)
            {
                folder.IsExpanded = true;
                selectedAsset = folder.FullPath;
            }
        }
        var select = Flatten(nodes).FirstOrDefault(n => n.FullPath == selectedAsset);
        AssetsTree.SelectedItem = select;
        if (select is not null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AssetsTree.ScrollIntoView(select), Avalonia.Threading.DispatcherPriority.Background);

        _thumbs?.Cancel();
        _thumbs = new CancellationTokenSource();
        Thumbnails.Load(activeItems.Cast<ThumbItem>().Concat(Flatten(nodes)), Manager.GameData.Palette, _thumbs.Token);

        var hidden = _activeEntries.Count(e => !e.IsValid);
        var notes = new List<string>();
        if (!_showInvalid && hidden > 0)
            notes.Add(hidden == 1 ? Loc.T("1 invalid file or folder hidden in the game folder") : Loc.F("{0} invalid files or folders hidden in the game folder", hidden));
        if (_assets.RootFiles.Count > 0)
            notes.Add(Loc.F("{0} files sit directly in {1}; move them into a folder", _assets.RootFiles.Count, $"assets{Path.DirectorySeparatorChar}{_type.FolderName}"));
        _host.SetHint(notes.Count > 0 ? string.Join(" · ", notes) + "." : "");
    }

    /// <summary>After an import: select (and expand) this Assets folder once the lists are rebuilt.</summary>
    public void SelectFolderAfterRefresh(string folderPath)
    {
        _selectAfterRefresh = folderPath;
        Refresh();
    }

    private static bool PathEquals(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private AssetNode FolderNode(AssetFolderEntry folder, bool isPack)
    {
        var node = new AssetNode
        {
            Name = folder.Name,
            FullPath = folder.FullPath,
            IsFolder = true,
            IsPack = isPack,
            IsValid = isPack,
            Modified = folder.Modified,
        };
        if (isPack || _showInvalid)
        {
            if (_showInvalid)
                foreach (var sub in folder.Folders)
                    node.Children.Add(FolderNode(sub, isPack: false));
            foreach (var file in folder.Files.Where(f => _showInvalid || f.IsValid))
                node.Children.Add(FileNode(file));
        }
        node.IsExpanded = _expanded.Contains(folder.FullPath);
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AssetNode.IsExpanded)) return;
            if (node.IsExpanded) _expanded.Add(node.FullPath);
            else _expanded.Remove(node.FullPath);
        };
        return node;
    }

    private static AssetNode FileNode(AssetFileEntry file) => new()
    {
        Name = file.Name,
        FullPath = file.FullPath,
        IsValid = file.IsValid,
        IsActive = file.IsActive,
        Modified = file.Modified,
    };

    private static IEnumerable<AssetNode> Flatten(IEnumerable<AssetNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    // ---------- Preview and its actions ----------

    private void OnActiveSelected()
    {
        if (ActiveList.SelectedItem is not ActiveItem item)
            return;
        AssetsTree.SelectedItem = null;
        var actions = ActiveActions(item);
        if (item.Entry.IsFolder)
            Preview.Clear(Loc.F("{0} holds {1}. The game doesn't load subfolders.", item.Name, Loc.Files(item.Entry.FileCount)), actions);
        else
            Preview.Show(Previews.FromFile(item.Entry.FullPath, Manager.GameData.Palette), modified: item.Entry.Modified, actions: actions);
    }

    private void OnAssetSelected()
    {
        if (AssetsTree.SelectedItem is not AssetNode node)
            return;
        ActiveList.SelectedItem = null;
        var actions = AssetActions(node);
        if (node.IsFolder)
        {
            Preview.Clear($"{node.Name}: {Loc.Files(node.Children.Count(c => !c.IsFolder))}.", actions);
            return;
        }
        var main = Previews.FromFile(node.FullPath, Manager.GameData.Palette);
        Preview? compare = null;
        if (node.IsValid && !node.IsActive && Manager.ActiveFolderExists(_type))
        {
            var baseName = Path.GetFileNameWithoutExtension(node.Name);
            var inGame = Directory.EnumerateFiles(Manager.ActiveFolder(_type))
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                                     Manager.IsValid(_type, Path.GetFileName(f)));
            compare = inGame is null
                ? new Preview(null, Loc.T("Nothing in the game folder; the game uses its built-in texture."))
                : Previews.FromFile(inGame, Manager.GameData.Palette);
        }
        Preview.Show(main, compare, node.Modified, actions);
    }

    private List<PreviewAction> ActiveActions(ActiveItem item) => item.IsInvalid
        ?
        [
            new(Loc.T("Move to assets…"), () => _ = MoveActiveToAssets(item)),
            new(Loc.T("Delete"), () => _ = DeleteActive(item), Danger: true),
        ]
        :
        [
            new(Loc.T("Open folder"), () => _ = Shell.OpenFolder(Owner, Manager.ActiveFolder(_type))),
            new(Loc.T("Delete"), () => _ = DeleteActive(item), Danger: true),
        ];

    /// <summary>Delete a game file; optionally also its copies in Assets (same content).</summary>
    private async Task DeleteActive(ActiveItem item)
    {
        if (item.Entry.IsFolder)
        {
            await Delete(item.Entry.FullPath, item.Name);
            return;
        }
        var copies = Manager.FindCopiesInAssets(_type, item.Entry.FullPath);
        var message = item.IsInvalid
            ? Loc.F("Move {0} to the {1}?", item.Name, TrashName)
            : Loc.F("Move {0} to the {1}? The game will use its built-in version.", item.Name, TrashName);
        var (confirmed, alsoCopies) = await Dialogs.ConfirmDelete(Owner, message, copies, Manager.AssetsFolder(_type));
        if (!confirmed) return;
        Run(() =>
        {
            Manager.Delete(item.Entry.FullPath);
            if (alsoCopies)
                foreach (var copy in copies)
                    Manager.Delete(copy);
            _host.SetStatus(alsoCopies && copies.Count > 0
                ? Loc.F("Moved {0} and {1} in Assets to the {2}.", item.Name, Loc.Files(copies.Count), TrashName)
                : Loc.F("Moved {0} to the {1}.", item.Name, TrashName));
        });
    }

    private List<PreviewAction> AssetActions(AssetNode node)
    {
        var actions = new List<PreviewAction>();
        if (node.IsFolder && node.IsPack)
        {
            actions.Add(new(Loc.T("Use these"), () => _ = UseFolder(node)));
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
        }
        else if (!node.IsFolder && node.IsValid)
        {
            actions.Add(new(Loc.T("Use this file"), () => Run(() =>
            {
                var target = Manager.UseFile(_type, node.FullPath);
                _host.SetStatus(Loc.F("{0} is now in the game.", Path.GetFileName(target)));
            })));
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
        }
        else
        {
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
            actions.Add(new(Loc.T("Move to folder…"), () => _ = MoveWithinAssets(node)));
        }
        actions.Add(new(Loc.T("Delete"), () => _ = Delete(node.FullPath, node.Name), Danger: true));
        return actions;
    }

    // ---------- Context menus (same actions as the preview buttons) ----------

    private static T? ItemFrom<T>(RoutedEventArgs e) where T : class =>
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as T
        ?? (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as T;

    private void OnActiveContext(object? sender, ContextRequestedEventArgs e)
    {
        var item = ItemFrom<ActiveItem>(e);
        if (item is null) return;
        ActiveList.SelectedItem = item;
        OpenMenu(ActiveActions(item), ActiveList);
        e.Handled = true;
    }

    private void OnAssetsContext(object? sender, ContextRequestedEventArgs e)
    {
        var node = ItemFrom<AssetNode>(e);
        if (node is null) return;
        AssetsTree.SelectedItem = node;
        OpenMenu(AssetActions(node), AssetsTree);
        e.Handled = true;
    }

    private static void OpenMenu(IEnumerable<PreviewAction> actions, Control target)
    {
        var menu = new ContextMenu();
        foreach (var a in actions)
        {
            if (a.Danger) menu.Items.Add(new Separator());
            var item = new MenuItem { Header = a.Label };
            item.Click += (_, _) => a.Run();
            menu.Items.Add(item);
        }
        menu.Open(target);
    }

    // ---------- Actions ----------

    private Window Owner => (Window)TopLevel.GetTopLevel(this)!;

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _ = Dialogs.Message(Owner, Loc.T("Something went wrong"), e.Message);
        }
        Refresh();
    }

    private async Task MoveActiveToAssets(ActiveItem item)
    {
        var isFolder = item.Entry.IsFolder;
        var choice = await Dialogs.ChooseDestination(Owner,
            Loc.F("Move {0} to assets", item.Entry.Name), Loc.T("Move"),
            Manager.PackFolderNames(_type),
            isFolder ? item.Entry.Name : Manager.NewDatedFolderName(_type),
            isFolder ? null : Manager.ValidNames[_type.Kind].SuggestName(item.Entry.Name),
            NameErr);
        if (choice is not { } c) return;
        Run(() =>
        {
            Manager.MoveActiveToAssets(_type, item.Entry.FullPath, c.Folder, c.FileName);
            _host.SetStatus(Loc.F("Moved {0} to {1}.", item.Entry.Name, Path.Combine("assets", _type.FolderName, c.Folder)));
        });
    }

    private async Task MoveWithinAssets(AssetNode node)
    {
        var folders = Manager.PackFolderNames(_type);
        var current = Path.GetFileName(Path.GetDirectoryName(node.FullPath)) ?? "";
        var choice = await Dialogs.ChooseDestination(Owner, Loc.F("Move {0}", node.Name), Loc.T("Move"),
            folders, folders.FirstOrDefault(f => f != current) ?? Manager.NewDatedFolderName(_type), null, NameErr);
        if (choice is not { } c) return;
        Run(() =>
        {
            Manager.MoveWithinAssets(_type, node.FullPath, c.Folder);
            _host.SetStatus(Loc.F("Moved {0} to {1}.", node.Name, c.Folder));
        });
    }

    private async Task Rename(AssetNode node)
    {
        var suggestion = node.IsFolder || node.IsValid ? node.Name : Manager.ValidNames[_type.Kind].SuggestName(node.Name);
        var name = await Dialogs.Rename(Owner, Loc.F("Rename {0}", node.Name), node.Name, suggestion, NameErr);
        if (name is null || name == node.Name) return;
        if (!node.IsFolder && node.IsValid && !Manager.IsValid(_type, name) &&
            !await Dialogs.Confirm(Owner, Loc.T("Rename"), Loc.F("The game never loads \"{0}\", so this file won't be usable any more. Rename anyway?", name), Loc.T("Rename")))
            return;
        Run(() =>
        {
            var dest = Manager.RenameAsset(_type, node.FullPath, name);
            if (node.IsFolder && _expanded.Remove(node.FullPath)) _expanded.Add(dest);
            _host.SetStatus(Loc.F("Renamed {0} to {1}.", node.Name, name));
        });
    }

    private async Task Delete(string path, string name)
    {
        if (!await Dialogs.Confirm(Owner, Loc.T("Delete"), Loc.F("Move {0} to the {1}?", name, TrashName), Loc.T("Delete"), danger: true))
            return;
        Run(() =>
        {
            Manager.Delete(path);
            _host.SetStatus(Loc.F("Moved {0} to the {1}.", name, TrashName));
        });
    }

    private async Task UseFolder(AssetNode node)
    {
        if (!Manager.ActiveFolderExists(_type))
        {
            await Dialogs.Message(Owner, Loc.T("Use these"), Loc.F("Create the game folder {0} first.", _type.ActiveRelativePath));
            return;
        }
        // Only the folder's files are copied; nothing else in the game folder is touched.
        var plan = Manager.PlanUseFolder(_type, node.FullPath);
        if (plan.FilesToCopy.Count == 0)
        {
            await Dialogs.Message(Owner, Loc.T("Use these"), Loc.F("\"{0}\" has no valid {1} files to use.", node.Name, _type.DisplayName));
            return;
        }
        Run(() =>
        {
            var copied = Manager.UseFolder(_type, plan, []);
            var skipped = plan.InvalidSkipped > 0 ? " (" + Loc.F("invalid, skipped: {0}", plan.InvalidSkipped) + ")" : "";
            _host.SetStatus(Loc.F("Copied {0} from {1} into the game.", Loc.Files(copied), node.Name) + skipped);
        });
    }

    private async Task CreatePackFolder()
    {
        var name = await Dialogs.Rename(Owner, Loc.T("New folder"), "", Manager.NewDatedFolderName(_type), n =>
            NameErr(n) ?? (Directory.Exists(Path.Combine(Manager.AssetsFolder(_type), n)) ? Loc.T("This folder already exists.") : null), Loc.T("Create"));
        if (name is null) return;
        Run(() => Directory.CreateDirectory(Path.Combine(Manager.AssetsFolder(_type), name)));
    }

#if DEBUG
    public void SnapshotSelectFirstOption()
    {
        if (AssetsTree.ItemsSource is not IEnumerable<AssetNode> nodes) return;
        var pack = nodes.FirstOrDefault(n => n.IsPack && n.Children.Any(c => !c.IsFolder && c.IsValid && !c.IsActive))
                   ?? nodes.FirstOrDefault(n => n.IsPack);
        if (pack is null) return;
        pack.IsExpanded = true;
        AssetsTree.SelectedItem = pack.Children.FirstOrDefault(c => !c.IsFolder && c.IsValid && !c.IsActive) ?? pack.Children.FirstOrDefault();
    }

    public void SnapshotShowInvalid(bool on) => ShowInvalid.IsChecked = on;

    public void SnapshotOnline(bool on) => SetOnline(on);
#endif
}
