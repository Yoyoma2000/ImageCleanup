using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.Services;
using ImageCleanup.Core.Organization;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Organization feature — preview-only for now. Reads ScanSessionService's
/// scanned files, runs OrganizationPlanner.BuildHierarchy off the UI thread
/// (a real library can be thousands of files), and exposes the result as a
/// tree of OrganizationNodeViewModel for a TreeView to bind to. No staging,
/// no commit, nothing moves a file yet.
/// </summary>
public sealed class OrganizationViewModel : INotifyPropertyChanged
{
    private readonly ScanSessionService _scanSession;
    private readonly ThumbnailCache _thumbnailCache = new();

    /// <summary>Populated per rebuild so thumbnail requests can look up a file's LastModified for cache keying.</summary>
    private readonly Dictionary<string, DateTime> _lastModifiedByPath = new(StringComparer.OrdinalIgnoreCase);

    private string _statusText = "Select a folder above to preview an organization plan.";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    public ObservableCollection<OrganizationNodeViewModel> RootNodes { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public OrganizationViewModel(ScanSessionService scanSession)
    {
        _scanSession = scanSession;

        // Unlike Duplicates/Quality (which rebuild synchronously on
        // ScanCompleted), this fires an async rebuild: BuildHierarchy runs
        // off the UI thread deliberately, since a real library can be
        // thousands of files and this must not hang the UI.
        _scanSession.ScanCompleted += (_, _) => _ = RebuildAsync();

        if (_scanSession.Records.Count > 0) _ = RebuildAsync();
    }

    private async Task RebuildAsync()
    {
        StatusText = "Computing organization plan…";

        try
        {
            // Snapshot on the calling thread before handing off — cheap
            // (reference copy), and avoids reading the ObservableCollection
            // concurrently with any UI-thread mutation.
            var recordsSnapshot = _scanSession.Records.ToList();

            _lastModifiedByPath.Clear();
            foreach (var r in recordsSnapshot)
                _lastModifiedByPath[r.FilePath] = r.LastModified;

            var tree = await Task.Run(() =>
            {
                var plan = OrganizationPlanner.BuildHierarchy(recordsSnapshot.Select(r => r.ToImageRecord()));
                return OrganizationTreeBuilder.BuildTree(plan);
            });

            RootNodes.Clear();
            foreach (var node in tree)
                RootNodes.Add(new OrganizationNodeViewModel(node));

            var fileCount  = recordsSnapshot.Count;
            var monthCount = tree.Sum(year => year.Children.Count);

            StatusText = fileCount == 0
                ? "No files to organize yet — scan a folder first."
                : $"{fileCount} file(s) across {monthCount} month(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't compute an organization plan: {ex.Message}";
        }
    }

    /// <summary>
    /// Lazily requests thumbnails for a Category node's File children — called
    /// from the TreeView's Expanding event rather than eagerly for every file
    /// when the plan builds, since thumbnail generation isn't free and a real
    /// library can have thousands of files across many categories the user
    /// may never expand.
    /// </summary>
    public void RequestThumbnailsFor(OrganizationNodeViewModel node)
    {
        if (node.Kind != OrganizationNodeKind.Category || node.ThumbnailsRequested)
            return;

        node.ThumbnailsRequested = true;
        foreach (var fileNode in node.Children)
        {
            var path = fileNode.SourcePath;
            if (path is null) continue;
            fileNode.RequestThumbnail(() => _thumbnailCache.GetOrCreateThumbnail(path, GetLastModified(path)));
        }
    }

    private DateTime GetLastModified(string filePath) =>
        _lastModifiedByPath.TryGetValue(filePath, out var lm) ? lm : File.GetLastWriteTimeUtc(filePath);

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
