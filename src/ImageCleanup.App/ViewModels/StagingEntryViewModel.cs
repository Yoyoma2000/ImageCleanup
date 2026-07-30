using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.ViewModels;

public sealed class StagingEntryViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public int StagingId { get; }
    public string FilePath { get; }
    public string DisplayText { get; }
    public string Action { get; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set { _thumbnail = value; Notify(); }
    }

    public StagingEntryViewModel(int stagingId, string filePath, string action)
    {
        StagingId   = stagingId;
        FilePath    = filePath;
        DisplayText = Path.GetFileName(filePath);
        Action      = action;
    }

    /// <summary>See <see cref="FileActionViewModel.RequestThumbnail"/>.</summary>
    public void RequestThumbnail(Func<byte[]?> generateBytes) =>
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => Thumbnail = bitmap);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
