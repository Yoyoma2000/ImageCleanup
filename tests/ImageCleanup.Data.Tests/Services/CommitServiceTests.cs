using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Services;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Tests.Services;

/// <summary>
/// Uses a file-based SQLite temp database so CommitService's internal connections
/// see the same data as the test's setup connections (no shared-cache complexity).
/// </summary>
public sealed class CommitServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _connectionString;
    private readonly FileCacheRepository _cacheRepo;
    private readonly OrganizationStagingRepository _stagingRepo;

    public CommitServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CommitServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dbPath = Path.Combine(_tempDir, "test.db");
        // Pooling=False ensures connections are truly closed after disposal,
        // so the temp directory can be deleted after each test.
        _connectionString = $"Data Source={dbPath};Pooling=False";
        DbInitializer.Initialize(_connectionString);

        _cacheRepo   = new FileCacheRepository(_connectionString);
        _stagingRepo = new OrganizationStagingRepository(_connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore lock races on CI */ }
        }
    }

    // ── Move ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ExecutePendingActions_MoveAction_MovesFileAndUpdatesCache()
    {
        var src  = WriteFile("original.jpg", "content");
        var dest = Path.Combine(_tempDir, "archive", "moved.jpg");

        var record = UpsertRecord(src);
        var sid    = _stagingRepo.StageAction(record.Id, "Move", null, null);
        _stagingRepo.UpdateTargetPath(sid, dest);

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dest));
        Assert.Null(_cacheRepo.GetByPath(src));
    }

    [Fact]
    public void ExecutePendingActions_MoveAction_CreatesDestinationDirectory()
    {
        var src  = WriteFile("nested.jpg", "data");
        var dest = Path.Combine(_tempDir, "deep", "sub", "folder", "nested.jpg");

        var record = UpsertRecord(src);
        var sid    = _stagingRepo.StageAction(record.Id, "Move", null, null);
        _stagingRepo.UpdateTargetPath(sid, dest);

        new CommitService(_connectionString).ExecutePendingActions();

        Assert.True(File.Exists(dest));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void ExecutePendingActions_DeleteAction_DeletesFileAndRemovesFromCache()
    {
        var src    = WriteFile("dup.jpg", "bytes");
        var record = UpsertRecord(src);
        _stagingRepo.StageAction(record.Id, "Delete", null, null);

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(File.Exists(src));
        Assert.Null(_cacheRepo.GetByPath(src));
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public void ExecutePendingActions_MissingSourceFile_MoveReturnsFailure()
    {
        // File.Move throws FileNotFoundException when source doesn't exist
        var ghostPath = Path.Combine(_tempDir, "ghost.jpg");
        var destPath  = Path.Combine(_tempDir, "dest", "ghost.jpg");
        var record    = UpsertRecord(ghostPath);  // cached but never created on disk
        var sid       = _stagingRepo.StageAction(record.Id, "Move", null, null);
        _stagingRepo.UpdateTargetPath(sid, destPath);

        var result = new CommitService(_connectionString).ExecutePendingActions();

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.NotEmpty(result.Failures[0].Message);
    }

    [Fact]
    public void ExecutePendingActions_MoveWithoutTargetPath_ReturnsFailure()
    {
        var src    = WriteFile("no_dest.jpg", "x");
        var record = UpsertRecord(src);
        _stagingRepo.StageAction(record.Id, "Move", null, null);  // TargetPath left null

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(File.Exists(src));  // untouched
    }

    [Fact]
    public void ExecutePendingActions_MissingCacheEntry_ReturnsFailureAndContinues()
    {
        // Inject a staging entry that references a non-existent FileRecord.
        // Disable FK enforcement for this one insert so SQLite allows the orphan row.
        using (var conn = new SqliteConnection(_connectionString))
        {
            conn.Open();
            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaCmd.ExecuteNonQuery();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO OrganizationStaging (FileRecordId, Action, TargetPath, Reason, Committed)
                VALUES (99999, 'Delete', NULL, NULL, 0)
                """;
            cmd.ExecuteNonQuery();
        }

        // Also add a valid entry so we confirm processing continues past the bad one
        var src  = WriteFile("valid.jpg", "ok");
        var good = UpsertRecord(src);
        _stagingRepo.StageAction(good.Id, "Delete", null, null);

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }

    // ── CommitResult summary ──────────────────────────────────────────────────

    [Fact]
    public void CommitResult_Summary_AllSucceeded_ReturnsAllSuccessMessage()
    {
        var src    = WriteFile("ok.jpg", "data");
        var record = UpsertRecord(src);
        _stagingRepo.StageAction(record.Id, "Delete", null, null);

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Contains("successfully", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitResult_Summary_WithFailures_MentionsBothCounts()
    {
        // 1 good, 1 phantom
        var src  = WriteFile("b.jpg", "b");
        var good = UpsertRecord(src);
        _stagingRepo.StageAction(good.Id, "Delete", null, null);

        using (var conn = new SqliteConnection(_connectionString))
        {
            conn.Open();
            using var p = conn.CreateCommand();
            p.CommandText = "PRAGMA foreign_keys = OFF";
            p.ExecuteNonQuery();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO OrganizationStaging (FileRecordId, Action, TargetPath, Reason, Committed) VALUES (77777, 'Delete', NULL, NULL, 0)";
            cmd.ExecuteNonQuery();
        }

        var result = new CommitService(_connectionString)
            .ExecutePendingActions();

        // Summary should mention both counts
        Assert.Contains("1", result.Summary);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private FileRecord UpsertRecord(string path)
    {
        var record = new FileRecord
        {
            FilePath     = path,
            FileHash     = Guid.NewGuid().ToString("N"),
            FileSize     = 100,
            LastModified = DateTime.UtcNow,
        };
        _cacheRepo.Upsert(record);
        return record;
    }
}
