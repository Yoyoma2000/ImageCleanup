using ImageCleanup.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ImageCleanup.App.Views;

/// <summary>
/// Larger, scrollable view of every file in a duplicate group. Binds directly
/// to the same DuplicateGroupViewModel/FileActionViewModel instances shown in
/// the main list, so actions taken here update the same staging state.
/// </summary>
public sealed partial class GroupDetailDialog : ContentDialog
{
    public DuplicateGroupViewModel ViewModel { get; }

    public GroupDetailDialog(DuplicateGroupViewModel group)
    {
        ViewModel = group;
        this.InitializeComponent();
    }
}
