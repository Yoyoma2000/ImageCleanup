using System.Collections.Concurrent;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Tests;

public sealed class FileCacheRepositoryTests : IDisposable
{
    private readonly DbFixture _db = new();
    private FileCacheRepository Repo => new(_db.ConnectionString);

    public void Dispose() => _db.Dispose();

    // ── Upsert + GetByPath round-trip ─────────────────────────────────────

    [Fact]
    public void Upsert_ThenGetByPath_ReturnsEquivalentRecord()
    {
        var record = MakeSampleRecord("/photos/img001.jpg");
        Repo.Upsert(record);

        var fetched = Repo.GetByPath("/photos/img001.jpg");

        Assert.NotNull(fetched);
        Assert.Equal(record.FilePath, fetched.FilePath);
        Assert.Equal(record.FileHash, fetched.FileHash);
        Assert.Equal(record.PerceptualHash, fetched.PerceptualHash);
        Assert.Equal(record.FileSize, fetched.FileSize);
        Assert.Equal(record.LastModified, fetched.LastModified);
        Assert.Equal(record.Width, fetched.Width);
        Assert.Equal(record.Height, fetched.Height);
        Assert.Equal(record.BlurScore, fetched.BlurScore);
        Assert.Equal(record.CameraModel, fetched.CameraModel);
        Assert.Equal(record.IsScreenshot, fetched.IsScreenshot);
    }

    [Fact]
    public void GetByPath_UnknownPath_ReturnsNull()
    {
        var result = Repo.GetByPath("/does/not/exist.jpg");
        Assert.Null(result);
    }

    [Fact]
    public void Upsert_ExistingPath_UpdatesFields()
    {
        var record = MakeSampleRecord("/photos/update.jpg");
        Repo.Upsert(record);

        record.FileHash = "newhash";
        record.FileSize = 99_999;
        Repo.Upsert(record);

        var fetched = Repo.GetByPath("/photos/update.jpg");
        Assert.NotNull(fetched);
        Assert.Equal("newhash", fetched.FileHash);
        Assert.Equal(99_999, fetched.FileSize);
    }

    [Fact]
    public void Upsert_NullableFieldsRoundTrip_Null()
    {
        var record = new FileRecord
        {
            FilePath     = "/photos/minimal.jpg",
            FileHash     = "abc",
            FileSize     = 1024,
            LastModified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        Repo.Upsert(record);

        var fetched = Repo.GetByPath("/photos/minimal.jpg");
        Assert.NotNull(fetched);
        Assert.Null(fetched.PerceptualHash);
        Assert.Null(fetched.Width);
        Assert.Null(fetched.Height);
        Assert.Null(fetched.BlurScore);
        Assert.Null(fetched.DateTaken);
        Assert.Null(fetched.CameraModel);
        Assert.Null(fetched.IsScreenshot);
    }

    // ── NeedsRescan ──────────────────────────────────────────────────────

    [Fact]
    public void NeedsRescan_NoRow_ReturnsTrue()
    {
        var result = Repo.NeedsRescan("/photos/new.jpg", 1024, DateTime.UtcNow);
        Assert.True(result);
    }

    [Fact]
    public void NeedsRescan_ExactMatch_ReturnsFalse()
    {
        var modified = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var record = MakeSampleRecord("/photos/exact.jpg", fileSize: 5000, lastModified: modified);
        Repo.Upsert(record);

        Assert.False(Repo.NeedsRescan("/photos/exact.jpg", 5000, modified));
    }

    [Fact]
    public void NeedsRescan_SizeDiffers_ReturnsTrue()
    {
        var modified = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var record = MakeSampleRecord("/photos/size.jpg", fileSize: 5000, lastModified: modified);
        Repo.Upsert(record);

        Assert.True(Repo.NeedsRescan("/photos/size.jpg", 6000, modified));
    }

    [Fact]
    public void NeedsRescan_ModifiedDiffers_ReturnsTrue()
    {
        var modified = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var record = MakeSampleRecord("/photos/modified.jpg", fileSize: 5000, lastModified: modified);
        Repo.Upsert(record);

        Assert.True(Repo.NeedsRescan("/photos/modified.jpg", 5000, modified.AddSeconds(1)));
    }

    // ── SchemaVersion / NeedsRescan ──────────────────────────────────────

    [Fact]
    public void Upsert_SetsCurrentSchemaVersion()
    {
        var record = MakeSampleRecord("/photos/versioned.jpg");
        Repo.Upsert(record);

        var fetched = Repo.GetByPath("/photos/versioned.jpg");
        Assert.NotNull(fetched);
        Assert.Equal(FileCacheRepository.CurrentSchemaVersion, fetched.SchemaVersion);
    }

    [Fact]
    public void NeedsRescan_OldSchemaVersion_ReturnsTrueEvenWhenSizeAndDateUnchanged()
    {
        var modified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var record   = MakeSampleRecord("/photos/stale.jpg", fileSize: 8192, lastModified: modified);
        Repo.Upsert(record);

        // Simulate a record written by an older version of the app
        BackdateSchemaVersion("/photos/stale.jpg", version: 0);

        // File on disk is unchanged — but the cached schema is old
        Assert.True(Repo.NeedsRescan("/photos/stale.jpg", 8192, modified));
    }

    [Fact]
    public void NeedsRescan_CurrentSchemaVersion_UnchangedFile_ReturnsFalse()
    {
        var modified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var record   = MakeSampleRecord("/photos/fresh.jpg", fileSize: 8192, lastModified: modified);
        Repo.Upsert(record);

        // Schema is current and file stats match — no rescan needed
        Assert.False(Repo.NeedsRescan("/photos/fresh.jpg", 8192, modified));
    }

    [Fact]
    public void NeedsRescan_OldSchemaVersion_SizeAlsoChanged_StillReturnsTrue()
    {
        var modified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var record   = MakeSampleRecord("/photos/double.jpg", fileSize: 8192, lastModified: modified);
        Repo.Upsert(record);
        BackdateSchemaVersion("/photos/double.jpg", version: 0);

        // Both old schema AND changed size — should still be true
        Assert.True(Repo.NeedsRescan("/photos/double.jpg", 9999, modified));
    }

    // ── External connection/transaction overloads ───────────────────────
    // These exercise the ScanSessionService full-scan pattern: one shared
    // SqliteConnection + SqliteTransaction reused across many Upsert/
    // NeedsRescan/GetByPath calls instead of a connection per call.

    [Fact]
    public void Upsert_WithExternalConnectionAndTransaction_CommitPersistsRow()
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        using (var tx = connection.BeginTransaction())
        {
            Repo.Upsert(MakeSampleRecord("/photos/tx-commit.jpg"), connection, tx);
            tx.Commit();
        }

        var fetched = Repo.GetByPath("/photos/tx-commit.jpg");
        Assert.NotNull(fetched);
    }

