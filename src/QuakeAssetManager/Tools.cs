using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using QuakeAssetManager.Core;

namespace QuakeAssetManager;

/// <summary>Settings &gt; Tools: Convert to lower case and Find duplicates.</summary>
public static class Tools
{
    private sealed class CaseRow(CaseFix fix) : Observable
    {
        private bool _isChecked = true;
        public CaseFix Fix { get; } = fix;
        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    }

    /// <summary>Text on top, then the list filling the window (so it scrolls), then the buttons.</summary>
    private static Grid ToolLayout(Control intro, Control? options, Control list, Control footer)
    {
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        Grid.SetRow(intro, 0);
        grid.Children.Add(intro);
        if (options is not null)
        {
            Grid.SetRow(options, 1);
            grid.Children.Add(options);
        }
        Grid.SetRow(list, 2);
        grid.Children.Add(list);
        Grid.SetRow(footer, 3);
        grid.Children.Add(footer);
        return grid;
    }

    /// <summary>List the files with upper-case letters, let the user untick some, then rename the rest.</summary>
    public static async Task ConvertToLowerCase(MainWindow host)
    {
        if (host.Manager is not { } manager) return;
        host.SetStatus(Loc.T("Looking for upper-case file names…"));
        IReadOnlyList<CaseFix> found;
        try
        {
            found = await Task.Run(manager.FindUpperCaseNames);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(host, Loc.T("Convert to lower case"), e.Message);
            return;
        }
        host.SetStatus("");
        if (found.Count == 0)
        {
            await Dialogs.Message(host, Loc.T("Convert to lower case"), Loc.T("Every file name is already lower case."));
            return;
        }

        var rows = found.Select(f => new CaseRow(f)).ToList();
        var w = Dialogs.ToolWindow(Loc.T("Convert to lower case"), 840, 600);
        var intro = new TextBlock
        {
            Text = Loc.F("{0} have upper-case letters. Tick the ones to rename; a file is skipped if the lower-case name is taken.", Loc.Files(found.Count)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        // "All" ticks or unticks every row; it shows a dash while only some are ticked.
        var updating = false;
        var master = new CheckBox { Content = Loc.T("All"), IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
        var list = new ListBox
        {
            ItemsSource = rows,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<CaseRow>((row, _) => row is null ? new Panel() : RowView(row), supportsRecycling: false),
        };
        master.IsCheckedChanged += (_, _) =>
        {
            if (updating) return;
            var value = master.IsChecked != false;
            updating = true;
            foreach (var r in rows) r.IsChecked = value;
            master.IsChecked = value;
            updating = false;
        };
        var convert = new Button { Content = Loc.T("Convert"), IsDefault = true };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        var ok = false;
        convert.Click += (_, _) => { ok = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();

        var footer = Dialogs.ToolFooter(Loc.F("Enter: {0}", Loc.T("Convert").ToLower(CultureInfo.CurrentCulture)) + " · " + Loc.T("Esc: cancel"), cancel, convert);
        w.Content = ToolLayout(intro, master, list, footer);
        w.Opened += (_, _) => convert.Focus();
        await w.ShowDialog(host);
        if (!ok) return;

        var ticked = rows.Where(r => r.IsChecked).Select(r => r.Fix).ToList();
        if (ticked.Count == 0) return;
        try
        {
            var (renamed, skipped) = await Task.Run(() => manager.ConvertToLowerCase(ticked));
            host.SetStatus(Loc.F("Renamed {0} to lower case.", Loc.Files(renamed)));
            if (skipped.Count > 0)
                await Dialogs.Message(host, Loc.T("Convert to lower case"),
                    Loc.F("Renamed {0}. Skipped {1}, because a file with the lower-case name already exists:", Loc.Files(renamed), skipped.Count) + "\n" +
                    string.Join("\n", skipped.Take(30).Select(s => "• " + Path.Combine(s.Folder, s.Name))) + (skipped.Count > 30 ? "\n…" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(host, Loc.T("Convert to lower case"), Loc.T("Some files couldn't be renamed:") + " " + e.Message);
        }
        host.RefreshCurrent();

        Control RowView(CaseRow row)
        {
            var check = new CheckBox { IsChecked = row.IsChecked, VerticalAlignment = VerticalAlignment.Center };
            // Follow "All", and update "All" when a single row changes.
            row.PropertyChanged += (_, _) => check.IsChecked = row.IsChecked;
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                if (updating) return;
                updating = true;
                var ticked = rows.Count(r => r.IsChecked);
                master.IsChecked = ticked == rows.Count ? true : ticked == 0 ? false : null;
                updating = false;
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
            // Where the file is: the Quake folder (the game uses it) or the Assets folder (a stored option).
            var where = new Border
            {
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = Themes.Brush(row.Fix.InAssets ? "ThemeInfoBrush" : "ThemeAccentBrush"),
                Child = new TextBlock
                {
                    Text = row.Fix.InAssets ? Loc.T("Local assets") : Loc.T("Quake folder"),
                    FontSize = 11,
                    Foreground = Themes.Brush(row.Fix.InAssets ? "ThemeInfoBrush" : "ThemeAccentBrush"),
                },
            };
            ToolTip.SetTip(where, row.Fix.InAssets
                ? Loc.T("In your Local assets folder")
                : Loc.T("In your Quake folder: the game loads it from here"));
            var names = new TextBlock { Text = $"{row.Fix.Name}  →  {row.Fix.NewName}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var folder = new TextBlock { Text = row.Fix.Folder, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
            ToolTip.SetTip(folder, row.Fix.FullPath);
            Grid.SetColumn(where, 1);
            Grid.SetColumn(names, 2);
            Grid.SetColumn(folder, 3);
            grid.Children.Add(check);
            grid.Children.Add(where);
            grid.Children.Add(names);
            grid.Children.Add(folder);
            return grid;
        }
    }

    private static string DuplicatesTitle => Loc.T("Find duplicates in the active Quake folder");

    /// <summary>Images and Quake palette files can be previewed; models (.mdl) show their first skin.</summary>
    private static bool CanPreview(string fileName) =>
        Core.Imaging.ImageDecoders.CanPreview(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>List the paths that exist in several places in the Quake folder, with Preview and Export….</summary>
    public static async Task FindDuplicates(MainWindow host)
    {
        if (host.Manager is not { } manager) return;
        host.SetStatus(Loc.T("Looking for duplicates in the Quake folder…"));
        IReadOnlyList<DuplicateGroup> groups;
        try
        {
            groups = await Task.Run(() => manager.FindDuplicates());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(host, DuplicatesTitle, e.Message);
            return;
        }
        host.SetStatus("");
        if (groups.Count == 0)
        {
            await Dialogs.Message(host, DuplicatesTitle, Loc.T("No duplicates: every path in the game folders exists only once."));
            return;
        }

        var w = Dialogs.ToolWindow(DuplicatesTitle, 820, 640);
        var identical = groups.Count(g => g.Identical);
        var intro = new TextBlock
        {
            Text = Loc.F("{0} paths exist in more than one place ({1} with identical contents). The game uses only one of each.", groups.Count, identical)
                   + " " + Loc.T("The one marked ✓ is the copy the game loads: qw before ezquake before id1, and inside a folder a loose file before a pk3 or pak."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var palette = manager.GameData.Palette;
        var list = new ListBox
        {
            ItemsSource = groups,
            ItemTemplate = new FuncDataTemplate<DuplicateGroup>((g, _) => g is null ? new Panel() : GroupView(g), supportsRecycling: false),
        };
        var export = new Button { Content = Loc.T("Export…") };
        ToolTip.SetTip(export, Loc.T("Save this list as a text file"));
        var close = new Button { Content = Loc.T("Close"), IsDefault = true, IsCancel = true };
        export.Click += async (_, _) => await Export(w, manager.Settings.QuakeFolder, groups);
        close.Click += (_, _) => w.Close();
        var footer = Dialogs.ToolFooter(Loc.T("Enter or Esc: close"), export, close);
        w.Content = ToolLayout(intro, null, list, footer);
        w.Opened += (_, _) => close.Focus();
        await w.ShowDialog(host);

        Control GroupView(DuplicateGroup g)
        {
            var panel = new StackPanel { Spacing = 1 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new TextBlock { Text = g.Path, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock
            {
                Text = g.Identical ? Loc.T("identical") : Loc.T("different"),
                Classes = { g.Identical ? "ok" : "warn" },
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (CanPreview(g.FileName))
            {
                var preview = new Button { Content = Loc.T("Preview"), Padding = new Thickness(8, 1), FontSize = 12 };
                ToolTip.SetTip(preview, Loc.T("Show every copy side by side"));
                preview.Click += async (_, _) => await ShowCopies(w, g.Path,
                    g.Locations.Select(l => (l.Display, (Func<byte[]>)(() => AssetManager.ReadDuplicate(l)), l.InUse)).ToList(), palette);
                header.Children.Add(preview);
            }
            panel.Children.Add(header);
            foreach (var l in g.Locations)
            {
                var line = new TextBlock { Text = $"    {(l.InUse ? "✓ " : "   ")}{l.Display}   {Previews.Size(l.Size)}" + (l.InUse ? "   · " + Loc.T("used by the game") : "") };
                line.Classes.Add(l.InUse ? "ok" : "muted");
                panel.Children.Add(line);
            }
            return panel;
        }
    }

    /// <summary>
    /// Every copy of a file next to each other, with where it is and whether the game uses it.
    /// Each item's bytes are read only when the window opens.
    /// </summary>
    private static async Task ShowCopies(Window owner, string title, IReadOnlyList<(string Label, Func<byte[]> Read, bool InUse)> copies,
                                         Core.Imaging.QuakePalette? palette)
    {
        var w = Dialogs.ToolWindow(title, 860, 560);
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, read, inUse) in copies)
        {
            Preview p;
            try { p = Previews.FromBytes(Path.GetFileName(title), read(), palette); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { p = new Preview(null, e.Message); }
            var card = new StackPanel { Width = 250, Spacing = 4, Margin = new Thickness(0, 0, 12, 12) };
            card.Children.Add(new Border
            {
                Classes = { "frame" },
                Height = 180,
                BorderThickness = new Thickness(inUse ? 2 : 0),
                BorderBrush = Themes.Brush("ThemeInGameBrush"),
                Child = p.Image is null
                    ? new TextBlock { Text = p.Text ?? Loc.T("No preview for this file type."), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }
                    : new Image { Source = p.Image, Stretch = Stretch.Uniform, Margin = new Thickness(4) },
            });
            RenderOptions.SetBitmapInterpolationMode(card, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            card.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            card.Children.Add(new TextBlock { Text = p.Info, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            if (inUse)
                card.Children.Add(new TextBlock { Text = "✓ " + Loc.T("used by the game"), Classes = { "ok" } });
            wrap.Children.Add(card);
        }
        var close = new Button { Content = Loc.T("Close"), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => w.Close();
        var intro = new TextBlock { Text = Loc.F("{0} copies of {1}.", copies.Count, title), Margin = new Thickness(0, 0, 0, 8) };
        w.Content = ToolLayout(intro, null, new ScrollViewer { Content = wrap }, Dialogs.ToolFooter(Loc.T("Enter or Esc: close"), close));
        w.Opened += (_, _) => close.Focus();
        await w.ShowDialog(owner);
    }

    /// <summary>A readable report: one block per path, with its locations indented and the used one marked.</summary>
    public static string Report(string quakeFolder, IReadOnlyList<DuplicateGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.F("Duplicates in {0}", quakeFolder));
        sb.AppendLine(DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture));
        sb.AppendLine(Loc.F("{0} paths exist in more than one place ({1} with identical contents). The game uses only one of each.", groups.Count, groups.Count(g => g.Identical)));
        foreach (var g in groups)
        {
            sb.AppendLine();
            sb.AppendLine($"{g.Path}  ({(g.Identical ? Loc.T("identical") : Loc.T("different"))})");
            foreach (var l in g.Locations)
                sb.AppendLine($"  {(l.InUse ? "*" : " ")} {l.Display}  {Previews.Size(l.Size)}" + (l.InUse ? "  (" + Loc.T("used by the game") + ")" : ""));
        }
        return sb.ToString();
    }

    private static async Task Export(Window owner, string quakeFolder, IReadOnlyList<DuplicateGroup> groups)
    {
        // Start in Downloads (or the home folder when there's none).
        var start = await owner.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads)
                    ?? await owner.StorageProvider.TryGetFolderFromPathAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Save the list of duplicates"),
            SuggestedFileName = "quake-duplicates.txt",
            SuggestedStartLocation = start,
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType(Loc.T("Text file")) { Patterns = ["*.txt"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(Report(quakeFolder, groups));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(owner, DuplicatesTitle, Loc.T("The list couldn't be saved:") + " " + e.Message);
        }
    }

    // ---------- Find duplicates in the Local Assets folders ----------

    private sealed class CopyRow(AssetCopy copy) : Observable
    {
        private bool _keep;
        public AssetCopy Copy { get; } = copy;
        public bool Keep { get => _keep; set => Set(ref _keep, value); }
    }

    private static string AssetCopiesTitle => Loc.T("Find duplicates in the Local assets folders");

    /// <summary>
    /// Exact copies in the Assets folder: tick the ones to keep (the oldest is ticked), then Delete the rest.
    /// A second dialog lists exactly what will be moved to the Recycle Bin before anything happens.
    /// </summary>
    public static async Task FindAssetCopies(MainWindow host)
    {
        if (host.Manager is not { } manager) return;
        host.SetStatus(Loc.T("Looking for copies in the Local assets folders…"));
        IReadOnlyList<AssetCopyGroup> groups;
        try
        {
            groups = await Task.Run(() => manager.FindAssetCopies());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Message(host, AssetCopiesTitle, e.Message);
            return;
        }
        host.SetStatus("");
        if (groups.Count == 0)
        {
            await Dialogs.Message(host, AssetCopiesTitle, Loc.T("No copies: every file in your Local assets folders is there only once."));
            return;
        }

        var rows = groups.ToDictionary(g => g, g => g.Copies.Select((c, i) => new CopyRow(c) { Keep = i == 0 }).ToList());
        var extra = groups.Sum(g => g.Copies.Count - 1);
        var w = Dialogs.ToolWindow(AssetCopiesTitle, 860, 660);
        var intro = new TextBlock
        {
            Text = Loc.F("{0} files are stored more than once ({1} extra copies). Copies have the same name, size and contents. Tick the copies to keep (the oldest is ticked); Delete moves the others to the {2}. At least one copy of each file is always kept.",
                groups.Count, extra, TrashName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var palette = manager.GameData.Palette;
        var list = new ListBox
        {
            ItemsSource = groups,
            ItemTemplate = new FuncDataTemplate<AssetCopyGroup>((g, _) => g is null ? new Panel() : GroupView(g), supportsRecycling: false),
        };
        var delete = new Button { Content = Loc.T("Delete…"), Classes = { "danger" } };
        ToolTip.SetTip(delete, Loc.T("Shows the list of files to delete and asks you to confirm"));
        var close = new Button { Content = Loc.T("Close"), IsCancel = true };
        close.Click += (_, _) => w.Close();
        delete.Click += async (_, _) =>
        {
            var toDelete = rows.SelectMany(kv => kv.Value.Where(r => !r.Keep)).Select(r => r.Copy).ToList();
            if (toDelete.Count == 0)
            {
                await Dialogs.Message(w, AssetCopiesTitle, Loc.T("Every copy is ticked to keep, so there's nothing to delete."));
                return;
            }
            if (!await ConfirmDeleteCopies(w, toDelete, manager.Settings.AssetsFolder))
                return;
            try
            {
                var n = await Task.Run(() => manager.DeleteAssetCopies(toDelete.Select(c => c.FullPath)));
                host.SetStatus(Loc.F("Moved {0} to the {1}.", Loc.Files(n), TrashName));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Dialogs.Message(w, AssetCopiesTitle, Loc.T("Some files couldn't be deleted:") + " " + e.Message);
            }
            w.Close();
            host.RefreshCurrent();
        };
        var footer = Dialogs.ToolFooter(Loc.T("Esc: close"), close, delete);
        w.Content = ToolLayout(intro, null, list, footer);
        w.Opened += (_, _) => close.Focus();
        await w.ShowDialog(host);

        Control GroupView(AssetCopyGroup g)
        {
            var copyRows = rows[g];
            var panel = new StackPanel { Spacing = 2 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = $"{Previews.Size(g.Size)} · " + Loc.F("{0} copies", g.Copies.Count), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
            if (CanPreview(g.Name))
            {
                var preview = new Button { Content = Loc.T("Preview"), Padding = new Thickness(8, 1), FontSize = 12 };
                preview.Click += async (_, _) => await ShowCopies(w, g.Name,
                    g.Copies.Select(c => (c.Folder, (Func<byte[]>)(() => File.ReadAllBytes(c.FullPath)), false)).ToList(), palette);
                header.Children.Add(preview);
            }
            panel.Children.Add(header);
            var boxes = new List<CheckBox>();
            foreach (var r in copyRows)
            {
                var keep = new CheckBox { IsChecked = r.Keep, Margin = new Thickness(12, 0, 0, 0) };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var path = new TextBlock { Text = Path.Combine("assets", r.Copy.Folder, g.Name), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(path, r.Copy.FullPath);
                var date = new TextBlock { Text = Loc.F("Modified {0}", RelativeDate.Full(r.Copy.Modified)), Classes = { "muted" }, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(date, 1);
                grid.Children.Add(path);
                grid.Children.Add(date);
                keep.Content = grid;
                ToolTip.SetTip(keep, Loc.T("Ticked: keep this copy. Unticked: delete it."));
                keep.IsCheckedChanged += (_, _) =>
                {
                    // One copy must stay: unticking the last kept one is undone.
                    if (keep.IsChecked != true && copyRows.Count(x => x.Keep) == 1 && r.Keep)
                    {
                        keep.IsChecked = true;
                        return;
                    }
                    r.Keep = keep.IsChecked == true;
                };
                boxes.Add(keep);
                panel.Children.Add(keep);
            }
            return panel;
        }
    }

    private static string TrashName => OperatingSystem.IsWindows() ? Loc.T("Recycle Bin") : Loc.T("trash");

    /// <summary>The last step before deleting: every file that will go to the Recycle Bin, and the copies that stay.</summary>
    private static async Task<bool> ConfirmDeleteCopies(Window owner, IReadOnlyList<AssetCopy> toDelete, string assetsRoot)
    {
        var w = Dialogs.ToolWindow(Loc.T("Delete these copies?"), 760, 520);
        var intro = new TextBlock
        {
            Text = Loc.F("These {0} will be moved to the {1}. The copies you ticked stay in Local assets. You can restore deleted files from the {1}.", Loc.Files(toDelete.Count), TrashName),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var text = new SelectableTextBlock
        {
            Text = string.Join("\n", toDelete.Select(c => Path.Combine("assets", c.Folder, Path.GetFileName(c.FullPath)))),
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
        };
        var ok = new Button { Content = Loc.F("Delete {0}", Loc.Files(toDelete.Count)), Classes = { "danger" } };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, IsDefault = true };
        var result = false;
        ok.Click += (_, _) => { result = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        w.Content = ToolLayout(intro, null, new ScrollViewer { Content = text }, Dialogs.ToolFooter(Loc.T("Enter or Esc: cancel"), cancel, ok));
        w.Opened += (_, _) => cancel.Focus();
        await w.ShowDialog(owner);
        return result;
    }
}