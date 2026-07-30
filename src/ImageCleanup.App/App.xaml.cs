using ImageCleanup.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App;

public partial class App : Application
{
    /// <summary>Minimal DI container so singleton services (e.g. ScanSessionService) are shared across pages.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// The app's single Window — pages need this for WinRT.Interop.WindowNative
    /// .GetWindowHandle when showing their own FolderPicker (a Page has no HWND
    /// of its own in an unpackaged app; the owning Window does). Named
    /// ShellWindow rather than MainWindow to avoid colliding with the
    /// MainWindow class name.
    /// </summary>
    public static Window ShellWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        var services = new ServiceCollection();
        services.AddSingleton<ScanSessionService>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        ShellWindow = _window;
        _window.Activate();
    }

    private Window? _window;
}
