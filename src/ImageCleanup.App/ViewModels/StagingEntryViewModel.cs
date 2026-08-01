using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Core.Grouping;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.ViewModels;

public sealed class StagingEntryViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public int StagingId { get; }
    public string FilePath { get; }
    public string DisplayText { get; }

    /// <summary>Localized display text (e.g. "Delete" under Dev) — resolved once at construction, not the stable value; nothing here compares this.</summary>
    public string Action { get; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set { _thumbnail = value; Notify(); }
    }

    public StagingEntryViewModel(int stagingId, string filePath, ActionType action)
    {
        StagingId   = stagingId;
        FilePath    = filePath;
        DisplayText = Path.GetFileName(filePath);
        Action      = ActionDisplay.GetDisplayText(action);
    }

    /// <summary>See <see cref="FileActionViewModel.RequestThumbnail"/>.</summary>
    public void RequestThumbnail(Func<byte[]?> generateBytes) =>
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => Thumbnail = bitmap);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
