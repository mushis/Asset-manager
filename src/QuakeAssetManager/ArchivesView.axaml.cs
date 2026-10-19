using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Formats;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager;

/// <summary>Read-only browser for PAK/PK3 files. qw archives can be extracted to Assets; nothing is ever written to an archive.</summary>
public partial class ArchivesView : UserControl
{
    private MainWindow _host = null!;
    private GameArchive? _archive;
    private List<ArchiveItem> _items = [];
    private string? _selectedArchive;

    public ArchivesView()
    {
        InitializeComponent();
        ArchiveTree.SelectionChanged += async (_, _) => await OpenSelected();
        EntryList.SelectionChanged += (_, _) => OnEntrySelected();
        FilterBox.TextChanged += (_, _) => ApplyFilter();
        ExtractAll.Click += async (_, _) => await CopyToAssets(_items.Where(i => i.Lump is null).ToList());
        ExtractSelected.Click += async (_, _) => await CopyToAssets(Selected());
        EntryList.ContextRequested += OnEntryContext;
        DetachedFromVisualTree += (_, _) => CloseArchive();
    }

    private AssetManager Manager => _host.Manager!;

    public void Init(MainWindow host) => _host = host;

    public void Show()
    {
        RefreshTree();
        UpdateButtons();
    }

    public void RefreshTree()
    {
        if (_host.Manager is null) return;
        var groups = new ObservableCollection<ArchiveTreeNode>();
        ArchiveTreeNode? reselect = null;
        foreach (var (dir, archives) in Manager.ListArchives())
        {
            var group = new ArchiveTreeNode { Name = dir };
            foreach (var path in archives)
            {
                var node = new ArchiveTreeNode { Name = Path.GetFileName(path), ArchivePath = path };
                group.Children.Add(node);
                if (path == _selectedArchive) reselect = node;
            }
            groups.Add(group);
        }
        ArchiveTree.ItemsSource = groups;
        if (reselect is not null)
            ArchiveTree.SelectedItem = reselect;
    }

    private async Task OpenSelected()
    {
        if (ArchiveTree.SelectedItem is not ArchiveTreeNode { ArchivePath: { } path })
            return;
        if (path == _selectedArchive && _archive is not null)
            return;
        EntryList.SelectedItems?.Clear();
        EntryList.ItemsSource = null;
        CloseArchive();
        _selectedArchive = path;
        ArchiveHeader.Text = $"{Path.GetFileName(Path.GetDirectoryName(path))}{Path.DirectorySeparatorChar}{Path.GetFileName(path)} · " + Loc.T("reading…");
        Preview.Clear();
        try
        {
            var manager = Manager;
            var (archive, items) = await Task.Run(() =>
            {
                var a = GameArchive.Open(path);
                return (a, ListItems(a, manager));
            });
            if (_selectedArchive != path)
            {
                archive.Dispose();
                return;
            }
            _archive = archive;
            _items = items;
            ArchiveHeader.Text = $"{Path.GetFileName(Path.GetDirectoryName(path))}{Path.DirectorySeparatorChar}{Path.GetFileName(path)} · " + Loc.Files(items.Count);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ArchiveHeader.Text = Path.GetFileName(path);
            _items = [];
            _host.SetStatus(Loc.F("{0} couldn't be read:", Path.GetFileName(path)) + " " + e.Message);
        }
        ApplyFilter();
        UpdateButtons();
    }

