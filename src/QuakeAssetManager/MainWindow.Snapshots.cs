#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;

namespace QuakeAssetManager;

/// <summary>
/// Debug-only: with QAM_SNAPSHOT=&lt;folder&gt; the window walks through its screens, saves a PNG of each and closes.
/// Used to check the layout without clicking through by hand.
/// </summary>
public partial class MainWindow
{
    public async Task RunSnapshots(string folder)
    {
        Directory.CreateDirectory(folder);
        // The subfolder dialog can open on its own after the scan: capture it and press Skip.
        Dialogs.DebugDialogOpened = w => Dispatcher.UIThread.Post(async () =>
        {
            foreach (var expander in w.GetLogicalDescendants().OfType<Expander>())
                expander.IsExpanded = true;
            await Task.Delay(900);
            using (var bmp = new RenderTargetBitmap(new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height)))
            {
                bmp.Render(w);
                bmp.Save(Path.Combine(folder, "startup-dialog.png"));
            }
            w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == Loc.T("Skip"))
                ?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        });
        await Task.Delay(1500);
        for (var i = 0; i < 100 && LoadingText.IsVisible; i++)
            await Task.Delay(200);
        await Task.Delay(2500);
        Dialogs.DebugDialogOpened = null;

        if (Setup.IsVisible)
        {
            await Snap(folder, "setup");
            Close();
            return;
        }

        foreach (var tab in new[] { "models", "wad", "tracker" })
        {
            SelectTab(tab);
            await Task.Delay(1500);
            await Snap(folder, tab.ToLowerInvariant());
        }

        SelectTab("models");
        await Task.Delay(1200);
        Assets.SnapshotSelectFirstOption();
        await Task.Delay(500);
        await Snap(folder, "models-preview");

        HelpButton.IsChecked = true;
        await Task.Delay(300);
        await Snap(folder, "models-help");
        HelpButton.IsChecked = false;

        Assets.SnapshotShowInvalid(true);
        await Task.Delay(800);
        await Snap(folder, "models-invalid");
        Assets.SnapshotShowInvalid(false);

        Assets.SnapshotOnline(true);
        await Task.Delay(6000);
        await Snap(folder, "models-online");
        Assets.SnapshotOnline(false);

        SelectTab("archives");
        await Task.Delay(800);
        await Archives.SnapshotOpen("qw", "models.pk3");
        await Task.Delay(1500);
        await Snap(folder, "archives");
        await Archives.SnapshotOpen("id1", "pak0.pak", "gfx.wad/sb_quad");
        await Task.Delay(1500);
        await Snap(folder, "archives-wad");

        await SnapDialog(folder, "about", () => Dialogs.About(this), "Close");
        var zip = Path.Combine(Path.GetTempPath(), "qam-snapshot-pack.zip");
        File.Delete(zip);
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var (name, src) in new[] { ("textures/models/armor_0.png", Path.Combine(Settings.QuakeFolder, "qw", "textures", "models", "backpack_0.png")) })
                if (File.Exists(src)) using (var s = z.CreateEntry(name).Open()) s.Write(File.ReadAllBytes(src));
            using (var w = new StreamWriter(z.CreateEntry("progs/armor.mdl").Open())) w.Write("x");
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("x");
            using (var w = new StreamWriter(z.CreateEntry("sb_quad.png").Open())) w.Write("not really a png");
        }
        await SnapDialog(folder, "import-summary", () => ImportWithSummary([zip], "qam-snapshot-pack.zip"), "Revert");
        File.Delete(zip);

        ShowSetup(mandatory: false);
        await Task.Delay(500);
        await Snap(folder, "settings");
        Setup.SnapshotTab(1);
        await Task.Delay(500);
        await Snap(folder, "settings-types");
        Close();
    }

    /// <summary>Open a dialog, save a PNG of it, then press one of its buttons to close it.</summary>
    private async Task SnapDialog(string folder, string name, Func<Task> open, string closeWith)
    {
        var count = 0;
        Dialogs.DebugDialogOpened = w => Dispatcher.UIThread.Post(async () =>
        {
            foreach (var expander in w.GetLogicalDescendants().OfType<Expander>())
                expander.IsExpanded = true;
            await Task.Delay(900);
            var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
            using (var bmp = new RenderTargetBitmap(size))
            {
                bmp.Render(w);
                bmp.Save(Path.Combine(folder, (count++ == 0 ? name : $"{name}-{count}") + ".png"));
            }
            var buttons = w.GetLogicalDescendants().OfType<Button>().ToList();
            var button = buttons.FirstOrDefault(b => b.Content as string == Loc.T(closeWith))
                         ?? buttons.FirstOrDefault(b => b.Content as string == Loc.T("Continue"));
            button?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        });
        await open();
        Dialogs.DebugDialogOpened = null;
    }

    private async Task Snap(string folder, string name)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(this);
        bmp.Save(Path.Combine(folder, name + ".png"));
    }
}
#endif
