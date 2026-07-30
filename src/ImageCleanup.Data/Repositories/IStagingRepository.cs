using ImageCleanup.Data.Models;

namespace ImageCleanup.Data.Repositories;

/// <summary>
/// Common shape shared by OrganizationStagingRepository and
/// QualityStagingRepository so CommitService's execution logic (Delete/Move,
/// per-entry error handling, FK-order row removal) can run against either
/// one without duplicating it — while each feature's staging table, and thus
/// its review/commit flow, stays fully separate from the other's.
/// </summary>
public interface IStagingRepository
{
    int StageAction(int fileRecordId, string action, string? targetPath, string? reason);
    void RemoveStagingEntry(int stagingId);
    void UpdateTargetPath(int stagingId, string? targetPath);
    IEnumerable<StagingEntry> GetPendingActions();
    void CommitAction(int stagingId);
    void ClearStaged();
}
