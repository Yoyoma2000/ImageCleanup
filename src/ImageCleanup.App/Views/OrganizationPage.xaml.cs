using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using ImageCleanup.Data.Services;
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

    private async void OnViewPhotoClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrganizationNodeViewModel node || node.SourcePath is null)
            return;

        var dialog = new SinglePhotoDialog(node.SourcePath, ViewModel.GetDetailThumbnailProvider(node.SourcePath))
        {
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
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
        var loc = LocalizationService.Current;

        var selected = ViewModel.SelectedFileCount;
        var total    = ViewModel.PlannedFileCount;
        var countText = selected == total
            ? loc.GetString("Organization.TotalFiles", total)
            : loc.GetString("Organization.CountOfTotalFiles", selected, total);

        var confirm = new ContentDialog
        {
            Title             = loc.GetString("Organization.OrganizeConfirmDialog.Title"),
            Content           = loc.GetString("Organization.OrganizeConfirmDialog.Message", countText, ViewModel.DestinationFolder),
            PrimaryButtonText = loc.GetString("Organization.OrganizeConfirmDialog.PrimaryButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var choice = await confirm.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        var result = await ViewModel.ExecutePlanAsync();

        var summary = new ContentDialog
        {
            Title           = loc.GetString("Organization.OrganizeCompleteDialog.Title"),
            Content         = loc.GetString("Organization.OrganizeCompleteDialog.Message", result.Summary, result.MoveLogPath),
            CloseButtonText = loc.GetString("Common.OkButton"),
            XamlRoot        = this.XamlRoot,
        };
        await summary.ShowAsync();
    }

    /// <summary>
    /// Lets the user browse past move logs and reverse one — a ListView
    /// inside a ContentDialog rather than a full picker control, since this
    /// is a simple single-selection browse/pick from a handful of files, not
    /// a general file-open dialog (the logs live in a fixed app-owned
    /// directory, not somewhere the user browses to).
    /// </summary>
    private async void OnUndoMoveClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Current;

        var logs = await ViewModel.GetAvailableMoveLogsAsync();
        if (logs.Count == 0)
        {
            var none = new ContentDialog
            {
                Title           = loc.GetString("Organization.NoMoveLogsDialog.Title"),
                Content         = loc.GetString("Organization.NoMoveLogsDialog.Message"),
                CloseButtonText = loc.GetString("Common.OkButton"),
                XamlRoot        = this.XamlRoot,
            };
            await none.ShowAsync();
            return;
        }

        var listView = new ListView
        {
            // MoveLog.Timestamp is stored as UTC (correct for a durable
            // record — see OrganizationExecutor) — convert to local time
            // only here, at display time, so the picker shows "1:23 AM"
            // for a move made at 1:23 AM local rather than its UTC offset.
            ItemsSource = logs
                .Select(l => loc.GetString("Organization.MoveLogSummary",
                    l.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), l.FileCount, l.DestinationRoot))
                .ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
        };

        var pickDialog = new ContentDialog
        {
            Title             = loc.GetString("Organization.SelectMoveDialog.Title"),
            Content           = listView,
            PrimaryButtonText = loc.GetString("Organization.SelectMoveDialog.PrimaryButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var pickChoice = await pickDialog.ShowAsync();
        if (pickChoice != ContentDialogResult.Primary || listView.SelectedIndex < 0)
            return;

        var selectedLog = logs[listView.SelectedIndex];

        var confirm = new ContentDialog
        {
            Title             = loc.GetString("Organization.UndoConfirmDialog.Title"),
            Content           = loc.GetString("Organization.UndoConfirmDialog.Message",
                selectedLog.FileCount, selectedLog.DestinationRoot, selectedLog.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            PrimaryButtonText = loc.GetString("Organization.UndoConfirmDialog.PrimaryButton"),
            CloseButtonText   = loc.GetString("Common.CancelButton"),
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot,
        };

        var confirmChoice = await confirm.ShowAsync();
        if (confirmChoice != ContentDialogResult.Primary)
            return;

        var result = await ViewModel.UndoMoveLogAsync(selectedLog.Path);

        var summary = new ContentDialog
        {
            Title           = loc.GetString("Organization.UndoCompleteDialog.Title"),
            Content         = result.Summary,
            CloseButtonText = loc.GetString("Common.OkButton"),
            XamlRoot        = this.XamlRoot,
        };
        await summary.ShowAsync();
    }
}
