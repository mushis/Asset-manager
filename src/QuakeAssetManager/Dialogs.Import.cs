using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager;

/// <summary>Dialogs added in 0.4: where unclear files go, subfolders found at scan time, and Delete.</summary>
public static partial class Dialogs
{
    /// <summary>An entry in a type dropdown: an asset type, or Skip (null).</summary>
    private sealed record TypeChoice(AssetType? Type)
    {
        public override string ToString() => Type is null
            ? Loc.T("Skip")
            : $"{Type.DisplayName} ({Type.ActiveRelativePath}){(Type.Visible ? "" : " · " + Loc.T("hidden"))}";
    }

    /// <summary>
    /// "Where should these files go?": one row per folder that no rule could place, with a type dropdown.
    /// The type the file names point to is pre-selected; otherwise Skip. Returns null when cancelled.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, AssetType?>?> WhereShouldTheseGo(Window owner, string title, IReadOnlyList<ImportQuestion> questions)
    {
        var w = NewWindow(Loc.T("Where should these files go?"), 680);
        var choices = new List<TypeChoice> { new(null) };
        choices.AddRange(AssetType.Known.OrderBy(t => t.Visible ? 0 : 1).Select(t => new TypeChoice(t)));
        Dictionary<string, AssetType?>? result = null;

        var content = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        content.Children.Add(new TextBlock { Text = Loc.T("Where should these files go?"), FontSize = 16, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = Loc.F("{0} has files in folders the app can't place by itself. Pick an asset type for each folder, or Skip.", title),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        });

        var rows = new StackPanel { Spacing = 6 };
        var boxes = new List<(ImportQuestion Question, ComboBox Box)>();
        foreach (var q in questions)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,260") };
            var label = new StackPanel { Spacing = 2 };
            label.Children.Add(new TextBlock
            {
                Text = (q.Folder.Length == 0 ? Loc.T("(archive root)") : q.Folder + "/") + "  · " + Loc.Files(q.FileNames.Count),
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            label.Children.Add(new TextBlock
            {
                Text = string.Join(", ", q.FileNames.Take(6)) + (q.FileNames.Count > 6 ? ", …" : ""),
                Classes = { "muted" },
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (q.Suggested is not null)
                label.Children.Add(new TextBlock { Text = Loc.T("suggested from the file names"), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#30A46C")) });
            var box = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            box.SelectedItem = choices.First(c => c.Type?.Id == q.Suggested?.Id);
            Grid.SetColumn(box, 1);
            grid.Children.Add(label);
            grid.Children.Add(box);
            rows.Children.Add(new Border
            {
                Child = grid,
                Padding = new Thickness(0, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.Parse("#30808080")),
            });
            boxes.Add((q, box));
        }
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 380 });

        var ok = new Button { Content = Loc.T("Continue"), IsDefault = true };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = boxes.ToDictionary(b => b.Question.Key, b => (b.Box.SelectedItem as TypeChoice)?.Type);
            w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        content.Children.Add(Footer(Loc.F("Enter: {0}", Loc.T("Continue")) + " · " + Loc.T("Esc: cancel"), cancel, ok));
        w.Content = content;
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
        return result;
    }

    private sealed class SubfolderRow(FoundSubfolder folder) : Observable
    {
        private bool _isChecked = true;
        public FoundSubfolder Folder { get; } = folder;
        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    }

    /// <summary>
    /// Subfolders found in the game folders at scan time. Returns the ticked folders, whether to delete the originals
    /// (Copy and delete), and whether to stop asking about the unticked ones; null when skipped.
    /// </summary>
    public static async Task<(IReadOnlyList<FoundSubfolder> Selected, bool DeleteOriginals, bool DontAskOthers)?> ChooseSubfolders(
        Window owner, IReadOnlyList<FoundSubfolder> found)
    {
        var w = NewWindow(Loc.T("Folders with alternative assets were found"), 620);
        (IReadOnlyList<FoundSubfolder>, bool, bool)? result = null;
        var rows = found.Select(f => new SubfolderRow(f)).ToList();
        var sep = System.IO.Path.DirectorySeparatorChar;

        var content = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        content.Children.Add(new TextBlock { Text = Loc.T("Folders with alternative assets were found"), FontSize = 16, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("The game doesn't load subfolders, so these are usually alternative versions. Tick the ones to copy to Assets; each keeps its name."),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        });
        var list = new StackPanel { Spacing = 2 };
        foreach (var group in rows.GroupBy(r => r.Folder.Type))
        {
            list.Children.Add(new TextBlock { Text = $"{group.Key.DisplayName}  ({group.Key.ActiveRelativePath})", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var row in group)
            {
                var check = new CheckBox { IsChecked = true, Margin = new Thickness(12, 0, 0, 0) };
                check.IsCheckedChanged += (_, _) => row.IsChecked = check.IsChecked == true;
                check.Content = new TextBlock
                {
                    Text = $"{row.Folder.Name}   {Loc.Files(row.Folder.FileCount)}   → assets{sep}{row.Folder.Type.FolderName}{sep}{row.Folder.Name}",
                };
                list.Children.Add(check);
            }
        }
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320 });
        var dontAsk = new CheckBox { Content = Loc.T("Don't ask again about the folders I leave unticked") };
        content.Children.Add(dontAsk);

        var copy = new Button { Content = Loc.T("Copy"), IsDefault = true };
        var copyDelete = new Button { Content = Loc.T("Copy and delete") };
        ToolTip.SetTip(copyDelete, Loc.T("Copy, then move the original folders from your Quake folder to the Recycle Bin"));
        var skip = new Button { Content = Loc.T("Skip"), IsCancel = true };
        void Finish(bool delete)
        {
            result = (rows.Where(r => r.IsChecked).Select(r => r.Folder).ToList(), delete, dontAsk.IsChecked == true);
            w.Close();
        }
        copy.Click += (_, _) => Finish(false);
        copyDelete.Click += (_, _) => Finish(true);
        skip.Click += (_, _) => w.Close();
        content.Children.Add(Footer(Loc.F("Enter: {0}", Loc.T("Copy")) + " · " + Loc.F("Esc: {0}", Loc.T("Skip")), skip, copyDelete, copy));
        w.Content = content;
        w.Opened += (_, _) => copy.Focus();
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Delete confirmation for a game file. When copies with the same content are in Assets, a tickbox offers to
    /// delete them too. Returns (confirmed, also delete copies).
    /// </summary>
    public static async Task<(bool Confirmed, bool AlsoCopies)> ConfirmDelete(Window owner, string message, IReadOnlyList<string> copiesInAssets, string assetsRoot)
    {
        var w = NewWindow(Loc.T("Delete"), 480);
        var ok = new Button { Content = Loc.T("Delete"), IsDefault = true };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        var confirmed = false;
        CheckBox? also = null;
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (copiesInAssets.Count > 0)
        {
            var folders = copiesInAssets.Select(c => System.IO.Path.GetRelativePath(assetsRoot, System.IO.Path.GetDirectoryName(c)!)).Distinct();
            also = new CheckBox
            {
                Content = new TextBlock { Text = Loc.F("Also delete the copies in Assets ({0})", string.Join(", ", folders)), TextWrapping = TextWrapping.Wrap },
            };
            content.Children.Add(also);
        }
        else
        {
            content.Children.Add(new TextBlock { Text = Loc.T("There's no copy of this file in Assets: this is the only copy."), Foreground = new SolidColorBrush(Color.Parse("#E8A33D")), TextWrapping = TextWrapping.Wrap });
        }
        ok.Click += (_, _) => { confirmed = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        content.Children.Add(Footer(KeyHint(Loc.T("Delete")), cancel, ok));
        w.Content = content;
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
        return (confirmed, also?.IsChecked == true);
    }
}
