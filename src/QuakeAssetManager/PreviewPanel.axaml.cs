using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using QuakeAssetManager.Core;

namespace QuakeAssetManager;

/// <summary>An action button shown under the preview, matching the right-click menu.</summary>
public sealed record PreviewAction(string Label, Action Run, bool Danger = false);

public partial class PreviewPanel : UserControl
{
    private Func<float, float, Bitmap>? _turn;
    private float _yaw = -35, _pitch = 15;
    private Avalonia.Point? _dragFrom;

    public PreviewPanel()
    {
        InitializeComponent();
        // 3D models: drag the picture to turn them.
        MainFrame.PointerPressed += (_, e) => { if (_turn is not null) _dragFrom = e.GetPosition(MainFrame); };
        MainFrame.PointerReleased += (_, _) => _dragFrom = null;
        MainFrame.PointerMoved += (_, e) =>
        {
            if (_turn is null || _dragFrom is not { } from) return;
            var at = e.GetPosition(MainFrame);
            _yaw += (float)(at.X - from.X) * 0.8f;
            _pitch = Math.Clamp(_pitch + (float)(at.Y - from.Y) * 0.6f, -89, 89);
            _dragFrom = at;
            MainImage.Source = _turn(_yaw, _pitch);
        };
    }

    public void Clear(string? message = null, IEnumerable<PreviewAction>? actions = null)
    {
        Placeholder.Text = message ?? Loc.T("Select a file to preview it.");
        Placeholder.IsVisible = true;
        _turn = null;
        Title.IsVisible = false;
        MainFrame.IsVisible = false;
        MainImage.Source = null;
        MainInfo.Text = MainText.Text = ModifiedInfo.Text = null;
        ModifiedInfo.IsVisible = false;
        SetNote(HintInfo, null);
        SetNote(WarningInfo, null);
        SetCopies(null, null);
        CompareBox.IsVisible = false;
        CompareImage.Source = null;
        SetActions(actions);
    }

    /// <param name="hint">The client setting that decides whether the file is used (see <see cref="HintText"/>).</param>
    /// <param name="warning">A problem with the file, e.g. a spray bigger than the client loads.</param>
    public void Show(Preview main, Preview? compare = null, DateTime? modified = null, IEnumerable<PreviewAction>? actions = null,
                     string? title = null, string? hint = null, string? warning = null, IReadOnlyList<string>? copies = null,
                     string? copiesRoot = null)
    {
        Placeholder.IsVisible = false;
        _turn = main.Turn;
        (_yaw, _pitch) = (-35, 15);
        MainFrame.Cursor = _turn is null ? null : new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
        Title.Text = title;
        Title.IsVisible = title is not null;
        MainImage.Source = main.Image;
        MainFrame.IsVisible = main.Image is not null;
        MainInfo.Text = main.Info;
        ModifiedInfo.Text = modified is { } m ? Loc.F("Modified {0}", RelativeDate.Full(m)) : null;
        ModifiedInfo.IsVisible = modified is not null;
        SetNote(HintInfo, hint);
        SetNote(WarningInfo, warning);
        SetCopies(copies, copiesRoot);
        MainText.Text = main.Text;
        MainText.IsVisible = main.Text is not null;
        CompareBox.IsVisible = compare is not null;
        CompareImage.Source = compare?.Image;
        CompareInfo.Text = compare is null ? null : compare.Text is null ? compare.Info : $"{compare.Info}\n{compare.Text}";
        SetActions(actions);
    }

    /// <summary>The other copies of a duplicate; each opens the file explorer with the file selected.</summary>
    /// <param name="root">Paths are shown relative to this folder's parent (e.g. "assets\wad\pack\file.png").</param>
    private void SetCopies(IReadOnlyList<string>? copies, string? root)
    {
        CopiesList.Children.Clear();
        CopiesBox.IsVisible = copies is { Count: > 0 };
        foreach (var path in copies ?? [])
        {
            var link = new HyperlinkButton { Content = new TextBlock { Text = root is null ? path : Path.GetRelativePath(Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? root, path), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 }, Padding = new Avalonia.Thickness(0, 1) };
            ToolTip.SetTip(link, Loc.T("Show this file in your file explorer"));
            link.Click += (_, _) => Shell.ShowFile(path);
            CopiesList.Children.Add(link);
        }
    }

    private static void SetNote(TextBlock block, string? text)
    {
        block.Text = text;
        block.IsVisible = !string.IsNullOrEmpty(text);
    }

    /// <summary>"Used when gl_simpleitems is 1", "Chosen with crosshairimage"...</summary>
    public static string? HintText(CvarHint? hint) => hint is null ? null : hint.Kind.ToLowerInvariant() switch
    {
        "chosen" => "⚙ " + Loc.F("Chosen with {0}", hint.Cvar),
        "shown" => "⚙ " + Loc.F("Shown when {0} is {1}", hint.Cvar, hint.Value),
        "drawn" => "⚙ " + Loc.F("Drawn when {0} is {1}", hint.Cvar, hint.Value),
        _ => "⚙ " + Loc.F("Used when {0} is {1}", hint.Cvar, hint.Value),
    };

    /// <summary>Show an image that arrives later (e.g. a website screenshot).</summary>
    public void SetImage(Bitmap? image)
    {
        MainImage.Source = image;
        MainFrame.IsVisible = image is not null;
    }

    public void SetActions(IEnumerable<PreviewAction>? actions)
    {
        Actions.Children.Clear();
        foreach (var a in actions ?? [])
        {
            var button = new Button { Content = a.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (a.Danger) button.Classes.Add("danger");
            button.Click += (_, _) => a.Run();
            Actions.Children.Add(button);
        }
    }
}
