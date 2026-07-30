using ImageCleanup.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App;

public partial class App : Application
{
    /// <summary>Minimal DI container so singleton services (e.g. ScanSessionService) are shared across pages.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

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
        _window.Activate();
    }

    private Window? _window;
}
