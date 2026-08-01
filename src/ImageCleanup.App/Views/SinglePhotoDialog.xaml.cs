using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.Views;

/// <summary>
/// Shows one photo at a larger size with its file path — the single-file
/// counterpart to GroupDetailDialog's multi-file comparison grid. See the
/// XAML for why this takes a plain path + byte-generating delegate rather
/// than binding to a specific caller's ViewModel type.
/// </summary>
public sealed partial class SinglePhotoDialog : ContentDialog, INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public string FilePath { get; }

    private ImageSource? _photo;
    public ImageSource? Photo
    {
        get => _photo;
        private set { _photo = value; Notify(); }
    }

    public SinglePhotoDialog(string filePath, Func<byte[]?> generateBytes)
    {
        FilePath = filePath;
        this.InitializeComponent();
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => Photo = bitmap);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
