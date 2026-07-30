using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ImageCleanup.App.Views;

public sealed partial class QualityPage : Page
{
    public QualityViewModel ViewModel { get; }

    public QualityPage()
    {
        // Cached rather than recreated on every nav — QualityViewModel
        // subscribes to ScanSessionService.ScanCompleted for the lifetime of
        // the page, so reusing one instance avoids stacking up subscriptions.
        this.NavigationCacheMode = NavigationCacheMode.Enabled;

        var scanSession = App.Services.GetRequiredService<ScanSessionService>();
        ViewModel = new QualityViewModel(scanSession);

        this.InitializeComponent();
    }

    private async void OnCommitClick(object sender, RoutedEventArgs e)
    {
        // Confirmation dialog
        var confirm = new ContentDialog
        {
            Title             = "Commit Changes",
            Content           = $"This will move {ViewModel.StagedItems.Count} file(s) to the Recycle Bin or a new location. Continue?",
            PrimaryButtonText = "Commit",
            CloseButtonText   = "Cancel",
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        var result = await ViewModel.CommitStagedChangesAsync();

        // Summary dialog
        var summary = new ContentDialog
        {
            Title           = "Commit Complete",
            Content         = result.Summary,
            CloseButtonText = "OK",
            XamlRoot        = this.XamlRoot,
        };
        await summary.ShowAsync();
    }

    private void OnRemoveStagingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int stagingId)
            ViewModel.RemoveStagingEntry(stagingId);
    }
}
