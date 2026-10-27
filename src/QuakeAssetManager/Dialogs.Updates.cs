using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuakeAssetManager;

/// <summary>Check for updates: tell the user about a new version, and install it.</summary>
public static partial class Dialogs
{
    /// <summary>
    /// Check GitHub. With <paramref name="quiet"/> (the check at start), only a new version is shown; "up to date"
    /// and failures stay silent.
    /// </summary>
    public static async Task CheckForUpdates(MainWindow owner, bool quiet)
    {
        Updates.Release? release;
        try
        {
            if (!quiet) owner.SetStatus(Loc.T("Checking for updates…"));
            release = await Updates.CheckAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (!quiet)
            {
                owner.SetStatus("");
                await Message(owner, Loc.T("Check for updates"), Loc.T("GitHub couldn't be reached. Check your internet connection and try again.") + "\n" + e.Message);
            }
            return;
        }
        if (release is null)
        {
            if (!quiet)
            {
                owner.SetStatus("");
                await Message(owner, Loc.T("Check for updates"), Loc.F("You have the latest version ({0}).", Updates.Current.ToString(3)));
            }
            return;
        }
        owner.SetStatus("");
        await UpdateAvailable(owner, release);
    }

    private static async Task UpdateAvailable(MainWindow owner, Updates.Release release)
    {
        var w = NewWindow(Loc.T("Update available"), 560);
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = Loc.F("Version {0} is available. You have {1}.", release.Version.ToString(3), Updates.Current.ToString(3)),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (release.Notes.Length > 0)
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                Content = new SelectableTextBlock { Text = release.Notes, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
            });
        var canInstall = Updates.CanInstall && release.DownloadUrl is not null;
        content.Children.Add(new TextBlock
        {
            Text = canInstall
                ? Loc.T("Install downloads the new version, replaces the app and restarts it. Your settings and Local assets are kept.")
                : Loc.T("This copy can't update itself (it isn't the release version). Download the new version from the release page."),
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        });
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false, Height = 6 };
        content.Children.Add(progress);
        var error = new TextBlock { Classes = { "error" }, IsVisible = false };
        content.Children.Add(error);

        var install = new Button { Content = Loc.T("Install and restart"), IsDefault = canInstall, IsVisible = canInstall, Classes = { "accent" } };
        var page = new Button { Content = Loc.T("Open release page") };
        var later = new Button { Content = Loc.T("Not now"), IsCancel = true };
        page.Click += async (_, _) => await owner.Launcher.LaunchUriAsync(new Uri(release.PageUrl));
        later.Click += (_, _) => w.Close();
        install.Click += async (_, _) =>
        {
            install.IsEnabled = page.IsEnabled = later.IsEnabled = false;
            progress.IsVisible = true;
            error.IsVisible = false;
            try
            {
                await Updates.InstallAsync(release, new Progress<int>(p => progress.Value = p));
                Updates.Restart();
            }
            catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or TaskCanceledException)
            {
                error.Text = Loc.T("The update couldn't be installed:") + " " + e.Message;
                error.IsVisible = true;
                install.IsEnabled = page.IsEnabled = later.IsEnabled = true;
                progress.IsVisible = false;
            }
        };
        var footer = Footer(Loc.T("Esc: not now"), later, page, install);
        content.Children.Add(footer);
        w.Content = content;
        w.Opened += (_, _) => (canInstall ? install : page).Focus();
        await w.ShowDialog(owner);
    }
}
