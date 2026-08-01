using ImageCleanup.App.Services;
using ImageCleanup.App.Views;
using ImageCleanup.Data.Services;
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

        // NavigationView's built-in Settings entry (IsSettingsVisible="True")
        // is an implicit item WinUI constructs internally, not a normal
        // NavigationViewItem declared in this XAML — it isn't materialized
        // until the control has applied its template, which isn't
        // guaranteed to have happened yet right after InitializeComponent,
        // so this is set on Loaded rather than here directly.
        Nav.Loaded += (_, _) => ApplySettingsItemLabel();

        // Triggers OnNavSelectionChanged, which navigates the Frame to DuplicatesPage.
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void ApplySettingsItemLabel()
    {
        if (Nav.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Content = LocalizationService.Current.GetString("Nav.Settings");
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
