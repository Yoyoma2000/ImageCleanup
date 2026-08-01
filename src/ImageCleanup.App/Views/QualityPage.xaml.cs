using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using ImageCleanup.Data.Services;
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
        var loc = LocalizationService.Current;

        // Confirmation dialog
        var confirm = new ContentDialog
        {
            Title             = loc.GetString("Common.CommitConfirmDialog.Title"),
            Content           = loc.GetString("Common.CommitConfirmDialog.Message", ViewModel.StagedItems.Count),
            PrimaryButtonText = loc.GetString("Common.CommitButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        var result = await ViewModel.CommitStagedChangesAsync();

        // Summary dialog
        var summary = new ContentDialog
        {
            Title           = loc.GetString("Common.CommitCompleteDialog.Title"),
            Content         = result.Summary,
            CloseButtonText = loc.GetString("Common.OkButton"),
            XamlRoot        = this.XamlRoot,
        };
        await summary.ShowAsync();
    }

    private void OnRemoveStagingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int stagingId)
            ViewModel.RemoveStagingEntry(stagingId);
    }

    private async void OnViewPhotoClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not FileActionViewModel fa) return;

        var dialog = new SinglePhotoDialog(fa.FilePath, ViewModel.GetDetailThumbnailProvider(fa.FilePath))
        {
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
