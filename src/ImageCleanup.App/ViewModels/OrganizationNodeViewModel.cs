using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Core.Organization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Thin WinUI-aware wrapper around a pure Core.Organization.OrganizationTreeNode
/// — adds thumbnail loading, XAML-friendly Visibility properties, and a
/// tri-state IsChecked for the selective-execution CheckBox, but the actual
/// label/count/rename/cascading-selection logic lives in Core
/// (OrganizationTreeBuilder / OrganizationSelectionNode) where it's
/// unit-testable without WinUI.
/// </summary>
public sealed class OrganizationNodeViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly OrganizationSelectionNode _selection;
    private readonly Action? _onSelectionChanged;

    public OrganizationNodeKind Kind { get; }
    public string DisplayText { get; }
    public string? SourcePath { get; }
    public string? TargetFileName { get; }
    public bool WasRenamed { get; }

    /// <summary>Set at construction time by the parent — used to walk upward and re-notify ancestors' IsChecked when a descendant's selection changes.</summary>
    public OrganizationNodeViewModel? Parent { get; private set; }
    public IReadOnlyList<OrganizationNodeViewModel> Children { get; }

    public Visibility ThumbnailVisibility => Kind == OrganizationNodeKind.File ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RenamedBadgeVisibility => WasRenamed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TargetNameVisibility => WasRenamed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Tri-state value for a ThreeState CheckBox — null renders as
    /// indeterminate. Purely a read of the underlying OrganizationSelectionNode;
    /// see <see cref="SetSelected"/> for how it changes.
    /// </summary>
    public bool? IsChecked => _selection.CheckBoxState;

    /// <summary>Set once thumbnails have been requested for this node's File children (Category nodes only) — avoids re-requesting on repeat expand/collapse.</summary>
    internal bool ThumbnailsRequested { get; set; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set { _thumbnail = value; Notify(); }
    }

    public OrganizationNodeViewModel(
        OrganizationTreeNode node,
        OrganizationSelectionNode selection,
        Action? onSelectionChanged = null,
        OrganizationNodeViewModel? parent = null)
    {
        Kind                = node.Kind;
        DisplayText         = node.DisplayText;
        SourcePath          = node.SourcePath;
        TargetFileName      = node.TargetFileName;
        WasRenamed          = node.WasRenamed;
        Parent              = parent;
        _selection          = selection;
        _onSelectionChanged = onSelectionChanged;

        Children = node.Children
            .Zip(selection.Children, (childNode, childSelection) =>
                new OrganizationNodeViewModel(childNode, childSelection, onSelectionChanged, this))
            .ToList();
    }

    /// <summary>
    /// Applies a checked/unchecked value to this node — cascades to every
    /// descendant if this is a group node (Year/Month/Category), or just
    /// this file if it's a File node (see OrganizationSelectionNode). Never
    /// forces an ancestor's own stored state (there isn't one — ancestors
    /// derive IsChecked live), but does re-raise PropertyChanged up the
    /// Parent chain and down through Children so every visible CheckBox in
    /// the tree reflects the change immediately.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _selection.SetSelected(selected);
        RefreshCheckedStateRecursively();
        NotifyAncestorsCheckedStateChanged();
        _onSelectionChanged?.Invoke();
    }

    private void RefreshCheckedStateRecursively()
    {
        Notify(nameof(IsChecked));
        foreach (var child in Children)
            child.RefreshCheckedStateRecursively();
    }

    private void NotifyAncestorsCheckedStateChanged()
    {
        var current = Parent;
        while (current is not null)
        {
            current.Notify(nameof(IsChecked));
            current = current.Parent;
        }
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
