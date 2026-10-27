using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager;

/// <summary>
/// Small modal dialogs. In every dialog Enter runs the main action, Esc cancels, Tab moves between fields
/// (the import summary is the exception: Esc does nothing there).
/// </summary>
public static partial class Dialogs
{
#if DEBUG
    /// <summary>Debug-only hook so the snapshot run can capture dialogs.</summary>
    public static Action<Window>? DebugDialogOpened;
#endif

    private static Window NewWindow(string title, double width)
    {
        var w = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = AppIcon.Value,
        };
#if DEBUG
        w.Opened += (_, _) => DebugDialogOpened?.Invoke(w);
#endif
        return w;
    }

    private static readonly Lazy<WindowIcon?> AppIcon = new(() =>
    {
        try { return new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://QuakeAssetManager/Assets/icon.ico"))); }
        catch (Exception) { return null; }
    });

    /// <summary>A dialog for the Settings tools (long lists), built by <see cref="Tools"/>.</summary>
    /// <remarks>
    /// A fixed, resizable height: with "size to content" a long list gets unlimited height and can't scroll.
    /// </remarks>
    public static Window ToolWindow(string title, double width, double height)
    {
        var w = NewWindow(title, width);
        w.SizeToContent = SizeToContent.Manual;
        w.Height = height;
        w.MinHeight = 320;
        w.CanResize = true;
        return w;
    }

    public static Grid ToolFooter(string hint, params Button[] buttons) => Footer(hint, buttons);

    private static TextBlock Hint(string text) => new() { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "muted" }, Margin = new Thickness(0, 8, 0, 4) };

    private static Grid Footer(string hint, params Button[] buttons)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        grid.Children.Add(Hint(hint));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var b in buttons) panel.Children.Add(b);
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);
        return grid;
    }

    private static string KeyHint(string action, bool tab = false) =>
        Loc.F("Enter: {0}", action.ToLower(CultureInfo.CurrentCulture)) + " · " + Loc.T("Esc: cancel") + (tab ? " · " + Loc.T("Tab: next") : "");

    /// <param name="cancelText">The other button (Esc), "Cancel" unless given.</param>
    public static async Task<bool> Confirm(Window owner, string title, string message, string okText, bool danger = false, string? cancelText = null)
    {
        var w = NewWindow(title, 440);
        var ok = new Button { Content = okText, IsDefault = true };
        if (danger) ok.Classes.Add("danger");
        var cancel = new Button { Content = cancelText ?? Loc.T("Cancel"), IsCancel = true };
        var result = false;
        ok.Click += (_, _) => { result = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        w.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                Footer(KeyHint(okText), cancel, ok),
            },
        };
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task Message(Window owner, string title, string message)
    {
        var w = NewWindow(title, 440);
        var ok = new Button { Content = Loc.T("OK"), IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => w.Close();
        w.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, Footer(Loc.T("Enter or Esc: close"), ok) },
        };
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
    }

    /// <summary>Ask for a name. Without <paramref name="actionText"/> it's a rename. Returns null when cancelled.</summary>
    public static async Task<string?> Rename(Window owner, string title, string current, string suggestion, Func<string, string?> validate, string? actionText = null)
    {
        var isRename = actionText is null;
        actionText ??= Loc.T("Rename");
        var w = NewWindow(title, 440);
        var box = new TextBox { Text = suggestion };
        var error = new TextBlock { Classes = { "error" }, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
        var ok = new Button { Content = actionText, IsDefault = true };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        string? result = null;
        box.TextChanged += (_, _) => error.IsVisible = false;
        ok.Click += (_, _) =>
        {
            var text = box.Text?.Trim() ?? "";
            if (validate(text) is { } message)
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }
            result = text;
            w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        var content = new StackPanel { Margin = new Thickness(20) };
        if (isRename)
            content.Children.Add(new TextBlock { Text = Loc.F("Current name: {0}", current), Classes = { "muted" } });
        content.Children.Add(Label(isRename ? Loc.T("New name") : Loc.T("Name")));
        content.Children.Add(box);
        content.Children.Add(error);
        if (isRename && suggestion != current)
            content.Children.Add(new TextBlock { Text = Loc.T("Suggested from the closest valid name"), Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 0) });
        content.Children.Add(Footer(KeyHint(actionText), cancel, ok));
        w.Content = content;
        w.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Pick an assets subfolder. With <paramref name="currentFileName"/>, the file keeps its name unless "Rename file to"
    /// is ticked; the name box then starts with <paramref name="suggestedFileName"/>. Returns null when cancelled;
    /// FileName is null when the name is kept.
    /// </summary>
    public static async Task<(string Folder, string? FileName)?> ChooseDestination(
        Window owner, string title, string actionText, IReadOnlyList<string> folders, string suggestedFolder,
        string? currentFileName, string? suggestedFileName, Func<string, string?> validateName)
    {
        var w = NewWindow(title, 460);
        var folderBox = new AutoCompleteBox
        {
            ItemsSource = folders,
            Text = suggestedFolder,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            MinimumPrefixLength = 0,
            Watermark = Loc.T("Folder name"),
        };
        var rename = new CheckBox { Content = Loc.T("Rename file to"), Margin = new Thickness(0, 10, 0, 4) };
        var fileBox = new TextBox { Text = suggestedFileName ?? currentFileName ?? "", IsEnabled = false };
        rename.IsCheckedChanged += (_, _) =>
        {
            fileBox.IsEnabled = rename.IsChecked == true;
            if (fileBox.IsEnabled)
            {
                fileBox.Focus();
                fileBox.SelectAll();
            }
        };
        var error = new TextBlock { Classes = { "error" }, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = actionText, IsDefault = true };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        (string, string?)? result = null;
        ok.Click += (_, _) =>
        {
            var folder = folderBox.Text?.Trim() ?? "";
            var file = currentFileName is not null && rename.IsChecked == true ? fileBox.Text?.Trim() ?? "" : null;
            var message = validateName(folder) is { } fe ? Loc.T("Folder:") + " " + fe
                : file is not null && validateName(file) is { } ne ? Loc.T("File name:") + " " + ne : null;
            if (message is not null)
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }
            result = (folder, file);
            w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(Label(Loc.T("Folder (pick one or type a new name)")));
        content.Children.Add(folderBox);
        if (currentFileName is not null)
        {
            content.Children.Add(rename);
            content.Children.Add(fileBox);
            if (suggestedFileName is not null && suggestedFileName != currentFileName)
                content.Children.Add(new TextBlock { Text = Loc.T("Suggested from the closest valid name"), Classes = { "muted" }, Margin = new Thickness(0, 4, 0, 0) });
        }
        content.Children.Add(error);
        content.Children.Add(Footer(KeyHint(actionText, tab: true), cancel, ok));
        w.Content = content;
        w.Opened += (_, _) => folderBox.Focus();
        await w.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Summary after a drop, download or archive copy. OK (or Enter) adds the files to Assets; Revert throws them away.
    /// Revert has no key, and Esc and the window's close button do nothing, so an import is never lost by accident.
    /// Returns true when the files were added.
    /// </summary>
    public enum ImportChoice { Revert, Ok, OkAddDuplicates }

    public static async Task<ImportChoice> ImportSummary(Window owner, ImportSession session)
    {
        var title = Loc.F("Added from {0}", session.Title);
        var w = NewWindow(title, 520);
        var decided = false;
        var committed = false;
        w.Closing += (_, e) => { if (!decided) e.Cancel = true; };

        var content = new StackPanel { Margin = new Thickness(20), Spacing = 4 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions() };
        void Row(string label, string value, bool muted = false)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 2, 16, 2) };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2) };
            if (muted) { l.Classes.Add("muted"); v.Classes.Add("muted"); }
            Grid.SetRow(l, grid.RowDefinitions.Count - 1);
            Grid.SetRow(v, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
        }
        var sep = System.IO.Path.DirectorySeparatorChar;
        // Only the asset types that received files.
        foreach (var type in AssetType.Known.Where(t => session.AddedCount(t.Kind) > 0))
        {
            var n = session.AddedCount(type.Kind);
            var invalid = session.InvalidCount(type.Kind);
            var label = type.Visible ? type.DisplayName : $"{type.DisplayName} ({Loc.T("hidden")})";
            var value = n == 0 ? "0" : Loc.Files(n) + " → " + string.Join(", ", session.FoldersFor(type.Kind).Select(f => $"assets{sep}{type.FolderName}{sep}{f}"));
            if (invalid > 0) value += " · " + Loc.F("{0} with names the game doesn't load (marked invalid)", invalid);
            Row(label, value, muted: n == 0);
        }
        // Files the app doesn't manage are kept in assets/other with their game path.
        var other = session.AddedCount(Importer.Other);
        if (other > 0)
            Row(Loc.T("Not managed by the app"), Loc.Files(other) + $" → assets{sep}{Importer.Other} (" + string.Join(", ", session.NotManaged.Select(kv => $"{kv.Key}: {kv.Value}")) + ")");
        if (session.AddedDuplicates > 0)
            Row(Loc.T("Duplicates"), Loc.F("{0} already in Local assets, added anyway", session.AddedDuplicates), muted: true);
        if (session.Duplicates > 0)
            Row(Loc.T("Already in Local assets"), Loc.F("{0} skipped", session.Duplicates), muted: true);
        if (session.Ignored.Count > 0)
            Row(Loc.T("Ignored"), session.Ignored.Count.ToString(CultureInfo.CurrentCulture), muted: true);
        content.Children.Add(grid);

        if (session.Ignored.Count > 0 || session.Errors.Count > 0)
        {
            var lines = session.Errors.Select(e => Loc.T("Error:") + " " + e).Concat(session.Ignored.Select(TranslateIgnored));
            content.Children.Add(new Expander
            {
                Header = Loc.T("Show the ignored files"),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new ScrollViewer
                {
                    MaxHeight = 200,
                    Content = new SelectableTextBlock { Text = string.Join("\n", lines), FontSize = 12, TextWrapping = TextWrapping.Wrap },
                },
            });
        }
        if (session.Files.Count == 0)
            content.Children.Add(new TextBlock { Text = Loc.T("Nothing was added: everything was already in Local assets, skipped or not supported."), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

        // Files whose contents are already in Local assets: skip them (default) or add them anyway.
        CheckBox? addDuplicates = null;
        if (session.Duplicates > 0)
        {
            addDuplicates = new CheckBox
            {
                Margin = new Thickness(0, 8, 0, 0),
                Content = new TextBlock
                {
                    Text = session.Duplicates == 1
                        ? Loc.T("1 file is already in Local assets (same contents). Add it anyway, marked as a duplicate")
                        : Loc.F("{0} are already in Local assets (same contents). Add them anyway, marked as duplicates", Loc.Files(session.Duplicates)),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            content.Children.Add(addDuplicates);
        }
        var ok = new Button { Content = Loc.T("OK"), IsDefault = true };
        var revert = new Button { Content = Loc.T("Revert"), IsVisible = session.Files.Count > 0 };
        ToolTip.SetTip(revert, Loc.T("Don't add these files to Local assets"));
        ok.Click += (_, _) => { decided = true; committed = true; w.Close(); };
        revert.Click += (_, _) => { decided = true; w.Close(); };
        content.Children.Add(Footer(Loc.F("Enter: {0}", Loc.T("OK")), revert, ok));
        w.Content = content;
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
        return !committed ? ImportChoice.Revert : addDuplicates?.IsChecked == true ? ImportChoice.OkAddDuplicates : ImportChoice.Ok;
    }

    /// <summary>"path — reason": translate the reason part.</summary>
    private static string TranslateIgnored(string line)
    {
        var at = line.LastIndexOf(" — ", StringComparison.Ordinal);
        if (at < 0) return line;
        var reason = line[(at + 3)..];
        var several = reason.IndexOf(" (", StringComparison.Ordinal);
        reason = several > 0 && reason.StartsWith("name fits several asset types", StringComparison.Ordinal)
            ? Loc.T("name fits several asset types") + reason[several..]
            : Loc.T(reason);
        return line[..at] + " — " + reason;
    }

    public static async Task About(Window owner)
    {
        var asm = typeof(Dialogs).Assembly;
        var version = asm.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "?";
        var built = asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        var when = DateTime.TryParseExact(built, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", CultureInfo.CurrentUICulture)
            : "";
        if (when.Length > 0) when = char.ToUpper(when[0], CultureInfo.CurrentUICulture) + when[1..];

        var w = NewWindow(Loc.T("About Quake Asset Manager"), 600);
        var ok = new Button { Content = Loc.T("Close"), IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => w.Close();
        var accent = Themes.Brush("ThemeAccentBrush", "#F07B2B");
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 6 };
        content.Children.Add(new TextBlock { Text = "Quake Asset Manager", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = accent });
        content.Children.Add(new TextBlock { Text = Loc.F("Version {0}", version) + (when.Length > 0 ? " · " + when : ""), FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = Loc.T("Vibe coded in Claude"), Classes = { "muted" } });
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("The goal of the app is to assist in the management of the Quake assets that are not configurable in the client. Assets like the hud icons, textures for models and others require a specific file name to be placed on a specific folder. This app manages those, by creating and keeping a copy of alternative assets in a different folder, and replacing the ones in your quake folder with a click."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        content.Children.Add(new TextBlock { Text = Loc.T("I hope you find it useful."), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = Loc.T("Made with love by mushi"), FontStyle = FontStyle.Italic, Foreground = accent, Margin = new Thickness(0, 4, 0, 0) });
        content.Children.Add(new Expander
        {
            Header = Loc.T("Details about QuakeWorld assets"),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer { MaxHeight = 380, Content = FormattedFacts(accent) },
        });
        content.Children.Add(Footer(Loc.T("Enter or Esc: close"), ok));
        w.Content = content;
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
    }

    [GeneratedRegex(@"(\*\*.+?\*\*|`.+?`)")]
    private static partial Regex MarkupRegex();

    /// <summary>
    /// The facts text with a little markup: "# " headings, "**bold**" and "`code`" (folder and file names).
    /// Each line is translated on its own; translations keep the markup.
    /// </summary>
    private static SelectableTextBlock FormattedFacts(IBrush accent)
    {
        var codeBrush = Themes.Brush("ThemeCodeBrush", "#4FB3D9");
        var block = new SelectableTextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        var first = true;
        foreach (var raw in AboutFacts.Lines)
        {
            if (!first) block.Inlines!.Add(new LineBreak());
            first = false;
            if (raw.Length == 0) continue;
            var line = Loc.T(raw);
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                block.Inlines!.Add(new Run(line[2..].ToUpper(CultureInfo.CurrentCulture)) { FontWeight = FontWeight.Bold, Foreground = accent, FontSize = 13 });
                continue;
            }
            foreach (var part in MarkupRegex().Split(line))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length > 4)
                    block.Inlines!.Add(new Run(part[2..^2]) { FontWeight = FontWeight.Bold });
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                    block.Inlines!.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas, Menlo, monospace"), Foreground = codeBrush });
                else
                    block.Inlines!.Add(new Run(part));
            }
        }
        return block;
    }
}