    /// <summary>All entries, with WAD files expanded into their lumps, each labelled with where it would be copied.</summary>
    private static List<ArchiveItem> ListItems(GameArchive archive, AssetManager manager)
    {
        var items = new List<ArchiveItem>();
        var sep = Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var c = FileClassifier.Classify(entry.Path, manager, fromArchive: true);
            var destination = c.Type is { } t ? $"→ assets{sep}{t.FolderName}" + (t.Visible ? "" : $" ({Loc.T("hidden")})")
                : c.OtherPath is not null ? $"→ assets{sep}{Importer.Other}"
                : c.NeedsChoice ? Loc.T("you'll pick")
                : "";
            items.Add(new ArchiveItem { Archive = archive, Entry = entry, Destination = destination, Target = c.Type });
            if (entry.Extension == ".wad" && entry.Size < 32 * 1024 * 1024 &&
                WadFile.TryParse(archive.ReadEntry(entry), out var wad) && wad is not null)
            {
                foreach (var lump in wad.Lumps.OrderBy(l => l.Name, StringComparer.Ordinal))
                    items.Add(new ArchiveItem { Archive = archive, Entry = entry, Lump = lump, Wad = wad });
            }
        }
        return items;
    }

    private CancellationTokenSource? _thumbs;

    private void ApplyFilter()
    {
        var f = FilterBox.Text?.Trim() ?? "";
        var shown = f.Length == 0
            ? _items
            : _items.Where(i => i.DisplayPath.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        EntryList.ItemsSource = shown;
        _thumbs?.Cancel();
        _thumbs = new CancellationTokenSource();
        if (_host.Manager is not null)
            Thumbnails.Load(shown.Where(i => i.IsImage).Take(400), Manager.GameData.Palette, _thumbs.Token);
    }

    private void UpdateButtons()
    {
        var open = _archive is not null;
        ExtractAll.IsVisible = ExtractSelected.IsVisible = open;
        var textures = _items.Count(i => i.Target is not null);
        ExtractNote.Text = _archive is null
            ? Loc.T("Pick an archive on the left to see what's inside.")
            : textures > 0
                ? Loc.F("{0} files here belong to a known asset type and go to {1}. Files in folders the app doesn't manage go to {2}; for the rest you pick the type. Hold Ctrl or Shift to select several, then right-click.",
                    textures, Path.Combine("assets", "<type>", Path.GetFileNameWithoutExtension(_archive.FilePath)), Path.Combine("assets", Importer.Other))
                : Loc.F("No file here belongs to a known asset type. Files in folders the app doesn't manage go to {0}; for the rest you pick the type.", Path.Combine("assets", Importer.Other));
        ExtractAll.IsEnabled = _items.Count > 0;
    }

    private void OnEntrySelected()
    {
        if (_archive is null || EntryList.SelectedItems?.OfType<ArchiveItem>().LastOrDefault() is not { } item ||
            item.Archive != _archive)
            return;
        try
        {
            if (item.Lump is not null && item.Wad is not null)
                Preview.Show(Previews.FromBytes(item.Lump.Name, item.Wad.ReadLump(item.Lump), Manager.GameData.Palette, ".wadlump", item.Lump.Type));
            else if (item.Entry.Size > 64 * 1024 * 1024)
                Preview.Show(new Preview(null, $"{item.Name} · {item.SizeText}", Loc.T("Too big to preview.")));
            else
                Preview.Show(Previews.FromBytes(item.Name, _archive.ReadEntry(item.Entry), Manager.GameData.Palette));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or KeyNotFoundException)
        {
            Preview.Clear(Loc.F("{0} couldn't be read:", item.Name) + " " + e.Message);
        }
    }

    private List<ArchiveItem> Selected() =>
        EntryList.SelectedItems?.OfType<ArchiveItem>().Where(i => i.Archive == _archive && i.Lump is null).ToList() ?? [];

    /// <summary>
    /// Selected archive files grouped by the asset type of the folder they sit in (shown or hidden types).
    /// The archive's folder decides; files in folders the app doesn't manage go to assets/other, and the rest are asked about.
    /// </summary>
    private (List<IGrouping<AssetType, ArchiveItem>> ByType, List<ArchiveItem> Other, int Unsure) Sorted(IEnumerable<ArchiveItem> items)
    {
        var classified = items.Select(i => (Item: i, Class: FileClassifier.Classify(i.Entry.Path, Manager, fromArchive: true))).ToList();
        var byType = classified.Where(x => x.Class.Type is not null)
            .GroupBy(x => x.Class.Type!, x => x.Item)
            .OrderByDescending(g => g.Count())
            .ToList();
        var other = classified.Where(x => x.Class.Type is null && x.Class.OtherPath is not null).Select(x => x.Item).ToList();
        return (byType, other, classified.Count(x => x.Class.NeedsChoice));
    }

    private static string TypeLabel(AssetType t) => t.Visible ? t.DisplayName : $"{t.DisplayName}, {Loc.T("hidden")}";

    /// <summary>Right-click: copy the selected files to their asset type folders (same summary and Revert as a drop).</summary>
    private void OnEntryContext(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (_archive is null) return;
        var clicked = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ArchiveItem;
        if (clicked is not null && EntryList.SelectedItems?.Contains(clicked) != true)
            EntryList.SelectedItem = clicked;
        var selected = Selected();
        if (selected.Count == 0) return;

        var menu = new ContextMenu();
        var folder = Importer.ArchiveBaseName(_archive.FilePath);
        var (groups, other, unsure) = Sorted(selected);
        var sep = System.IO.Path.DirectorySeparatorChar;
        var parts = groups.Select(g => $"{TypeLabel(g.Key)}: {g.Count()}").ToList();
        if (other.Count > 0) parts.Add($"{Loc.T("other")}: {other.Count}");
        if (unsure > 0) parts.Add(Loc.F("to pick: {0}", unsure));
        var kinds = groups.Count + (other.Count > 0 ? 1 : 0) + (unsure > 0 ? 1 : 0);

        if (kinds == 1 && groups.Count == 1)
            menu.Items.Add(Item(Loc.F("Copy {0} to Local assets", Count(selected.Count)) + $" → assets{sep}{groups[0].Key.FolderName}{sep}{folder} ({TypeLabel(groups[0].Key)})", () => _ = CopyToAssets(selected)));
        else if (kinds == 1 && other.Count > 0)
            menu.Items.Add(Item(Loc.F("Copy {0} to Local assets", Count(selected.Count)) + $" → assets{sep}{Importer.Other} ({Loc.T("not managed by the app")})", () => _ = CopyToAssets(selected)));
        else if (kinds == 1)
            menu.Items.Add(Item(Loc.F("Copy {0} to Local assets", Count(selected.Count)) + " (" + Loc.T("you'll pick the asset type") + ")", () => _ = CopyToAssets(selected)));
        else
        {
            menu.Items.Add(Item(Loc.F("Copy to their asset folders ({0})", string.Join(", ", parts)), () => _ = CopyToAssets(selected)));
            menu.Items.Add(new Separator());
            foreach (var g in groups)
                menu.Items.Add(Item(Loc.F("Copy only {0} ({1})", TypeLabel(g.Key), g.Count()) + $" → assets{sep}{g.Key.FolderName}{sep}{folder}", () => _ = CopyToAssets(g.ToList())));
            if (other.Count > 0)
                menu.Items.Add(Item(Loc.F("Copy only {0} ({1})", Loc.T("files the app doesn't manage"), other.Count) + $" → assets{sep}{Importer.Other}", () => _ = CopyToAssets(other)));
        }
        menu.Open(EntryList);

        static string Count(int n) => Loc.Files(n);
        static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
    }

    /// <summary>Copy archive files to Assets through the import summary (OK or Revert). Any archive can be copied from.</summary>
    private async Task CopyToAssets(List<ArchiveItem> items)
    {
        if (_archive is null) return;
        var owner = (Window)TopLevel.GetTopLevel(this)!;
        if (items.Count == 0)
        {
            await Dialogs.Message(owner, Loc.T("Copy to Local assets"), Loc.T("Select the files to copy first."));
            return;
        }
        var archive = _archive;
        var files = items.Select(i => new SourceFile(i.Entry.Path, () => archive.ReadEntry(i.Entry), InArchive: true)).ToList();
        await _host.ImportWithSummary(m => new Importer(m).Prepare(archive.Name, Importer.ArchiveBaseName(archive.FilePath), files));
    }
    private void CloseArchive()
    {
        _thumbs?.Cancel();
        _archive?.Dispose();
        _archive = null;
        _items = [];
    }

#if DEBUG
    public async Task SnapshotOpen(string dir, string archive, string? select = null)
    {
        if (ArchiveTree.ItemsSource is not IEnumerable<ArchiveTreeNode> groups) return;
        var node = groups.FirstOrDefault(g => g.Name == dir)?.Children.FirstOrDefault(c => c.Name == archive);
        if (node is null) return;
        ArchiveTree.SelectedItem = node;
        for (var i = 0; i < 50 && _archive?.Name != archive; i++)
            await Task.Delay(100);
        var item = select is null
            ? _items.FirstOrDefault(i => i.Type is not null && i.Entry.Extension == ".png")
            : _items.FirstOrDefault(i => i.DisplayPath == select);
        if (item is not null)
        {
            EntryList.SelectedItem = item;
            EntryList.ScrollIntoView(item);
        }
    }
#endif
}

