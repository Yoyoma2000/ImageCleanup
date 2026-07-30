using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ImageCleanup.Core.Hashing;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Quality;
using ImageCleanup.Data;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;

namespace ImageCleanup.App.Services;

/// <summary>
/// Owns the current folder scan — the single source of truth shared by every
/// feature page (Duplicates, Quality, Organization). Registered as a
/// singleton so all pages see the same folder/records without re-scanning.
/// </summary>
public sealed class ScanSessionService : INotifyPropertyChanged
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private readonly string _connectionString;

    /// <summary>Shared SQLite connection string (file cache + staging tables) — reuse this rather than opening a second database.</summary>
    public string ConnectionString => _connectionString;

    /// <summary>The most recently scanned files. Repopulated wholesale on each scan/refresh — see <see cref="ScanCompleted"/>.</summary>
    public ObservableCollection<FileRecord> Records { get; } = [];

    private string? _currentFolder;
    public string? CurrentFolder
    {
        get => _currentFolder;
        private set { _currentFolder = value; Notify(); }
    }

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

    /// <summary>
    /// Fired once after <see cref="Records"/> has been fully repopulated by a
    /// scan or refresh. Pages that need to rebuild derived state (e.g.
    /// duplicate grouping) should subscribe to this rather than reacting to
    /// individual ObservableCollection changes.
    /// </summary>
    public event EventHandler? ScanCompleted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScanSessionService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "cache.db");
        _connectionString = $"Data Source={dbPath}";
        DbInitializer.Initialize(_connectionString);
    }

    /// <summary>Scans a newly selected folder and repopulates Records.</summary>
    public async Task ScanFolderAsync(string folderPath)
    {
        CurrentFolder = folderPath;
        await RefreshAsync();
    }

    /// <summary>
    /// Re-scans <see cref="CurrentFolder"/> and repopulates Records. Call
    /// after a commit changes what's on disk so every page's derived state
    /// (duplicate groups, staging, etc.) reflects the new file set.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (CurrentFolder is null) return;

        IsIdle = false;
        StatusText = "Scanning…";

        try
        {
            var scanned = await Task.Run(() => ScanFiles(CurrentFolder));

            Records.Clear();
            foreach (var r in scanned) Records.Add(r);

            StatusText = scanned.Count == 0
                ? "No image files found in that folder."
                : $"{scanned.Count} file(s) scanned.";

            ScanCompleted?.Invoke(this, EventArgs.Empty);
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

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
