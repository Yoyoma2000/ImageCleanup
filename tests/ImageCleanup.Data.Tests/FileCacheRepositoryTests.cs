using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Tests.Helpers;

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
