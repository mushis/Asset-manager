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
    private static bool _showInvalid = true;
    private bool _online;
    private bool _rebuilding;

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
        FilterBox.TextChanged += (_, _) =>
        {
            ClearFilter.IsVisible = !string.IsNullOrEmpty(FilterBox.Text);
            Rebuild();
        };
        ClearFilter.Click += (_, _) => FilterBox.Text = "";
        CreateFolder.Click += (_, _) => Run(() =>
        {
            Manager.CreateActiveFolder(_type);
            _host.FoldersCreated();
            _host.SetStatus(Loc.F("Created {0}.", _type.ActiveRelativePath));
        });
    }

    private AssetManager Manager => _host.Manager!;

    private string Filter => FilterBox.Text?.Trim() ?? "";

    public void Init(MainWindow host)
    {
        _host = host;
        Online.Init(host, Preview);
    }

    /// <summary>Show a type from the header: always opens on its Local tab, with the filter cleared.</summary>
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
        ToolTip.SetTip(AssetsTab, Loc.T("Your local copies of assets for this type (the Local assets folder):") + "\n" + Manager.AssetsFolder(_type));
        OnlineTab.IsVisible = _host.Settings.ShowOnline;
        FilterBox.Text = "";
        SetOnline(false);
        Refresh();
    }

    private void SetOnline(bool online)
    {
        if (!_host.Settings.ShowOnline) online = false;
        if (online != _online) FilterBox.Text = "";
        _online = online;
        AssetsTab.IsChecked = !online;
        OnlineTab.IsChecked = online;
        AssetsPanel.IsVisible = !online;
        Online.IsVisible = online;
        OpenAssets.IsVisible = NewFolder.IsVisible = FilterBox.IsVisible = !online;
        if (online)
        {
            AssetsTree.SelectedItems?.Clear();
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
        if (_host.Manager is null || _type is null) return;
        var exists = Manager.ActiveFolderExists(_type);
        MissingFolder.IsVisible = !exists;
        MissingText.Text = Loc.F("The game folder {0} doesn't exist yet, so there's nothing loaded for {1}.", _type.ActiveRelativePath, _type.DisplayName);
        ActiveList.IsVisible = exists;
        OpenActive.IsEnabled = exists;
        var anyName = Manager.ValidNames.TryGetValue(_type.Id, out var set) && set.AcceptsAnyName;

        _rebuilding = true;
        try
        {
            // Active list: "Show invalid" only affects this list.
            var selectedActive = SelectedActive.Select(i => i.Entry.FullPath).ToHashSet(StringComparer.Ordinal);
            var activeItems = _activeEntries.Where(e => _showInvalid || e.IsValid).Select(e => new ActiveItem(e, anyName)).ToList();
            ActiveList.ItemsSource = activeItems;
            foreach (var item in activeItems.Where(i => selectedActive.Contains(i.Entry.FullPath)))
                ActiveList.SelectedItems?.Add(item);

            // Assets tree: every file and folder, narrowed by the filter.
            var selectedAssets = SelectedNodes.Select(n => n.FullPath).ToHashSet(StringComparer.Ordinal);
            var nodes = new ObservableCollection<AssetNode>();
            foreach (var pack in _assets.Packs)
                if (FolderNode(pack, isPack: true) is { } node)
                    nodes.Add(node);
            foreach (var file in _assets.RootFiles.Where(Matches))
                nodes.Add(FileNode(file));
            AssetsTree.ItemsSource = nodes;
            NoMatches.IsVisible = Filter.Length > 0 && nodes.Count == 0;
            NoMatches.Text = Loc.F("No file names start with \"{0}\".", Filter);
            if (_selectAfterRefresh is { } wanted)
            {
                _selectAfterRefresh = null;
                selectedAssets = [wanted];
                if (nodes.FirstOrDefault(n => PathEquals(n.FullPath, wanted)) is { } folder)
                {
                    folder.IsExpanded = true;
                    selectedAssets = [folder.FullPath];
                }
            }
            var reselect = Flatten(nodes).Where(n => selectedAssets.Contains(n.FullPath)).ToList();
            foreach (var n in reselect)
                AssetsTree.SelectedItems?.Add(n);
            if (reselect.FirstOrDefault() is { } first)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AssetsTree.ScrollIntoView(first), Avalonia.Threading.DispatcherPriority.Background);

            _thumbs?.Cancel();
            _thumbs = new CancellationTokenSource();
            Thumbnails.Load(activeItems.Cast<ThumbItem>().Concat(Flatten(nodes)), Manager.GameData.Palette, _thumbs.Token);
        }
        finally
        {
            _rebuilding = false;
        }

        // Refresh the preview for what's still selected.
        if (!_online)
        {
            if (SelectedActive.Count > 0) OnActiveSelected();
            else if (SelectedNodes.Count > 0) OnAssetSelected();
            else Preview.Clear();
        }

        var hidden = _activeEntries.Count(e => !e.IsValid);
        var notes = new List<string>();
        if (!_showInvalid && hidden > 0)
            notes.Add(hidden == 1 ? Loc.T("1 invalid file or folder hidden in the game folder") : Loc.F("{0} invalid files or folders hidden in the game folder", hidden));
        if (_assets.RootFiles.Count > 0)
            notes.Add(Loc.F("{0} files sit directly in {1}; move them into a folder", _assets.RootFiles.Count, $"assets{Path.DirectorySeparatorChar}{_type.FolderName}"));
        _host.SetHint(notes.Count > 0 ? string.Join(" · ", notes) + "." : "");
    }

    private bool Matches(AssetFileEntry file) => Filter.Length == 0 || file.Name.StartsWith(Filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>After an import: select (and expand) this Assets folder once the lists are rebuilt.</summary>
    public void SelectFolderAfterRefresh(string folderPath)
    {
        _selectAfterRefresh = folderPath;
        Refresh();
    }

    private static bool PathEquals(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>A folder and everything in it; with a filter, only folders that hold a match (expanded).</summary>
    private AssetNode? FolderNode(AssetFolderEntry folder, bool isPack)
    {
        var children = new List<AssetNode>();
        foreach (var sub in folder.Folders)
            if (FolderNode(sub, isPack: false) is { } child)
                children.Add(child);
        children.AddRange(folder.Files.Where(Matches).Select(FileNode));
        if (Filter.Length > 0 && children.Count == 0)
            return null;
        var node = new AssetNode
        {
            Name = folder.Name,
            FullPath = folder.FullPath,
            Location = folder.FullPath,
            IsFolder = true,
            IsPack = isPack,
            IsValid = true,
            FileCount = folder.Files.Count,
            Modified = folder.Modified,
        };
        foreach (var c in children)
            node.Children.Add(c);
        node.IsExpanded = Filter.Length > 0 || _expanded.Contains(folder.FullPath);
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AssetNode.IsExpanded) || Filter.Length > 0) return;
            if (node.IsExpanded) _expanded.Add(node.FullPath);
            else _expanded.Remove(node.FullPath);
        };
        return node;
    }

    private static AssetNode FileNode(AssetFileEntry file) => new()
    {
        Name = file.Name,
        FullPath = file.FullPath,
        Location = file.FullPath,
        IsValid = file.IsValid,
        IsActive = file.IsActive,
        Modified = file.Modified,
    };

    private static IEnumerable<AssetNode> Flatten(IEnumerable<AssetNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private List<ActiveItem> SelectedActive => ActiveList.SelectedItems?.OfType<ActiveItem>().ToList() ?? [];
    private List<AssetNode> SelectedNodes => AssetsTree.SelectedItems?.OfType<AssetNode>().ToList() ?? [];

    // ---------- Preview and its actions ----------

    private string? Hint(string fileName) => PreviewPanel.HintText(_type.HintFor(fileName));

    /// <summary>unezQuake skips spray images bigger than 128×128.</summary>
    private string? Warning(Preview preview) =>
        _type.Id == "sprays" && preview.Image is { } img && (img.PixelSize.Width > 128 || img.PixelSize.Height > 128)
            ? Loc.T("Larger than 128×128: unezQuake won't load this spray.")
            : null;

    private void OnActiveSelected()
    {
        if (_rebuilding) return;
        var items = SelectedActive;
        if (items.Count == 0)
            return;
        AssetsTree.SelectedItems?.Clear();
        var actions = ActiveActions(items);
        if (items.Count > 1)
        {
            Preview.Clear(Loc.F("{0} selected.", Loc.Files(items.Count)), actions);
            return;
        }
        var item = items[0];
        if (item.Entry.IsFolder)
        {
            Preview.Clear(Loc.F("{0} holds {1}. The game doesn't load subfolders.", item.Name, Loc.Files(item.Entry.FileCount)), actions);
            return;
        }
        var preview = Previews.FromFile(item.Entry.FullPath, Manager.GameData.Palette);
        Preview.Show(preview, modified: item.Entry.Modified, actions: actions,
            hint: item.IsInvalid ? null : Hint(item.Entry.Name), warning: Warning(preview));
    }

    private void OnAssetSelected()
    {
        if (_rebuilding) return;
        var nodes = SelectedNodes;
        if (nodes.Count == 0)
            return;
        ActiveList.SelectedItems?.Clear();
        var actions = AssetActions(nodes);
        if (nodes.Count > 1)
        {
            Preview.Clear(Loc.F("{0} selected.", Loc.Files(nodes.Count)), actions);
            return;
        }
        var node = nodes[0];
        if (node.IsFolder)
        {
            Preview.Clear($"{node.Name}: {Loc.Files(node.FileCount)}.", actions);
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
        Preview.Show(main, compare, node.Modified, actions, hint: node.IsValid ? Hint(node.Name) : null, warning: Warning(main));
    }

    private List<PreviewAction> ActiveActions(IReadOnlyList<ActiveItem> items)
    {
        if (items.Count > 1)
        {
            var several = new List<PreviewAction> { new(Loc.T("Move to Local assets…"), () => _ = MoveActiveToAssets(items)) };
            if (items.Any(i => i.IsInvalid && !i.Entry.IsFolder))
                several.Add(new(Loc.T("Mark as valid"), () => MarkValid(items.Where(i => i.IsInvalid && !i.Entry.IsFolder).Select(i => i.Entry.Name))));
            several.Add(new(Loc.T("Delete"), () => _ = DeleteActive(items), Danger: true));
            return several;
        }
        var item = items[0];
        if (item.Entry.IsFolder)
            return
            [
                new(Loc.T("Rename…"), () => _ = RenameActive(item)),
                new(Loc.T("Move to Local assets…"), () => _ = MoveActiveToAssets(items)),
                new(Loc.T("Delete"), () => _ = DeleteActive(items), Danger: true),
            ];
        if (item.IsInvalid)
            return
            [
                new(Loc.T("Rename…"), () => _ = RenameActive(item)),
                new(Loc.T("Mark as valid"), () => MarkValid([item.Entry.Name])),
                new(Loc.T("Move to Local assets…"), () => _ = MoveActiveToAssets(items)),
                new(Loc.T("Delete"), () => _ = DeleteActive(items), Danger: true),
            ];
        return
        [
            new(Loc.T("Rename…"), () => _ = RenameActive(item)),
            new(Loc.T("Show alternatives"), () => ShowAlternatives(item.Entry.Name)),
            new(Loc.T("Open folder"), () => _ = Shell.OpenFolder(Owner, Manager.ActiveFolder(_type))),
            new(Loc.T("Delete"), () => _ = DeleteActive(items), Danger: true),
        ];
    }

    private List<PreviewAction> AssetActions(IReadOnlyList<AssetNode> nodes)
    {
        var actions = new List<PreviewAction>();
        if (nodes.Count > 1)
        {
            var files = nodes.Where(n => !n.IsFolder).ToList();
            if (files.Any(f => f.IsValid))
                actions.Add(new(Loc.T("Use these files"), () => UseFiles(files.Where(f => f.IsValid).ToList())));
            actions.Add(new(Loc.T("Move to folder…"), () => _ = MoveWithinAssets(nodes)));
            if (files.Any(f => !f.IsValid))
                actions.Add(new(Loc.T("Mark as valid"), () => MarkValid(files.Where(f => !f.IsValid).Select(f => f.Name))));
            actions.Add(new(Loc.T("Delete"), () => _ = Delete(nodes), Danger: true));
            return actions;
        }
        var node = nodes[0];
        if (node.IsFolder)
        {
            actions.Add(new(Loc.T("Use these"), () => _ = UseFolder(node)));
            if (!node.IsPack)
                actions.Add(new(Loc.T("Move to the root"), () => MoveToRoot(node)));
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
        }
        else if (node.IsValid)
        {
            actions.Add(new(Loc.T("Use this file"), () => UseFiles([node])));
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
            actions.Add(new(Loc.T("Move to folder…"), () => _ = MoveWithinAssets(nodes)));
        }
        else
        {
            actions.Add(new(Loc.T("Rename…"), () => _ = Rename(node)));
            actions.Add(new(Loc.T("Mark as valid"), () => MarkValid([node.Name])));
            actions.Add(new(Loc.T("Copy to game folder"), () => CopyToGame(node)));
            actions.Add(new(Loc.T("Move to folder…"), () => _ = MoveWithinAssets(nodes)));
        }
        actions.Add(new(Loc.T("Delete"), () => _ = Delete(nodes), Danger: true));
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
        // Right-clicking outside the selection selects just that row.
        if (ActiveList.SelectedItems?.Contains(item) != true)
            ActiveList.SelectedItem = item;
        OpenMenu(ActiveActions(SelectedActive), ActiveList);
        e.Handled = true;
    }

    private void OnAssetsContext(object? sender, ContextRequestedEventArgs e)
    {
        var node = ItemFrom<AssetNode>(e);
        if (node is null) return;
        if (AssetsTree.SelectedItems?.Contains(node) != true)
            AssetsTree.SelectedItem = node;
        OpenMenu(AssetActions(SelectedNodes), AssetsTree);
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

    /// <summary>Fill the filter with the file's name, so the Local list shows every version of it.</summary>
    private void ShowAlternatives(string fileName)
    {
        SetOnline(false);
        FilterBox.Text = Path.GetFileNameWithoutExtension(fileName);
        _host.SetStatus(Loc.F("Showing every version of {0} in Local assets.", Path.GetFileNameWithoutExtension(fileName)));
    }

    /// <summary>Add the names to names/&lt;type&gt;.json so they count as valid everywhere.</summary>
    private void MarkValid(IEnumerable<string> fileNames)
    {
        var names = fileNames.Distinct().ToList();
        Run(() =>
        {
            var added = Manager.MarkValid(_type, names);
            var still = names.Where(n => !Manager.IsValid(_type, n)).ToList();
            var message = added.Count > 0
                ? Loc.F("Added {0} to {1}.", string.Join(", ", added), _type.NamesFile)
                : Loc.T("These names were already on the list.");
            if (still.Count > 0)
                message += " " + Loc.F("Still invalid: {0}. This asset type doesn't use these file extensions, or the files sit directly in the type's Local assets folder.", string.Join(", ", still));
            _host.SetStatus(message);
        });
    }

    private async Task RenameActive(ActiveItem item)
    {
        var current = item.Entry.Name;
        var isFolder = item.Entry.IsFolder;
        var suggestion = isFolder ? current
            : item.IsInvalid ? Manager.ValidNames[_type.Kind].SuggestName(current)
            : current.ToLowerInvariant();
        var name = await Dialogs.Rename(Owner, Loc.F("Rename {0}", current), current, suggestion, NameErr);
        if (name is null) return;
        var final = isFolder ? name : name.ToLowerInvariant();
        if (final == current) return;
        if (!isFolder && !Manager.IsValid(_type, final) &&
            !await Dialogs.Confirm(Owner, Loc.T("Rename"), Loc.F("The game never loads \"{0}\", so it won't use this file. Rename anyway?", final), Loc.T("Rename")))
            return;
        Run(() =>
        {
            Manager.RenameInGame(item.Entry.FullPath, final);
            _host.SetStatus(Loc.F("Renamed {0} to {1}.", current, final));
        });
    }

    /// <summary>Delete game files; for one file, optionally also its copies in Assets (same content).</summary>
    private async Task DeleteActive(IReadOnlyList<ActiveItem> items)
    {
        if (items.Count > 1)
        {
            if (!await Dialogs.Confirm(Owner, Loc.T("Delete"), Loc.F("Move {0} to the {1}?", Loc.Files(items.Count), TrashName), Loc.T("Delete"), danger: true))
                return;
            Run(() =>
            {
                foreach (var i in items)
                    Manager.Delete(i.Entry.FullPath);
                _host.SetStatus(Loc.F("Moved {0} to the {1}.", Loc.Files(items.Count), TrashName));
            });
            return;
        }
        var item = items[0];
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
                ? Loc.F("Moved {0} and {1} in Local assets to the {2}.", item.Name, Loc.Files(copies.Count), TrashName)
                : Loc.F("Moved {0} to the {1}.", item.Name, TrashName));
        });
    }

    private async Task MoveActiveToAssets(IReadOnlyList<ActiveItem> items)
    {
        var one = items.Count == 1 ? items[0] : null;
        var isFile = one is { Entry.IsFolder: false };
        var title = one is not null ? Loc.F("Move {0} to Local assets", one.Entry.Name) : Loc.F("Move {0} to Local assets", Loc.Files(items.Count));
        var choice = await Dialogs.ChooseDestination(Owner, title, Loc.T("Move"),
            Manager.PackFolderNames(_type),
            one is { Entry.IsFolder: true } ? one.Entry.Name : Manager.NewDatedFolderName(_type),
            isFile ? one!.Entry.Name : null,
            isFile ? Manager.ValidNames[_type.Kind].SuggestName(one!.Entry.Name) : null,
            NameErr);
        if (choice is not { } c) return;
        Run(() =>
        {
            foreach (var item in items)
                Manager.MoveActiveToAssets(_type, item.Entry.FullPath, c.Folder, items.Count == 1 ? c.FileName : null);
            _host.SetStatus(Loc.F("Moved {0} to {1}.", one?.Entry.Name ?? Loc.Files(items.Count), Path.Combine("assets", _type.FolderName, c.Folder)));
        });
    }

    private async Task MoveWithinAssets(IReadOnlyList<AssetNode> nodes)
    {
        var folders = Manager.PackFolderNames(_type);
        var current = Path.GetFileName(Path.GetDirectoryName(nodes[0].FullPath)) ?? "";
        var label = nodes.Count == 1 ? nodes[0].Name : Loc.Files(nodes.Count);
        var choice = await Dialogs.ChooseDestination(Owner, Loc.F("Move {0}", label), Loc.T("Move"),
            folders, folders.FirstOrDefault(f => f != current) ?? Manager.NewDatedFolderName(_type), null, null, NameErr);
        if (choice is not { } c) return;
        Run(() =>
        {
            foreach (var node in nodes)
                Manager.MoveWithinAssets(_type, node.FullPath, c.Folder);
            _host.SetStatus(Loc.F("Moved {0} to {1}.", label, c.Folder));
        });
    }

    private void MoveToRoot(AssetNode node) => Run(() =>
    {
        var dest = Manager.MoveFolderToRoot(_type, node.FullPath);
        _host.SetStatus(Loc.F("Moved {0} to {1}.", node.Name, Path.Combine("assets", _type.FolderName, Path.GetFileName(dest))));
    });

    private void CopyToGame(AssetNode node) => Run(() =>
    {
        var target = Manager.CopyToGame(_type, node.FullPath);
        _host.SetStatus(Loc.F("Copied {0} to the game folder. Rename it there to a name the game loads.", Path.GetFileName(target)));
    });

    private void UseFiles(IReadOnlyList<AssetNode> files) => Run(() =>
    {
        string? last = null;
        foreach (var f in files)
            last = Manager.UseFile(_type, f.FullPath);
        _host.SetStatus(files.Count == 1 && last is not null
            ? Loc.F("{0} is now in the game.", Path.GetFileName(last))
            : Loc.F("Copied {0} into the game.", Loc.Files(files.Count)));
    });

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

    private Task Delete(IReadOnlyList<AssetNode> nodes) =>
        nodes.Count == 1 ? Delete(nodes[0].FullPath, nodes[0].Name) : DeleteSeveral(nodes);

    private async Task DeleteSeveral(IReadOnlyList<AssetNode> nodes)
    {
        if (!await Dialogs.Confirm(Owner, Loc.T("Delete"), Loc.F("Move {0} to the {1}?", Loc.Files(nodes.Count), TrashName), Loc.T("Delete"), danger: true))
            return;
        Run(() =>
        {
            // A folder and a file inside it can both be selected: delete the outer one only.
            foreach (var n in nodes.Where(n => !nodes.Any(o => o != n && o.IsFolder && n.FullPath.StartsWith(o.FullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))))
                Manager.Delete(n.FullPath);
            _host.SetStatus(Loc.F("Moved {0} to the {1}.", Loc.Files(nodes.Count), TrashName));
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
        // Only the folder's own files are copied; nothing else in the game folder is touched.
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

    public void SnapshotSelectActive(int count)
    {
        if (ActiveList.ItemsSource is not IEnumerable<ActiveItem> items) return;
        ActiveList.SelectedItems?.Clear();
        foreach (var i in items.Where(i => !i.Entry.IsFolder).Take(count))
            ActiveList.SelectedItems?.Add(i);
    }

    public void SnapshotFilter(string text) => FilterBox.Text = text;

    public Task SnapshotMoveDialog() => MoveActiveToAssets(SelectedActive);

    public void SnapshotShowInvalid(bool on) => ShowInvalid.IsChecked = on;

    public void SnapshotOnline(bool on) => SetOnline(on);
#endif
}
