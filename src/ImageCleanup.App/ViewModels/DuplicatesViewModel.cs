using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.Services;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Services;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Duplicates feature — reads its file list from the shared ScanSessionService
/// (rather than scanning itself) and rebuilds duplicate groups/staging
/// whenever a new scan completes.
/// </summary>
public sealed class DuplicatesViewModel : INotifyPropertyChanged
{
    private readonly ScanSessionService _scanSession;
    private readonly OrganizationStagingRepository _stagingRepo;
    private readonly ThumbnailCache _thumbnailCache = new();

    /// <summary>Populated per rebuild so thumbnail requests can look up a file's LastModified for cache keying.</summary>
    private readonly Dictionary<string, DateTime> _lastModifiedByPath = new(StringComparer.OrdinalIgnoreCase);

    // ── Observable state ─────────────────────────────────────────────────────

    private string _statusText = LocalizationService.Current.GetString("Duplicates.InitialStatus");
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    private bool _isIdle = true;
    /// <summary>True when not currently committing (gates the Commit button).</summary>
    public bool IsIdle
    {
        get => _isIdle;
        private set { _isIdle = value; Notify(); }
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];
    public ObservableCollection<StagingEntryViewModel> StagedItems { get; } = [];

    public bool HasStagedItems => StagedItems.Count > 0;
    public Visibility StagingPanelVisibility => StagedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string StagedCountText => LocalizationService.Current.GetString("Common.StagedCountText", StagedItems.Count);

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public DuplicatesViewModel(ScanSessionService scanSession)
    {
        _scanSession = scanSession;
        _stagingRepo = new OrganizationStagingRepository(scanSession.ConnectionString);

        _scanSession.ScanCompleted += (_, _) => RebuildFromRecords();

        // If a scan already happened before this page/ViewModel was created
        // (e.g. navigating back to Duplicates), rebuild immediately instead
        // of waiting for another ScanCompleted event.
        if (_scanSession.Records.Count > 0) RebuildFromRecords();
    }

    // ── Rebuild from the shared scan session ────────────────────────────────

    private void RebuildFromRecords()
    {
        Groups.Clear();
        StagedItems.Clear();
        _stagingRepo.ClearStaged();

        var scanned  = _scanSession.Records;
        var pathToId = scanned.ToDictionary(r => r.FilePath, r => r.Id, StringComparer.OrdinalIgnoreCase);

        _lastModifiedByPath.Clear();
        foreach (var r in scanned)
            _lastModifiedByPath[r.FilePath] = r.LastModified;

        var imageRecords = scanned.Select(ImageRecordMapper.ToImageRecord);
        var dupGroups    = SuggestionEngine.GroupDuplicates(imageRecords);

        foreach (var g in dupGroups)
        {
            var vm = new DuplicateGroupViewModel(g, pathToId);
            foreach (var fa in vm.FileActions)
            {
                fa.ActionChanged = OnFileActionChanged;
                RequestThumbnail(fa);
                if (!fa.IsSuggested)
                {
                    var sid = _stagingRepo.StageAction(fa.FileRecordId, ActionType.Delete.ToStagingValue(), null, "Duplicate detected");
                    fa.StagingId = sid;
                    var staged = new StagingEntryViewModel(sid, fa.FilePath, ActionType.Delete);
                    RequestThumbnail(staged);
                    StagedItems.Add(staged);
                }
            }
            Groups.Add(vm);
        }

        NotifyStagingPanel();
        StatusText = scanned.Count == 0
            ? LocalizationService.Current.GetString("Duplicates.NoFilesFound")
            : LocalizationService.Current.GetString("Duplicates.GroupsFound", dupGroups.Count, scanned.Count);
    }

    // ── Staging callbacks ─────────────────────────────────────────────────────

