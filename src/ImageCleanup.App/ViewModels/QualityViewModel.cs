using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.App.Services;
using ImageCleanup.Core.Quality;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Services;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Quality feature — a flat, blurriest-first review list over every scanned
/// file with a BlurScore. Staged through QualityStagingRepository, a table
/// fully separate from Duplicates' OrganizationStaging, so the two features'
/// review/commit flows can never cross-contaminate.
/// </summary>
public sealed class QualityViewModel : INotifyPropertyChanged
{
    private readonly ScanSessionService _scanSession;
    private readonly QualityStagingRepository _stagingRepo;
    private readonly ThumbnailCache _thumbnailCache = new();

    /// <summary>Populated per rebuild so thumbnail requests can look up a file's LastModified for cache keying.</summary>
    private readonly Dictionary<string, DateTime> _lastModifiedByPath = new(StringComparer.OrdinalIgnoreCase);

    // ── Observable state ─────────────────────────────────────────────────────

    private string _statusText = "Select a folder above to review image quality.";
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

    public ObservableCollection<FileActionViewModel> Files { get; } = [];
    public ObservableCollection<StagingEntryViewModel> StagedItems { get; } = [];

    public bool HasStagedItems => StagedItems.Count > 0;
    public Visibility StagingPanelVisibility => StagedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string StagedCountText => $"Review Staged Changes ({StagedItems.Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public QualityViewModel(ScanSessionService scanSession)
    {
        _scanSession = scanSession;
        _stagingRepo = new QualityStagingRepository(scanSession.ConnectionString);

        _scanSession.ScanCompleted += (_, _) => RebuildFromRecords();

        // If a scan already happened before this page/ViewModel was created
        // (e.g. navigating back to Quality), rebuild immediately instead of
        // waiting for another ScanCompleted event.
        if (_scanSession.Records.Count > 0) RebuildFromRecords();
    }

    // ── Rebuild from the shared scan session ────────────────────────────────

    private void RebuildFromRecords()
    {
        Files.Clear();
        StagedItems.Clear();
        _stagingRepo.ClearStaged();

        _lastModifiedByPath.Clear();
        foreach (var r in _scanSession.Records)
            _lastModifiedByPath[r.FilePath] = r.LastModified;

        var sorted = QualityReviewOrder.SortBlurriestFirst(_scanSession.Records, r => r.BlurScore);

        foreach (var r in sorted)
        {
            // Default action is None — nothing pre-staged; the user decides per file.
            var fa = new FileActionViewModel(r.Id, r.FilePath, initialAction: "None", blurScore: r.BlurScore);
            fa.ActionChanged = OnFileActionChanged;
            RequestThumbnail(fa);
            Files.Add(fa);
        }

        StatusText = sorted.Count == 0
            ? "No files with a quality score to review yet."
            : $"{sorted.Count} file(s) with a quality score, sorted blurriest first.";
    }

    // ── Staging callbacks ─────────────────────────────────────────────────────

    private void OnFileActionChanged(FileActionViewModel fa)
    {
        // Remove old staging row
        if (fa.StagingId.HasValue)
        {
            _stagingRepo.RemoveStagingEntry(fa.StagingId.Value);
            var old = StagedItems.FirstOrDefault(s => s.StagingId == fa.StagingId.Value);
            if (old is not null) StagedItems.Remove(old);
            fa.StagingId = null;
        }

        // Create a staging row only for actionable choices — "Keep" and "None"
        // both mean "do nothing to this file" and have no staged action.
        if (fa.SelectedAction is "Delete" or "Move")
        {
            var sid = _stagingRepo.StageAction(fa.FileRecordId, fa.SelectedAction, null, "Flagged in Quality review");
            fa.StagingId = sid;
            var staged = new StagingEntryViewModel(sid, fa.FilePath, fa.SelectedAction);
            RequestThumbnail(staged);
            StagedItems.Add(staged);
        }

        NotifyStagingPanel();
    }

    /// <summary>Remove a staging entry from both the DB and the panel (e.g. user clicks "Remove").</summary>
    public void RemoveStagingEntry(int stagingId)
    {
        _stagingRepo.RemoveStagingEntry(stagingId);

        var panelEntry = StagedItems.FirstOrDefault(s => s.StagingId == stagingId);
        if (panelEntry is not null) StagedItems.Remove(panelEntry);

        // Reset the corresponding FileActionViewModel so the ComboBox shows "None"
        var fa = Files.FirstOrDefault(f => f.StagingId == stagingId);
        fa?.ResetToNone();

        NotifyStagingPanel();
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sync Move target paths from the UI into the DB, then run CommitService
    /// against QualityStagingRepository — independent from Duplicates' commit.
    /// Returns the result so the caller can show a summary dialog.
    /// </summary>
    public async Task<CommitResult> CommitStagedChangesAsync()
    {
        IsIdle = false;
        StatusText = "Committing changes…";
        try
        {
            // Flush Move target paths from TextBoxes into staging rows
            foreach (var fa in Files)
            {
                if (fa.SelectedAction == "Move" && fa.StagingId.HasValue)
                    _stagingRepo.UpdateTargetPath(fa.StagingId.Value, fa.TargetPath);
            }

            var result = await Task.Run(() =>
                new CommitService(_scanSession.ConnectionString, _stagingRepo, RecycleBinDelete).ExecutePendingActions());

            // Files are gone/moved — refresh the shared scan session so every
            // page (including Duplicates) reflects the new disk state.
            await _scanSession.RefreshAsync();

            StatusText = result.Failed == 0
                ? $"Done — {result.Succeeded} file(s) processed."
                : $"Done — {result.Succeeded} succeeded, {result.Failed} failed.";
            return result;
        }
        catch (Exception ex)
        {
            StatusText = $"Commit failed: {ex.Message}";
            return new CommitResult();
        }
        finally
        {
            IsIdle = true;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
