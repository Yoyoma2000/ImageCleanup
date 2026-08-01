using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.Services;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Organization;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Organization feature. Reads ScanSessionService's scanned files, runs
/// OrganizationPlanner.BuildHierarchy off the UI thread (a real library can
/// be thousands of files), and exposes the result as a tree of
/// OrganizationNodeViewModel for a TreeView to bind to. Each node carries a
/// checkable selection (default: everything checked, preserving the
/// original "organize everything" behavior) via Core.Organization.
/// OrganizationSelectionNode — ExecutePlanAsync only moves the files
/// currently selected, not necessarily the whole plan.
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

    /// <summary>One selection tree per root, mirroring RootNodes 1:1 — kept separately from the ViewModel tree so selection state can be queried (GetSelectedSourcePaths) without walking WinUI-aware objects.</summary>
    private List<OrganizationSelectionNode> _selectionRoots = [];

    /// <summary>True once the user has explicitly picked a destination — stops auto-defaulting it on every rescan.</summary>
    private bool _destinationManuallySet;

    private string _statusText = LocalizationService.Current.GetString("Organization.InitialStatus");
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

    /// <summary>Total files in the current plan, regardless of selection — for "N of M" display.</summary>
    public int PlannedFileCount => _plannedFileCount;

    /// <summary>Files currently checked for execution across the whole tree.</summary>
    public int SelectedFileCount => _selectionRoots.SelectMany(r => r.SelectedFileNodes()).Count();

    /// <summary>Gates the Execute button: idle, at least one selected file, and a chosen destination.</summary>
    public bool CanExecutePlan => IsIdle && SelectedFileCount > 0 && !string.IsNullOrEmpty(DestinationFolder);

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
        var loc = LocalizationService.Current;
        StatusText = loc.GetString("Organization.ComputingStatus");

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
                var plan = OrganizationPlanner.BuildHierarchy(
                    recordsSnapshot.Select(r => r.ToImageRecord()),
                    ResolveCategoryFolderName);
                return (plan, OrganizationTreeBuilder.BuildTree(plan));
            });

            _currentPlan      = plan;
            _plannedFileCount = plan.FileCount;

            // A fresh plan means a fresh selection tree too — everything
            // defaults to selected (OrganizationSelectionNode's default),
            // matching the original "organize everything" behavior for
            // anyone who doesn't touch the checkboxes.
            _selectionRoots = tree.Select(t => new OrganizationSelectionNode(t)).ToList();
            Notify(nameof(SelectedFileCount));
            Notify(nameof(PlannedFileCount));
            Notify(nameof(CanExecutePlan));

            // Default the destination to the scanned source folder, but only
            // until the user explicitly picks their own — don't clobber that
            // choice on every rescan.
            if (!_destinationManuallySet && _scanSession.CurrentFolder is not null)
                DestinationFolder = _scanSession.CurrentFolder;

            var onSelectionChanged = () =>
            {
                Notify(nameof(SelectedFileCount));
                Notify(nameof(CanExecutePlan));
            };

            RootNodes.Clear();
            foreach (var (node, selection) in tree.Zip(_selectionRoots))
                RootNodes.Add(new OrganizationNodeViewModel(node, selection, onSelectionChanged));

            var monthCount = tree.Sum(year => year.Children.Count);
            StatusText = plan.FileCount == 0
                ? loc.GetString("Organization.NoFilesToOrganize")
                : loc.GetString("Organization.PlanSummary", plan.FileCount, monthCount);
        }
        catch (Exception ex)
        {
            StatusText = loc.GetString("Organization.PlanFailed", ex.Message);
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

    private const int DetailThumbnailMaxDimension = 320;

    /// <summary>
    /// Returns a byte-generating delegate for SinglePhotoDialog's "View
    /// Photo" on a File-kind tree node — same 320px size/ThumbnailCache-entry
    /// convention Duplicates'/Quality's own detail views use, kept as its
    /// own cache key separate from the tree row's default-size Thumbnail.
    /// </summary>
    public Func<byte[]?> GetDetailThumbnailProvider(string filePath) =>
        () => _thumbnailCache.GetOrCreateThumbnail(filePath, GetLastModified(filePath), DetailThumbnailMaxDimension);

    /// <summary>
    /// Moves only the currently-selected/checked files in the plan to
    /// DestinationFolder (default: every node starts checked, so this moves
    /// everything unless the user has deselected something). Caller (the
    /// Page) is responsible for confirming with the user first — including
    /// showing the actual selected-vs-total count via SelectedFileCount/
    /// PlannedFileCount — this just executes.
    /// </summary>
    public async Task<OrganizationExecutionResult> ExecutePlanAsync()
    {
        if (_currentPlan is null || DestinationFolder is null)
            return new OrganizationExecutionResult();

        var selectedSourcePaths = GetSelectedSourcePaths();
        if (selectedSourcePaths.Count == 0)
            return new OrganizationExecutionResult();

        IsIdle = false;
        var loc = LocalizationService.Current;
        StatusText = loc.GetString("Organization.MovingStatus");
        try
        {
            var plan = _currentPlan;
            var destination = DestinationFolder;

            var result = await Task.Run(() => _executor.Execute(plan, destination, selectedSourcePaths));

            // Files have moved — refresh the shared scan session so every
            // page (including this one, via ScanCompleted) reflects the new
            // disk state. Files moved outside the originally-scanned folder
            // simply won't reappear in Records on the next scan, same as any
            // other file that's no longer under CurrentFolder.
            await _scanSession.RefreshAsync();

            StatusText = result.Failed == 0
                ? loc.GetString("Organization.MoveDoneAll", result.Succeeded, result.MoveLogPath)
                : loc.GetString("Organization.MoveDonePartial", result.Succeeded, result.Failed, result.MoveLogPath);

            return result;
        }
        catch (Exception ex)
        {
            StatusText = loc.GetString("Organization.MoveFailedStatus", ex.Message);
            return new OrganizationExecutionResult();
        }
        finally
        {
            IsIdle = true;
        }
    }

    /// <summary>Lists past move logs (newest first) so the Page can show a picker for Undo.</summary>
    public async Task<IReadOnlyList<MoveLogSummary>> GetAvailableMoveLogsAsync() =>
        await Task.Run(() => OrganizationUndoService.ListMoveLogs());

    /// <summary>
    /// Reverses a previously-executed move batch by reading the move log at
    /// <paramref name="moveLogPath"/> — see OrganizationUndoService for the
    /// per-entry validation (never overwrites, safe to re-run against an
    /// already-partially-undone log). Caller (the Page) is responsible for
    /// confirming with the user first, same as ExecutePlanAsync.
    /// </summary>
    public async Task<OrganizationUndoResult> UndoMoveLogAsync(string moveLogPath)
    {
        IsIdle = false;
        var loc = LocalizationService.Current;
        StatusText = loc.GetString("Organization.UndoingStatus");
        try
        {
            var result = await Task.Run(() => OrganizationUndoService.Undo(moveLogPath));

            // Files may have moved back to CurrentFolder (or elsewhere) —
            // refresh the shared scan session so every page reflects the
            // restored disk state, same as ExecutePlanAsync does after a move.
            await _scanSession.RefreshAsync();

            StatusText = loc.GetString("Organization.UndoDoneStatus", result.Summary);
            return result;
        }
        catch (Exception ex)
        {
            StatusText = loc.GetString("Organization.UndoFailedStatus", ex.Message);
            return new OrganizationUndoResult();
        }
        finally
        {
            IsIdle = true;
        }
    }

    /// <summary>
    /// Resolves a MetadataCategory to the folder name actually used on disk
    /// (and shown in the tree) — reads through LocalizationService so
    /// Organization.FolderName.Photo/NoMetadata respect the active
    /// language, falling back to Dev's "Photo"/"NoMetadata" for any
    /// language/key not yet translated (LocalizationService's own
    /// fallback), so Dev-language behavior is provably unchanged from
    /// before this resolver existed.
    /// </summary>
    private static string ResolveCategoryFolderName(MetadataCategory category) => category switch
    {
        MetadataCategory.Photo      => LocalizationService.Current.GetString("Organization.FolderName.Photo"),
        MetadataCategory.NoMetadata => LocalizationService.Current.GetString("Organization.FolderName.NoMetadata"),
        _                           => category.ToString(),
    };

    private HashSet<string> GetSelectedSourcePaths() =>
        _selectionRoots
            .SelectMany(r => r.SelectedFileNodes())
            .Select(n => n.SourcePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private DateTime GetLastModified(string filePath) =>
        _lastModifiedByPath.TryGetValue(filePath, out var lm) ? lm : File.GetLastWriteTimeUtc(filePath);

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
