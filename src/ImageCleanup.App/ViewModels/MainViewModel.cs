using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Hashing;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Quality;
using ImageCleanup.Data;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Services;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private readonly string _connectionString;
    private readonly OrganizationStagingRepository _stagingRepo;

    // ── Observable state ─────────────────────────────────────────────────────

    private string _statusText = "Ready — click \"Select Folder\" to start.";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    private bool _isIdle = true;
    public bool IsIdle
    {
        get => _isIdle;
        private set { _isIdle = value; Notify(); }
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];
    public ObservableCollection<StagingEntryViewModel> StagedItems { get; } = [];

    public bool HasStagedItems => StagedItems.Count > 0;
    public Visibility StagingPanelVisibility => StagedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string StagedCountText => $"Review Staged Changes ({StagedItems.Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public MainViewModel()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "cache.db");
        _connectionString = $"Data Source={dbPath}";
        DbInitializer.Initialize(_connectionString);
        _stagingRepo = new OrganizationStagingRepository(_connectionString);
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    public async Task ScanFolderAsync(string folderPath)
    {
        IsIdle = false;
        Groups.Clear();
        StagedItems.Clear();
        _stagingRepo.ClearStaged();
        StatusText = "Scanning…";

        try
        {
            var scanned  = await Task.Run(() => ScanFiles(folderPath));
            var pathToId = scanned.ToDictionary(r => r.FilePath, r => r.Id, StringComparer.OrdinalIgnoreCase);

            var imageRecords = scanned.Select(ToImageRecord);
            var dupGroups    = SuggestionEngine.GroupDuplicates(imageRecords);

            foreach (var g in dupGroups)
            {
                var vm = new DuplicateGroupViewModel(g, pathToId);
                foreach (var fa in vm.FileActions)
                {
                    fa.ActionChanged = OnFileActionChanged;
                    if (!fa.IsSuggested)
                    {
                        var sid = _stagingRepo.StageAction(fa.FileRecordId, "Delete", null, "Duplicate detected");
                        fa.StagingId = sid;
                        StagedItems.Add(new StagingEntryViewModel(sid, fa.FilePath, "Delete"));
                    }
                }
                Groups.Add(vm);
            }

            NotifyStagingPanel();
            StatusText = scanned.Count == 0
                ? "No image files found in that folder."
                : $"{scanned.Count} file(s) scanned — {dupGroups.Count} duplicate group(s) found.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsIdle = true;
        }
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

        // Create new staging row if user picked something actionable
        if (fa.SelectedAction != "None")
        {
            var sid = _stagingRepo.StageAction(fa.FileRecordId, fa.SelectedAction, null, "User staged");
            fa.StagingId = sid;
            StagedItems.Add(new StagingEntryViewModel(sid, fa.FilePath, fa.SelectedAction));
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
        StatusText = "Committing changes…";
        try
        {
            // Flush Move target paths from TextBoxes into staging rows
            foreach (var fa in Groups.SelectMany(g => g.FileActions))
            {
                if (fa.SelectedAction == "Move" && fa.StagingId.HasValue)
                    _stagingRepo.UpdateTargetPath(fa.StagingId.Value, fa.TargetPath);
            }

            var result = await Task.Run(() =>
                new CommitService(_connectionString, RecycleBinDelete).ExecutePendingActions());

            // Clear UI state — files are gone/moved
            Groups.Clear();
            StagedItems.Clear();
            NotifyStagingPanel();
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

    private List<FileRecord> ScanFiles(string folderPath)
    {
        var repo    = new FileCacheRepository(_connectionString);
        var results = new List<FileRecord>();

        var files = Directory
            .EnumerateFiles(folderPath)
            .Where(p => ImageExtensions.Contains(
                Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var path in files)
        {
            try
            {
                var fi           = new FileInfo(path);
                var lastModified = fi.LastWriteTimeUtc;

                if (!repo.NeedsRescan(path, fi.Length, lastModified))
                {
                    var cached = repo.GetByPath(path);
                    if (cached is not null) { results.Add(cached); continue; }
                }

                var fileHash = ComputeSha256(path);
                var meta     = ExifReader.ReadMetadata(path);

                ulong? perceptualHash = null;
                double? blurScore    = null;
                bool?   isLowDetail  = null;
                try
                {
                    perceptualHash = DHasher.ComputeFromFile(path);
                    blurScore      = BlurDetector.ComputeBlurScore(path);
                    isLowDetail    = LowDetailDetector.IsLowDetail(path);
                }
                catch { /* corrupt or unsupported image */ }

                var record = new FileRecord
                {
                    FilePath       = path,
                    FileHash       = fileHash,
                    PerceptualHash = perceptualHash,
                    FileSize       = fi.Length,
                    LastModified   = lastModified,
                    Width          = meta.Width,
                    Height         = meta.Height,
                    BlurScore      = blurScore,
                    DateTaken      = meta.DateTaken,
                    CameraModel    = meta.CameraModel,
                    IsScreenshot   = meta.Width.HasValue && meta.Height.HasValue
                        ? ScreenshotHeuristic.IsLikelyScreenshot(meta, meta.Width.Value, meta.Height.Value)
                        : null,
                    LowDetail      = isLowDetail,
                };

                repo.Upsert(record);
                results.Add(record);
            }
            catch { /* permission error or I/O failure */ }
        }

        return results;
    }

    private static string ComputeSha256(string path)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static ImageRecord ToImageRecord(FileRecord r) => new()
    {
        FilePath       = r.FilePath,
        FileHash       = r.FileHash,
        PerceptualHash = r.PerceptualHash,
        FileSize       = r.FileSize,
        LastModified   = r.LastModified,
        Width          = r.Width,
        Height         = r.Height,
        BlurScore      = r.BlurScore,
        LowDetail      = r.LowDetail,
    };

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
