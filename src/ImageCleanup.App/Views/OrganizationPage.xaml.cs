using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace ImageCleanup.App.Views;

/// <summary>
/// Organization: TreeView preview over OrganizationPlanner's proposed
/// hierarchy, with a checkbox per node (Year/Month/Category/File) for
/// selective execution, plus move execution — moves only the currently
/// selected/checked files to a chosen destination folder, behind an
/// explicit confirmation naming the real, non-Recycle-Bin nature of the
/// operation and the actual selected-vs-total file count.
/// </summary>
public sealed partial class OrganizationPage : Page
{
    public OrganizationViewModel ViewModel { get; }

    public OrganizationPage()
    {
        // Cached rather than recreated on every nav, same as Duplicates/Quality.
        this.NavigationCacheMode = NavigationCacheMode.Enabled;

        var scanSession = App.Services.GetRequiredService<ScanSessionService>();
        ViewModel = new OrganizationViewModel(scanSession);

        this.InitializeComponent();
    }

    /// <summary>Lazily requests thumbnails for a Category node's files only once it's actually expanded.</summary>
    private void OnTreeViewExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is OrganizationNodeViewModel node)
            ViewModel.RequestThumbnailsFor(node);
    }

    /// <summary>
    /// A ThreeState CheckBox is required so a group node CAN render
    /// indeterminate (some but not all descendants selected), but that also
    /// makes a raw click cycle through THREE states by default (unchecked ->
    /// checked -> indeterminate -> unchecked) — indeterminate should only
    /// ever be a derived, read-only display state, never something a user
    /// clicks their way into. This handler ignores whatever WinUI just
    /// cycled the control's own IsChecked to, and instead reads the
    /// ViewModel's last-known IsChecked (unaffected by that internal
    /// three-state cycle, since the binding is OneWay) to decide
    /// deterministically: anything not fully checked becomes fully checked;
    /// fully checked becomes fully unchecked. SetSelected then re-notifies
    /// IsChecked, which snaps the CheckBox's display back to the correct
    /// value regardless of what the click cycled it to.
    /// </summary>
    private void OnNodeCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: OrganizationNodeViewModel node })
            node.SetSelected(node.IsChecked != true);
    }

    private async void OnSelectDestinationClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(App.ShellWindow));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        ViewModel.SetDestinationFolder(folder.Path);
    }

    private async void OnExecuteClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedFileCount;
        var total    = ViewModel.PlannedFileCount;
        var countText = selected == total
            ? $"{total} file(s)"
            : $"{selected} of {total} file(s)";

        var confirm = new ContentDialog
        {
            Title             = "Organize Files",
            Content           =
                $"This will MOVE {countText} on disk to:\n\n{ViewModel.DestinationFolder}\n\n" +
                "This is a real file move, not a Recycle Bin operation — it is " +
                "NOT reversible through the Recycle Bin. A move log will be " +
                "written before anything is moved, in case you need to reverse " +
                "it manually. Continue?",
            PrimaryButtonText = "Move Files",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        var result = await ViewModel.ExecutePlanAsync();

        var summary = new ContentDialog
        {
            Title           = "Organize Complete",
            Content         = $"{result.Summary}\n\nMove log: {result.MoveLogPath}",
            CloseButtonText = "OK",
            XamlRoot        = this.XamlRoot,
        };
        await summary.ShowAsync();
    }
}
