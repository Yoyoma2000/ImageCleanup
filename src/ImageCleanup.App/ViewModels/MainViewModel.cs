using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Quality;
using ImageCleanup.Core.Hashing;
using ImageCleanup.Data;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;

namespace ImageCleanup.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private readonly string _connectionString;

    // ── Observable state ─────────────────────────────────────────────────

    private string _statusText = "Ready — click \"Select Folder\" to start.";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isIdle = true;
    public bool IsIdle
    {
        get => _isIdle;
        private set { _isIdle = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Construction ──────────────────────────────────────────────────────

    public MainViewModel()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "cache.db");
        _connectionString = $"Data Source={dbPath}";
        DbInitializer.Initialize(_connectionString);
    }

    // ── Scan ──────────────────────────────────────────────────────────────

    public async Task ScanFolderAsync(string folderPath)
    {
        IsIdle = false;
        Groups.Clear();
        StatusText = "Scanning…";

        try
        {
            var scanned = await Task.Run(() => ScanFiles(folderPath));

            var imageRecords = scanned.Select(ToImageRecord);
            var dupGroups = SuggestionEngine.GroupDuplicates(imageRecords);

            foreach (var g in dupGroups)
                Groups.Add(new DuplicateGroupViewModel(g));

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

    // ── Private helpers ───────────────────────────────────────────────────

    private List<FileRecord> ScanFiles(string folderPath)
    {
        var repo = new FileCacheRepository(_connectionString);
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
                var fi = new FileInfo(path);
                var lastModified = fi.LastWriteTimeUtc;

                if (!repo.NeedsRescan(path, fi.Length, lastModified))
                {
                    var cached = repo.GetByPath(path);
                    if (cached is not null) { results.Add(cached); continue; }
                }

                // ── fresh scan ───────────────────────────────────────────
                var fileHash = ComputeSha256(path);
                var meta     = ExifReader.ReadMetadata(path);

                ulong? perceptualHash = null;
                double? blurScore    = null;
                try
                {
                    perceptualHash = DHasher.ComputeFromFile(path);
                    blurScore      = BlurDetector.ComputeBlurScore(path);
                }
                catch { /* non-image or corrupt — skip perceptual fields */ }

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
                };

                repo.Upsert(record);
                results.Add(record);
            }
            catch { /* permission error or I/O failure — skip file */ }
        }

        return results;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
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
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