    private void OnFileActionChanged(FileActionViewModel fa)
    {
        var group = Groups.FirstOrDefault(g => g.FileActions.Contains(fa));

        // Only one file per group may be Keep at a time — bump any other
        // current keep-file back to Delete. This recurses into
        // OnFileActionChanged for that file (harmless: it resolves to
        // Delete, not Keep, so it can't cascade further).
        if (group is not null && fa.SelectedActionType == ActionType.Keep)
        {
            var conflicts = KeepSelector.ResolveKeepConflicts(
                group.FileActions.Select(f => (f.FilePath, f.SelectedActionType)),
                fa.FilePath);

            foreach (var path in conflicts)
            {
                var other = group.FileActions.FirstOrDefault(f =>
                    string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (other is not null) other.SelectedActionType = ActionType.Delete;
            }
        }

        // Remove old staging row
        if (fa.StagingId.HasValue)
        {
            _stagingRepo.RemoveStagingEntry(fa.StagingId.Value);
            var old = StagedItems.FirstOrDefault(s => s.StagingId == fa.StagingId.Value);
            if (old is not null) StagedItems.Remove(old);
            fa.StagingId = null;
        }

        // Create a staging row only for actionable choices — Keep and None
        // both mean "do nothing to this file" and have no staged action.
        if (fa.SelectedActionType is ActionType.Delete or ActionType.Move)
        {
            var sid = _stagingRepo.StageAction(fa.FileRecordId, fa.SelectedActionType.ToStagingValue(), null, "User staged");
            fa.StagingId = sid;
            var staged = new StagingEntryViewModel(sid, fa.FilePath, fa.SelectedActionType);
            RequestThumbnail(staged);
            StagedItems.Add(staged);
        }

        group?.NotifyKeepChanged();
        NotifyStagingPanel();
    }

    /// <summary>Remove a staging entry from both the DB and the panel (e.g. user clicks "Remove").</summary>
    public void RemoveStagingEntry(int stagingId)
    {
        _stagingRepo.RemoveStagingEntry(stagingId);

        var panelEntry = StagedItems.FirstOrDefault(s => s.StagingId == stagingId);
        if (panelEntry is not null) StagedItems.Remove(panelEntry);

        // Reset the corresponding FileActionViewModel so the ComboBox shows "None"
        var fa = Groups.SelectMany(g => g.FileActions)
                       .FirstOrDefault(f => f.StagingId == stagingId);
        fa?.ResetToNone();

        NotifyStagingPanel();
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sync Move target paths from the UI into the DB, then run CommitService.
    /// Returns the result so the caller can show a summary dialog.
    /// </summary>
    public async Task<CommitResult> CommitStagedChangesAsync()
    {
        IsIdle = false;
        var loc = LocalizationService.Current;
        StatusText = loc.GetString("Common.CommittingStatus");
        try
        {
            // Flush Move target paths from TextBoxes into staging rows
            foreach (var fa in Groups.SelectMany(g => g.FileActions))
            {
                if (fa.SelectedActionType == ActionType.Move && fa.StagingId.HasValue)
                    _stagingRepo.UpdateTargetPath(fa.StagingId.Value, fa.TargetPath);
            }

            var result = await Task.Run(() =>
                new CommitService(_scanSession.ConnectionString, RecycleBinDelete).ExecutePendingActions());

            // Files are gone/moved — refresh the shared scan session so every
            // page (including this one, via ScanCompleted) reflects disk state.
            await _scanSession.RefreshAsync();

            StatusText = result.Failed == 0
                ? loc.GetString("Common.CommitDoneAll", result.Succeeded)
                : loc.GetString("Common.CommitDonePartial", result.Succeeded, result.Failed);
            return result;
        }
        catch (Exception ex)
        {
            StatusText = loc.GetString("Common.CommitFailedStatus", ex.Message);
            return new CommitResult();
        }
        finally
        {
            IsIdle = true;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private const int DetailThumbnailMaxDimension = 320;

    /// <summary>
    /// Kicks off loading of the larger detail-view thumbnails for a group's files
    /// (a separate ThumbnailCache entry/size from the list view's). Safe to call
    /// every time the detail dialog opens — already-loaded thumbnails are skipped.
    /// </summary>
    public void RequestDetailThumbnails(DuplicateGroupViewModel group)
    {
        foreach (var fa in group.FileActions)
        {
            if (fa.DetailThumbnail is not null) continue;
            fa.RequestDetailThumbnail(() => _thumbnailCache.GetOrCreateThumbnail(
                fa.FilePath, GetLastModified(fa.FilePath), DetailThumbnailMaxDimension));
        }
    }

    private void RequestThumbnail(FileActionViewModel fa) =>
        fa.RequestThumbnail(() => _thumbnailCache.GetOrCreateThumbnail(fa.FilePath, GetLastModified(fa.FilePath)));

    private void RequestThumbnail(StagingEntryViewModel entry) =>
        entry.RequestThumbnail(() => _thumbnailCache.GetOrCreateThumbnail(entry.FilePath, GetLastModified(entry.FilePath)));

    private DateTime GetLastModified(string filePath) =>
        _lastModifiedByPath.TryGetValue(filePath, out var lm) ? lm : File.GetLastWriteTimeUtc(filePath);

    private static void RecycleBinDelete(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    private void NotifyStagingPanel()
    {
        Notify(nameof(HasStagedItems));
        Notify(nameof(StagingPanelVisibility));
        Notify(nameof(StagedCountText));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
