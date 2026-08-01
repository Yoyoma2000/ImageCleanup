using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using ImageCleanup.Core.Hashing;
using ImageCleanup.Core.IO;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Quality;
using ImageCleanup.Data;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Services;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    private string _statusText = LocalizationService.Current.GetString(
        "ScanSession.Ready", LocalizationService.Current.GetString("MainWindow.SelectFolderButton"));
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
        var loc = LocalizationService.Current;
        StatusText = loc.GetString("ScanSession.Scanning");

        try
        {
            var scanned = await Task.Run(() => ScanFiles(CurrentFolder));

            Records.Clear();
            foreach (var r in scanned) Records.Add(r);

            var skippedSuffix = _lastSkippedDirectories.Count > 0
                ? loc.GetString("ScanSession.SkippedFoldersSuffix", _lastSkippedDirectories.Count)
                : string.Empty;

            StatusText = scanned.Count == 0
                ? loc.GetString("ScanSession.NoFilesFound", skippedSuffix)
                : loc.GetString("ScanSession.FilesScanned", scanned.Count, skippedSuffix);

            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = loc.GetString("ScanSession.ScanFailed", ex.Message);
        }
        finally
        {
            IsIdle = true;
        }
    }

    /// <summary>Directories skipped (hidden/system/inaccessible) during the most recent scan.</summary>
    public IReadOnlyList<string> LastSkippedDirectories => _lastSkippedDirectories;
    private List<string> _lastSkippedDirectories = [];

    // Full scan's SQLite work is batched into transactions of this size
    // rather than one giant transaction, so a crash/failure partway through
    // a very large scan (tens of thousands of files) only loses the
    // in-progress batch, not everything scanned so far.
    private const int SqliteBatchSize = 500;

    // BlurDetector/LowDetailDetector both compute per-pixel statistics over
    // the whole image (Laplacian variance, pixel-value variance) — real
    // profiling showed this dominated scan time (66% combined on a 6183-file
    // scan of iPhone photos, ~12MP each) because both operated on the
    // full-resolution decode. Neither signal needs full resolution: blur and
    // near-blank/solid-colour detection are both about broad tonal/edge
    // structure, which survives a modest downscale. DHash is unaffected
    // (kept on the full-res image) since it already downsamples to 9x8
    // internally regardless of input size.
    private const int MetricsMaxDimension = 400;

    // Per-file work (decode/downscale/hash/EXIF/blur/low-detail) is
    // independent per file and CPU/IO-bound, so it runs in parallel — capped
    // below ProcessorCount on high-core-count machines rather than left
    // unbounded, since this is also disk I/O (every file gets opened and
    // read at least twice: SHA256 + image decode) and ImageSharp decode
    // buffers add real memory pressure per concurrent file; 8 is a
    // reasonable ceiling for typical consumer hardware regardless of how
    // many cores are available.
    private static readonly int MaxScanParallelism = Math.Min(Environment.ProcessorCount, 8);

    // TEMPORARY perf-investigation instrumentation — remove or feature-flag
    // after the scan-performance investigation is done.
    private List<FileRecord> ScanFiles(string folderPath)
    {
        var repo    = new FileCacheRepository(_connectionString);
        var results = new ConcurrentBag<FileRecord>();

        var swWallClock  = Stopwatch.StartNew();
        long ticksNeedsRescan = 0, ticksGetByPath = 0, ticksSha256 = 0, ticksExif = 0;
        long ticksDecode = 0, ticksDownscale = 0, ticksDHash = 0, ticksBlur = 0, ticksLowDetail = 0;
        long ticksUpsert = 0;
        int cacheHits = 0, cacheMisses = 0;

        var swEnumerate = Stopwatch.StartNew();
        var skipped = new List<string>();
        var files = ImageFileEnumerator.EnumerateFiles(folderPath, ImageExtensions, skipped).ToList();
        _lastSkippedDirectories = skipped;
        swEnumerate.Stop();

        // SQLite writes are not safely concurrent (Microsoft.Data.Sqlite
        // connections can't be shared across threads), so every Upsert goes
        // through this single dedicated writer consuming a queue that the
        // parallel workers below produce into — DB access stays
        // single-threaded while decode/hash/blur/etc. run in parallel.
        // NeedsRescan/GetByPath (reads) are cheap SELECTs and safe to run
        // from each worker on its own short-lived connection instead
        // (Microsoft.Data.Sqlite applies a busy-timeout automatically, so a
        // read racing the writer's periodic commit just waits briefly
        // rather than throwing).
        using var writeQueue = new BlockingCollection<FileRecord>(boundedCapacity: 1000);

        // A dedicated Thread (not Task.Run/ThreadPool) so this long-running
        // consumer can never be starved by the ThreadPool threads
        // Parallel.ForEach is using below — Task.Run would share the same
        // pool and could, in principle, be delayed behind it.
        Exception? writerException = null;
        var writerThread = new Thread(() =>
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var transaction = connection.BeginTransaction();
                var sinceCommit = 0;

                try
                {
                    foreach (var record in writeQueue.GetConsumingEnumerable())
                    {
                        try
                        {
                            var sw = Stopwatch.StartNew();
                            repo.Upsert(record, connection, transaction);
                            Interlocked.Add(ref ticksUpsert, sw.ElapsedTicks);

                            if (++sinceCommit >= SqliteBatchSize)
                            {
                                transaction.Commit();
                                transaction.Dispose();
                                transaction = connection.BeginTransaction();
                                sinceCommit = 0;
                            }
                        }
                        catch { /* DB write failure for one file — skip persisting it, scan continues */ }
                    }

                    transaction.Commit();
                }
                finally
                {
                    transaction.Dispose();
                }
            }
            catch (Exception ex)
            {
                writerException = ex;
            }
        })
        {
            IsBackground = true,
            Name = "ScanFiles-SqliteWriter"
        };
        writerThread.Start();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxScanParallelism };
        Parallel.ForEach(files, parallelOptions, path =>
        {
            try
            {
                var fi           = new FileInfo(path);
                var lastModified = fi.LastWriteTimeUtc;

                var sw = Stopwatch.StartNew();
                var needsRescan = repo.NeedsRescan(path, fi.Length, lastModified);
                Interlocked.Add(ref ticksNeedsRescan, sw.ElapsedTicks);

                if (!needsRescan)
                {
                    sw = Stopwatch.StartNew();
                    var cached = repo.GetByPath(path);
                    Interlocked.Add(ref ticksGetByPath, sw.ElapsedTicks);
                    if (cached is not null)
                    {
                        Interlocked.Increment(ref cacheHits);
                        results.Add(cached);
                        return;
                    }
                }

                Interlocked.Increment(ref cacheMisses);

                sw = Stopwatch.StartNew();
                var fileHash = ComputeSha256(path);
                Interlocked.Add(ref ticksSha256, sw.ElapsedTicks);

                sw = Stopwatch.StartNew();
                var meta = ExifReader.ReadMetadata(path);
                Interlocked.Add(ref ticksExif, sw.ElapsedTicks);

                ulong? perceptualHash = null;
                double? blurScore    = null;
                bool?   isLowDetail  = null;
                try
                {
                    // Decoded once and shared across DHash/blur/low-detail —
                    // each previously called Image.Load<L8> independently,
                    // decoding the same file three times.
                    sw = Stopwatch.StartNew();
                    using var image = Image.Load<L8>(path);
                    Interlocked.Add(ref ticksDecode, sw.ElapsedTicks);

                    sw = Stopwatch.StartNew();
                    perceptualHash = DHasher.Compute(image);
                    Interlocked.Add(ref ticksDHash, sw.ElapsedTicks);

                    // Blur/low-detail only need broad tonal/edge structure,
                    // not full resolution — downscale once and share that
                    // between them (see MetricsMaxDimension). Skipped when
                    // the image is already smaller than the target.
                    Image<L8>? downscaled = null;
                    try
                    {
                        var metricsImage = image;
                        if (Math.Max(image.Width, image.Height) > MetricsMaxDimension)
                        {
                            sw = Stopwatch.StartNew();
                            downscaled = image.Clone(ctx => ctx.Resize(new ResizeOptions
                            {
                                Size    = new Size(MetricsMaxDimension, MetricsMaxDimension),
                                Mode    = ResizeMode.Max,
                                Sampler = KnownResamplers.Bicubic
                            }));
                            Interlocked.Add(ref ticksDownscale, sw.ElapsedTicks);
                            metricsImage = downscaled;
                        }

                        sw = Stopwatch.StartNew();
                        blurScore = BlurDetector.ComputeBlurScore(metricsImage);
                        Interlocked.Add(ref ticksBlur, sw.ElapsedTicks);

                        sw = Stopwatch.StartNew();
                        isLowDetail = LowDetailDetector.IsLowDetail(metricsImage);
                        Interlocked.Add(ref ticksLowDetail, sw.ElapsedTicks);
                    }
                    finally
                    {
                        downscaled?.Dispose();
                    }
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

                results.Add(record);
                writeQueue.Add(record);
            }
            catch { /* permission error or I/O failure */ }
        });

        // All producers are done — let the writer drain the queue, commit
        // its final (possibly partial) batch, and finish before we return
        // (otherwise a caller could see FileRecord.Id still unset, or the
        // cache could be stale for files this scan just processed).
        writeQueue.CompleteAdding();
        writerThread.Join();
        if (writerException is not null)
            throw new InvalidOperationException("Scan database write failed.", writerException);

        swWallClock.Stop();

        // Once parallel, the sum of per-step times ("aggregateCpuTime") can
        // — and normally will — exceed wall-clock time, since multiple
        // files' steps overlap across threads. That's expected, not a bug;
        // wallClock is what the user actually experiences.
        var aggregateCpuMs =
            TimeSpan.FromTicks(ticksNeedsRescan + ticksGetByPath + ticksSha256 + ticksExif +
                                ticksDecode + ticksDownscale + ticksDHash + ticksBlur +
                                ticksLowDetail + ticksUpsert).TotalMilliseconds;

        Debug.WriteLine(
            $"[ScanPerf] wallClock={swWallClock.ElapsedMilliseconds}ms " +
            $"aggregateCpuTime={aggregateCpuMs:F0}ms (sum of per-step times across all files — " +
            $"can exceed wallClock now that work runs in parallel, that's expected) " +
            $"maxDegreeOfParallelism={MaxScanParallelism} files={files.Count} " +
            $"cacheHits={cacheHits} cacheMisses={cacheMisses} | " +
            $"enumerate={swEnumerate.ElapsedMilliseconds}ms " +
            $"needsRescan={TimeSpan.FromTicks(ticksNeedsRescan).TotalMilliseconds:F0}ms " +
            $"getByPath={TimeSpan.FromTicks(ticksGetByPath).TotalMilliseconds:F0}ms " +
            $"sha256={TimeSpan.FromTicks(ticksSha256).TotalMilliseconds:F0}ms " +
            $"exif={TimeSpan.FromTicks(ticksExif).TotalMilliseconds:F0}ms " +
            $"decode={TimeSpan.FromTicks(ticksDecode).TotalMilliseconds:F0}ms " +
            $"downscale={TimeSpan.FromTicks(ticksDownscale).TotalMilliseconds:F0}ms " +
            $"dhash={TimeSpan.FromTicks(ticksDHash).TotalMilliseconds:F0}ms " +
            $"blur={TimeSpan.FromTicks(ticksBlur).TotalMilliseconds:F0}ms " +
            $"lowDetail={TimeSpan.FromTicks(ticksLowDetail).TotalMilliseconds:F0}ms " +
            $"upsert={TimeSpan.FromTicks(ticksUpsert).TotalMilliseconds:F0}ms");

        return results.ToList();
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
