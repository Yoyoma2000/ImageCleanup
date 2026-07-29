using ImageCleanup.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ImageCleanup.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        this.InitializeComponent();
    }

    private async void OnSelectFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await ViewModel.ScanFolderAsync(folder.Path);
    }

    private async void OnCommitClick(object sender, RoutedEventArgs e)
    {
        // Confirmation dialog
        var confirm = new ContentDialog
        {
            Title           = "Commit Changes",
            Content         = $"This will move {ViewModel.StagedItems.Count} file(s) to the Recycle Bin or a new location. Continue?",
            PrimaryButtonText   = "Commit",
            CloseButtonText     = "Cancel",
            XamlRoot        = this.Content.XamlRoot,
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
            XamlRoot        = this.Content.XamlRoot,
        };
        await summary.ShowAsync();
    }

    private void OnRemoveStagingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int stagingId)
            ViewModel.RemoveStagingEntry(stagingId);
    }
}
