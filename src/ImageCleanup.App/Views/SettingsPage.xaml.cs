using ImageCleanup.App.ViewModels;
using ImageCleanup.Data.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ImageCleanup.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Enabled;

        var settingsService = App.Services.GetRequiredService<SettingsService>();
        ViewModel = new SettingsViewModel(settingsService);

        this.InitializeComponent();
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title             = "Clear Cache",
            Content           =
                "This deletes the scan cache. Nothing on disk is affected — the " +
                "next time you scan any folder, it will take longer while every " +
                "file is re-hashed and re-analyzed from scratch. Continue?",
            PrimaryButtonText = "Clear Cache",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        ViewModel.ClearCache();
    }

    private async void OnClearMoveHistoryClick(object sender, RoutedEventArgs e)
    {
        var logCount = ViewModel.CountMoveLogs();

        var confirm = new ContentDialog
        {
            Title             = "Clear Move History",
            Content           =
                $"This permanently deletes all {logCount} Organization move log(s). " +
                "Unlike Clear Cache, this is NOT recoverable — any past Organization " +
                "move you haven't undone yet can never be undone through the app " +
                "again after this. Continue?",
            PrimaryButtonText = "Clear Move History",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        ViewModel.ClearMoveHistory();
    }
}
