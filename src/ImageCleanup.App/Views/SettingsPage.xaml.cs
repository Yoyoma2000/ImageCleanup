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
        var localizationService = App.Services.GetRequiredService<LocalizationService>();
        ViewModel = new SettingsViewModel(settingsService, localizationService);

        this.InitializeComponent();
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Current;
        var confirm = new ContentDialog
        {
            Title             = loc.GetString("Settings.ClearCacheConfirmDialog.Title"),
            Content           = loc.GetString("Settings.ClearCacheConfirmDialog.Message"),
            PrimaryButtonText = loc.GetString("Settings.ClearCacheButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        ViewModel.ClearCache();
    }

    private async void OnClearMoveHistoryClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Current;
        var logCount = ViewModel.CountMoveLogs();

        var confirm = new ContentDialog
        {
            Title             = loc.GetString("Settings.ClearMoveHistoryConfirmDialog.Title"),
            Content           = loc.GetString("Settings.ClearMoveHistoryConfirmDialog.Message", logCount),
            PrimaryButtonText = loc.GetString("Settings.ClearMoveHistoryButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        ViewModel.ClearMoveHistory();
    }
}
