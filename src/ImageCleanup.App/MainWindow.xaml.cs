using ImageCleanup.App.Services;
using ImageCleanup.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ImageCleanup.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Shared across every page via App.Services — folder selection lives here, in the shell.</summary>
    public ScanSessionService ScanSession { get; }

    public MainWindow()
    {
        ScanSession = App.Services.GetRequiredService<ScanSessionService>();

        this.InitializeComponent();

        // Triggers OnNavSelectionChanged, which navigates the Frame to DuplicatesPage.
        Nav.SelectedItem = Nav.MenuItems[0];
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

        await ScanSession.ScanFolderAsync(folder.Path);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type pageType;

        if (args.IsSettingsSelected)
        {
            pageType = typeof(SettingsPage);
        }
        else
        {
            if (args.SelectedItem is not NavigationViewItem item) return;

            pageType = item.Tag switch
            {
                "Duplicates"   => typeof(DuplicatesPage),
                "Quality"      => typeof(QualityPage),
                "Organization" => typeof(OrganizationPage),
                _              => typeof(DuplicatesPage),
            };
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }
}
