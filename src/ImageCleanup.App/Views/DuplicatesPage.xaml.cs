using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using ImageCleanup.Data.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ImageCleanup.App.Views;

public sealed partial class DuplicatesPage : Page
{
    public DuplicatesViewModel ViewModel { get; }

    public DuplicatesPage()
    {
        // Cached rather than recreated on every nav — DuplicatesViewModel
        // subscribes to ScanSessionService.ScanCompleted for the lifetime of
        // the page, so reusing one instance avoids stacking up subscriptions.
        this.NavigationCacheMode = NavigationCacheMode.Enabled;

        var scanSession = App.Services.GetRequiredService<ScanSessionService>();
        ViewModel = new DuplicatesViewModel(scanSession);

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

    private async void OnViewGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DuplicateGroupViewModel group) return;

        ViewModel.RequestDetailThumbnails(group);

        var dialog = new GroupDetailDialog(group)
        {
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