    [Fact]
    public void Upsert_WithExternalTransaction_DisposedWithoutCommit_RollsBack()
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        using (var tx = connection.BeginTransaction())
        {
            Repo.Upsert(MakeSampleRecord("/photos/tx-rollback.jpg"), connection, tx);
            // Deliberately not committed — simulates a crash/failure mid-batch.
        }

        var fetched = Repo.GetByPath("/photos/tx-rollback.jpg");
        Assert.Null(fetched);
    }

    [Fact]
    public void NeedsRescanAndGetByPath_WithExternalConnection_SeeUncommittedWritesInSameTransaction()
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();

        Repo.Upsert(MakeSampleRecord("/photos/tx-visible.jpg", fileSize: 777), connection, tx);

        // Not yet committed, but reads sharing the same connection+transaction
        // must see it (this is how a batched scan avoids re-processing a file
        // it already upserted earlier in the same uncommitted batch).
        Assert.False(Repo.NeedsRescan("/photos/tx-visible.jpg", 777,
            new DateTime(2024, 3, 10, 9, 0, 0, DateTimeKind.Utc), connection, tx));
        Assert.NotNull(Repo.GetByPath("/photos/tx-visible.jpg", connection, tx));
    }

    [Fact]
    public void BatchedTransactions_CommittedBatchSurvives_UncommittedBatchDoesNot()
    {
        // Simulates ScanSessionService.ScanFiles' batching: commit every N
        // files, and confirm a failure partway through a later batch only
        // loses that in-progress batch, not previously committed ones.
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        using (var batch1 = connection.BeginTransaction())
        {
            Repo.Upsert(MakeSampleRecord("/photos/batch1-a.jpg"), connection, batch1);
            Repo.Upsert(MakeSampleRecord("/photos/batch1-b.jpg"), connection, batch1);
            batch1.Commit();
        }

        using (var batch2 = connection.BeginTransaction())
        {
            Repo.Upsert(MakeSampleRecord("/photos/batch2-a.jpg"), connection, batch2);
            // Simulate a crash before this batch commits.
        }

        Assert.NotNull(Repo.GetByPath("/photos/batch1-a.jpg"));
        Assert.NotNull(Repo.GetByPath("/photos/batch1-b.jpg"));
        Assert.Null(Repo.GetByPath("/photos/batch2-a.jpg"));
    }

    // ── Concurrency — mirrors ScanSessionService.ScanFiles' real pattern ──
    // ScanFiles now runs per-file work (including NeedsRescan/GetByPath
    // reads, each on its own short-lived connection) in parallel across
    // worker threads, while a single dedicated writer thread performs all
    // Upserts through one shared connection/transaction, batching commits.
    // FileCacheRepository itself has no App-layer test project to exercise
    // that orchestration directly, so this reproduces the same shape here:
    // many concurrent readers hammering the DB while one writer batches
    // writes, and confirms the result is exactly what a purely sequential
    // write of the same records would have produced — no corruption, no
    // lost writes, no unhandled exceptions from lock contention.

    [Fact]
    public void ConcurrentReaders_WhileSingleWriterBatchesUpserts_ProducesCorrectFinalState()
    {
        const int recordCount = 150;
        const int batchSize   = 25;
        const int readerThreads = 4;
        const int readerRounds  = 3;

        var expectedPaths = Enumerable.Range(0, recordCount)
            .Select(i => $"/photos/concurrent-{i}.jpg")
            .ToList();

        using var writerConnection = new SqliteConnection(_db.ConnectionString);
        writerConnection.Open();

        Exception? writerException = null;
        var writerThread = new Thread(() =>
        {
            try
            {
                var transaction = writerConnection.BeginTransaction();
                var sinceCommit = 0;
                for (int i = 0; i < recordCount; i++)
                {
                    Repo.Upsert(MakeSampleRecord(expectedPaths[i], fileSize: 1000 + i), writerConnection, transaction);
                    if (++sinceCommit >= batchSize)
                    {
                        transaction.Commit();
                        transaction.Dispose();
                        transaction = writerConnection.BeginTransaction();
                        sinceCommit = 0;
                    }
                }
                transaction.Commit();
                transaction.Dispose();
            }
            catch (Exception ex)
            {
                writerException = ex;
            }
        });

        var readerExceptions = new ConcurrentBag<Exception>();
        var readers = Enumerable.Range(0, readerThreads).Select(_ => new Thread(() =>
        {
            try
            {
                for (int round = 0; round < readerRounds; round++)
                {
                    foreach (var path in expectedPaths)
                    {
                        // Own short-lived connection per call — same as
                        // ScanFiles' parallel workers, relying on
                        // Microsoft.Data.Sqlite's automatic busy-timeout to
                        // absorb any contention with the writer's commits
                        // rather than throwing.
                        Repo.NeedsRescan(path, 999_999, DateTime.UtcNow);
                        Repo.GetByPath(path);
                    }
                }
            }
            catch (Exception ex)
            {
                readerExceptions.Add(ex);
            }
        })).ToList();

        writerThread.Start();
        foreach (var r in readers) r.Start();

        writerThread.Join();
        foreach (var r in readers) r.Join();

        Assert.Null(writerException);
        Assert.Empty(readerExceptions);

        var all = Repo.GetAllRecords().ToList();
        Assert.Equal(recordCount, all.Count);
        foreach (var i in Enumerable.Range(0, recordCount))
        {
            var match = Assert.Single(all, r => r.FilePath == expectedPaths[i]);
            Assert.Equal(1000 + i, match.FileSize);
        }
    }

    // ── GetAllRecords ────────────────────────────────────────────────────

    [Fact]
    public void GetAllRecords_ReturnsAllUpserted()
    {
        Repo.Upsert(MakeSampleRecord("/a.jpg"));
        Repo.Upsert(MakeSampleRecord("/b.jpg"));
        Repo.Upsert(MakeSampleRecord("/c.jpg"));

        var all = Repo.GetAllRecords().ToList();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, r => r.FilePath == "/a.jpg");
        Assert.Contains(all, r => r.FilePath == "/b.jpg");
        Assert.Contains(all, r => r.FilePath == "/c.jpg");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Directly sets SchemaVersion in the DB to simulate a record written by
    /// an older version of the app, without going through the repository.
    /// </summary>
    private void BackdateSchemaVersion(string filePath, int version)
    {
        using var conn = new SqliteConnection(_db.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE FileRecords SET SchemaVersion = $v WHERE FilePath = $p";
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.ExecuteNonQuery();
    }

    private static FileRecord MakeSampleRecord(
        string path,
        long fileSize = 102_400,
        DateTime? lastModified = null) => new()
    {
        FilePath       = path,
        FileHash       = "sha256-deadbeef",
        PerceptualHash = 0xCAFEBABE_12345678UL,
        FileSize       = fileSize,
        LastModified   = lastModified ?? new DateTime(2024, 3, 10, 9, 0, 0, DateTimeKind.Utc),
        Width          = 1920,
        Height         = 1080,
        BlurScore      = 42.5,
        DateTaken      = new DateTime(2024, 3, 10, 8, 30, 0, DateTimeKind.Utc),
        CameraModel    = "Canon EOS R6",
        IsScreenshot   = false,
    };
}
