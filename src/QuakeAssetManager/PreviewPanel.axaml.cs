using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;

namespace QuakeAssetManager;

/// <summary>An action button shown under the preview, matching the right-click menu.</summary>
public sealed record PreviewAction(string Label, Action Run, bool Danger = false);

public partial class PreviewPanel : UserControl
{
    public PreviewPanel() => InitializeComponent();

    public void Clear(string? message = null, IEnumerable<PreviewAction>? actions = null)
    {
        Placeholder.Text = message ?? Loc.T("Select a file to preview it.");
        Placeholder.IsVisible = true;
        Title.IsVisible = false;
        MainFrame.IsVisible = false;
        MainImage.Source = null;
        MainInfo.Text = MainText.Text = ModifiedInfo.Text = null;
        ModifiedInfo.IsVisible = false;
        CompareBox.IsVisible = false;
        CompareImage.Source = null;
        SetActions(actions);
    }

    public void Show(Preview main, Preview? compare = null, DateTime? modified = null, IEnumerable<PreviewAction>? actions = null, string? title = null)
    {
        Placeholder.IsVisible = false;
        Title.Text = title;
        Title.IsVisible = title is not null;
        MainImage.Source = main.Image;
        MainFrame.IsVisible = main.Image is not null;
        MainInfo.Text = main.Info;
        ModifiedInfo.Text = modified is { } m ? Loc.F("Modified {0}", RelativeDate.Full(m)) : null;
        ModifiedInfo.IsVisible = modified is not null;
        MainText.Text = main.Text;
        MainText.IsVisible = main.Text is not null;
        CompareBox.IsVisible = compare is not null;
        CompareImage.Source = compare?.Image;
        CompareInfo.Text = compare is null ? null : compare.Text is null ? compare.Info : $"{compare.Info}\n{compare.Text}";
        SetActions(actions);
    }

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
            if (a.Danger) button.Foreground = Avalonia.Media.Brushes.IndianRed;
            button.Click += (_, _) => a.Run();
            Actions.Children.Add(button);
        }
    }
}
