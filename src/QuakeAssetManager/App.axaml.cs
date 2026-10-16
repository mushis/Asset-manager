using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuakeAssetManager.Core;

namespace QuakeAssetManager;

public partial class App : Application
{
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The language must be set before any window is built: XAML texts are translated as they load.
            Loc.Set(AppSettings.Load().Language);
            _window = new MainWindow();
            desktop.MainWindow = _window;
            // Last line of defence: report unexpected errors instead of closing the app.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _window?.SetStatus(Loc.T("Unexpected error:") + " " + e.Exception.Message);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Rebuild the main window, e.g. after the language changed.</summary>
    public void ReopenMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || _window is null) return;
        var old = _window;
        _window = new MainWindow { Width = old.Width, Height = old.Height, Position = old.Position, WindowState = old.WindowState };
        desktop.MainWindow = _window;
        _window.Show();
        old.Close();
    }
}
