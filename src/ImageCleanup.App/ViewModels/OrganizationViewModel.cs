using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.Services;
using ImageCleanup.Core.Organization;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Organization feature. Reads ScanSessionService's scanned files, runs
/// OrganizationPlanner.BuildHierarchy off the UI thread (a real library can
/// be thousands of files), and exposes the result as a tree of
/// OrganizationNodeViewModel for a TreeView to bind to. v1: ExecutePlanAsync
/// moves every file in the plan — no per-file selection/opt-out yet (a
/// future enhancement).
/// </summary>
public sealed class OrganizationViewModel : INotifyPropertyChanged
{
    private readonly ScanSessionService _scanSession;
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly OrganizationExecutor _executor = new();

    /// <summary>Populated per rebuild so thumbnail requests can look up a file's LastModified for cache keying.</summary>
    private readonly Dictionary<string, DateTime> _lastModifiedByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The raw Core plan behind the currently displayed tree — kept so ExecutePlanAsync has something to execute.</summary>
    private OrganizationPlan? _currentPlan;

    /// <summary>True once the user has explicitly picked a destination — stops auto-defaulting it on every rescan.</summary>
    private bool _destinationManuallySet;

    private string _statusText = "Select a folder above to preview an organization plan.";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    private bool _isIdle = true;
    /// <summary>True when not currently computing a plan or executing moves (gates the Execute button).</summary>
    public bool IsIdle
    {
        get => _isIdle;
        private set { _isIdle = value; Notify(); Notify(nameof(CanExecutePlan)); }
    }

    private string? _destinationFolder;
    public string? DestinationFolder
    {
        get => _destinationFolder;
        private set { _destinationFolder = value; Notify(); Notify(nameof(CanExecutePlan)); }
    }

    private int _plannedFileCount;

    /// <summary>Gates the Execute button: idle, a non-empty plan, and a chosen destination.</summary>
    public bool CanExecutePlan => IsIdle && _plannedFileCount > 0 && !string.IsNullOrEmpty(DestinationFolder);

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

    /// <summary>Called from the Page when the user picks a destination folder via FolderPicker.</summary>
    public void SetDestinationFolder(string folderPath)
    {
        _destinationManuallySet = true;
        DestinationFolder = folderPath;
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

            var (plan, tree) = await Task.Run(() =>
            {
                var plan = OrganizationPlanner.BuildHierarchy(recordsSnapshot.Select(r => r.ToImageRecord()));
                return (plan, OrganizationTreeBuilder.BuildTree(plan));
            });

            _currentPlan      = plan;
            _plannedFileCount = plan.FileCount;
            Notify(nameof(CanExecutePlan));

            // Default the destination to the scanned source folder, but only
            // until the user explicitly picks their own — don't clobber that
            // choice on every rescan.
            if (!_destinationManuallySet && _scanSession.CurrentFolder is not null)
                DestinationFolder = _scanSession.CurrentFolder;

            RootNodes.Clear();
            foreach (var node in tree)
                RootNodes.Add(new OrganizationNodeViewModel(node));

            var monthCount = tree.Sum(year => year.Children.Count);
            StatusText = plan.FileCount == 0
                ? "No files to organize yet — scan a folder first."
                : $"{plan.FileCount} file(s) across {monthCount} month(s).";
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

    /// <summary>
    /// Moves every file in the current plan to DestinationFolder. v1 has no
    /// per-file selection — the whole plan or nothing. Caller (the Page) is
    /// responsible for confirming with the user first; this just executes.
    /// </summary>
    public async Task<OrganizationExecutionResult> ExecutePlanAsync()
    {
        if (_currentPlan is null || DestinationFolder is null)
            return new OrganizationExecutionResult();

        IsIdle = false;
        StatusText = "Moving files…";
        try
        {
            var plan = _currentPlan;
            var destination = DestinationFolder;

            var result = await Task.Run(() => _executor.Execute(plan, destination));

            // Files have moved — refresh the shared scan session so every
            // page (including this one, via ScanCompleted) reflects the new
            // disk state. Files moved outside the originally-scanned folder
            // simply won't reappear in Records on the next scan, same as any
            // other file that's no longer under CurrentFolder.
            await _scanSession.RefreshAsync();

            StatusText = result.Failed == 0
                ? $"Done — {result.Succeeded} file(s) moved. Move log: {result.MoveLogPath}"
                : $"Done — {result.Succeeded} succeeded, {result.Failed} failed. Move log: {result.MoveLogPath}";

            return result;
        }
        catch (Exception ex)
        {
            StatusText = $"Move failed: {ex.Message}";
            return new OrganizationExecutionResult();
        }
        finally
        {
            IsIdle = true;
        }
    }

    private DateTime GetLastModified(string filePath) =>
        _lastModifiedByPath.TryGetValue(filePath, out var lm) ? lm : File.GetLastWriteTimeUtc(filePath);

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
