using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace QuakeAssetManager;

/// <summary>The welcome screen shown at start: logo, what the app does, and "Don't show this again".</summary>
public static partial class Dialogs
{
    private static readonly Lazy<Bitmap?> Logo = new(() =>
    {
        try { return new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://QuakeAssetManager/Assets/logo.png"))); }
        catch (Exception) { return null; }
    });

    /// <summary>Returns true when "Don't show this again" was ticked.</summary>
    public static async Task<bool> Welcome(Window owner)
    {
        var w = NewWindow(Loc.T("Welcome to Quake Asset Manager"), 580);
        var accent = Themes.Brush("ThemeAccentBrush", "#D9661F");
        var content = new StackPanel { Margin = new Thickness(32, 24, 32, 20), Spacing = 10 };
        if (Logo.Value is { } logo)
            content.Children.Add(new Image { Source = logo, Width = 380, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("Swap models, HUDs, and more in one click."),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("A file explorer made for Quake assets: models, HUD, tracker, charsets and more. See what the game uses next to every alternative you have, apply one with a click, and jump into the game to test it. Your files are always backed up first, all in one Local assets folder."),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 21,
        });
        var features = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var line in new[]
                 {
                     Loc.T("Backs up your files automatically, all in one Local assets folder"),
                     Loc.T("Show alternatives: every version of a file at a glance"),
                     Loc.T("Download from gfx.quakeworld.nu, or drag and drop, straight into your Local assets"),
                     Loc.T("And much more"),
                 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = "✓", Foreground = accent, FontWeight = FontWeight.Bold });
            row.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, MaxWidth = 470 });
            features.Children.Add(row);
        }
        content.Children.Add(features);

        var dontShow = new CheckBox { Content = Loc.T("Don't show this again"), IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
        var start = new Button { Content = Loc.T("Get started"), IsDefault = true, IsCancel = true, Classes = { "accent" } };
        start.Click += (_, _) => w.Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(dontShow);
        Grid.SetColumn(start, 1);
        footer.Children.Add(start);
        content.Children.Add(footer);

        w.Content = content;
        w.Opened += (_, _) => start.Focus();
        await w.ShowDialog(owner);
        return dontShow.IsChecked == true;
    }
}
