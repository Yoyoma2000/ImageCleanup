using ImageCleanup.App.Services;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;
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
        services.AddSingleton<SettingsService>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        ShellWindow = _window;

        // Applied before Activate() so the window never flashes the wrong
        // theme on launch — RequestedTheme on the root element is honored
        // as soon as content is set, ahead of the first paint.
        var settings = Services.GetRequiredService<SettingsService>().Load();
        ApplyTheme(settings.Theme);

        _window.Activate();
    }

    /// <summary>
    /// Applies a theme immediately by setting RequestedTheme on the shell
    /// window's root element — WinUI re-themes that element and every
    /// descendant live, no restart needed. AppTheme.System clears any
    /// override (ElementTheme.Default) so the element defers back to
    /// whatever the OS is set to.
    /// </summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (ShellWindow?.Content is not FrameworkElement root) return;

        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark  => ElementTheme.Dark,
            _              => ElementTheme.Default,
        };
    }

    private Window? _window;
}
