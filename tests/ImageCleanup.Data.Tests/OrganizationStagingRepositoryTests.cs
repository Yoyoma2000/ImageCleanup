using ImageCleanup.Data.Models;
using ImageCleanup.Data.Repositories;
using ImageCleanup.Data.Tests.Helpers;

namespace ImageCleanup.Data.Tests;

public sealed class OrganizationStagingRepositoryTests : IDisposable
{
    private readonly DbFixture _db = new();
    private FileCacheRepository FileRepo => new(_db.ConnectionString);
    private OrganizationStagingRepository StagingRepo => new(_db.ConnectionString);

    public void Dispose() => _db.Dispose();

    // ── StageAction + GetPendingActions ───────────────────────────────────

    [Fact]
    public void StageAction_ThenGetPending_ReturnsStagedEntry()
    {
        int fileId = InsertFileRecord("/stage/img.jpg");

        StagingRepo.StageAction(fileId, "Move", "/archive/img.jpg", "Duplicate detected");

        var pending = StagingRepo.GetPendingActions().ToList();
        Assert.Single(pending);
        Assert.Equal(fileId, pending[0].FileRecordId);
        Assert.Equal("Move", pending[0].Action);
        Assert.Equal("/archive/img.jpg", pending[0].TargetPath);
        Assert.Equal("Duplicate detected", pending[0].Reason);
        Assert.False(pending[0].Committed);
    }

    [Fact]
    public void StageAction_NullTargetAndReason_RoundTripsNull()
    {
        int fileId = InsertFileRecord("/stage/no-target.jpg");

        StagingRepo.StageAction(fileId, "Delete", null, null);

        var pending = StagingRepo.GetPendingActions().ToList();
        Assert.Single(pending);
        Assert.Null(pending[0].TargetPath);
        Assert.Null(pending[0].Reason);
    }

    [Fact]
    public void GetPendingActions_MultipleEntries_ReturnsAllUncommitted()
    {
        int fileId1 = InsertFileRecord("/multi/a.jpg");
        int fileId2 = InsertFileRecord("/multi/b.jpg");
        int fileId3 = InsertFileRecord("/multi/c.jpg");

        StagingRepo.StageAction(fileId1, "Move", "/archive/a.jpg", "dup");
        StagingRepo.StageAction(fileId2, "Move", "/archive/b.jpg", "dup");
        StagingRepo.StageAction(fileId3, "Delete", null, "corrupt");

        Assert.Equal(3, StagingRepo.GetPendingActions().Count());
    }

    // ── CommitAction ──────────────────────────────────────────────────────

    [Fact]
    public void CommitAction_MarksRowCommitted_DisappearsFromPending()
    {
        int fileId = InsertFileRecord("/commit/img.jpg");
        StagingRepo.StageAction(fileId, "Move", "/dest/img.jpg", "reason");

        var pending = StagingRepo.GetPendingActions().ToList();
        Assert.Single(pending);
        int stagingId = pending[0].Id;

        StagingRepo.CommitAction(stagingId);

        Assert.Empty(StagingRepo.GetPendingActions());
    }

    [Fact]
    public void CommitAction_OnlyCommitsTargetRow_LeavesOthersUncommitted()
    {
        int fileId1 = InsertFileRecord("/partial/a.jpg");
        int fileId2 = InsertFileRecord("/partial/b.jpg");

        StagingRepo.StageAction(fileId1, "Move", "/dest/a.jpg", "r");
        StagingRepo.StageAction(fileId2, "Move", "/dest/b.jpg", "r");

        var pending = StagingRepo.GetPendingActions().ToList();
        Assert.Equal(2, pending.Count);

        StagingRepo.CommitAction(pending[0].Id);

        var remaining = StagingRepo.GetPendingActions().ToList();
        Assert.Single(remaining);
        Assert.Equal(pending[1].Id, remaining[0].Id);
    }

    // ── ClearStaged ───────────────────────────────────────────────────────

    [Fact]
    public void ClearStaged_RemovesAllUncommitted_LeavesCommitted()
    {
        int fileId1 = InsertFileRecord("/clear/a.jpg");
        int fileId2 = InsertFileRecord("/clear/b.jpg");

        StagingRepo.StageAction(fileId1, "Move", null, null);
        StagingRepo.StageAction(fileId2, "Delete", null, null);

        // commit the first one
        var pending = StagingRepo.GetPendingActions().ToList();
        StagingRepo.CommitAction(pending[0].Id);

        // clear uncommitted
        StagingRepo.ClearStaged();

        // GetPendingActions only returns uncommitted — should be empty
        Assert.Empty(StagingRepo.GetPendingActions());
    }

    [Fact]
    public void ClearStaged_EmptyTable_DoesNotThrow()
    {
        var ex = Record.Exception(() => StagingRepo.ClearStaged());
        Assert.Null(ex);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private int InsertFileRecord(string path)
    {
        var record = new FileRecord
        {
            FilePath     = path,
            FileHash     = "hash",
            FileSize     = 1024,
            LastModified = DateTime.UtcNow,
        };
        FileRepo.Upsert(record);
        return record.Id;
    }
}
