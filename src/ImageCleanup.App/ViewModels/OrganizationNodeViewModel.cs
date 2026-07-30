using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Core.Organization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Thin WinUI-aware wrapper around a pure Core.Organization.OrganizationTreeNode
/// — adds thumbnail loading and XAML-friendly Visibility properties, but the
/// actual label/count/rename logic lives in Core (OrganizationTreeBuilder)
/// where it's unit-testable without WinUI.
/// </summary>
public sealed class OrganizationNodeViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public OrganizationNodeKind Kind { get; }
    public string DisplayText { get; }
    public string? SourcePath { get; }
    public string? TargetFileName { get; }
    public bool WasRenamed { get; }

    public IReadOnlyList<OrganizationNodeViewModel> Children { get; }

    public Visibility ThumbnailVisibility => Kind == OrganizationNodeKind.File ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RenamedBadgeVisibility => WasRenamed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TargetNameVisibility => WasRenamed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Set once thumbnails have been requested for this node's File children (Category nodes only) — avoids re-requesting on repeat expand/collapse.</summary>
    internal bool ThumbnailsRequested { get; set; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set { _thumbnail = value; Notify(); }
    }

    public OrganizationNodeViewModel(OrganizationTreeNode node)
    {
        Kind           = node.Kind;
        DisplayText    = node.DisplayText;
        SourcePath     = node.SourcePath;
        TargetFileName = node.TargetFileName;
        WasRenamed     = node.WasRenamed;
        Children       = node.Children.Select(c => new OrganizationNodeViewModel(c)).ToList();
    }

    /// <summary>
    /// Kicks off async thumbnail generation for a File-kind node. The bytes
    /// are produced by <paramref name="generateBytes"/> off the UI thread
    /// (e.g. via ThumbnailCache); Thumbnail populates once decoding completes.
    /// </summary>
    public void RequestThumbnail(Func<byte[]?> generateBytes) =>
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => Thumbnail = bitmap);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
