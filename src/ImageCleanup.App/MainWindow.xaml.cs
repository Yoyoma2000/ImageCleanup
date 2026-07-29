using ImageCleanup.App.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace ImageCleanup.App;

public sealed partial class MainWindow : Window
{
    // Exposed as a property so x:Bind in the XAML can reach it at compile time.
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        this.InitializeComponent();
    }

    private async void OnSelectFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();

        // Unpackaged WinUI 3 apps must initialise the picker with the window HWND.
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await ViewModel.ScanFolderAsync(folder.Path);
    }
}
