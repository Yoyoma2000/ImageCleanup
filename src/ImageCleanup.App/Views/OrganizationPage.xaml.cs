using ImageCleanup.App.Services;
using ImageCleanup.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ImageCleanup.App.Views;

/// <summary>Organization preview — TreeView over OrganizationPlanner's proposed hierarchy. No move/commit logic yet.</summary>
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
}
