using ImageCleanup.Data.Repositories;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Executes all pending staged actions.
/// The caller supplies the delete strategy via <paramref name="deleteFile"/> so this
/// assembly stays free of platform-specific APIs (e.g. Windows Recycle Bin).
/// </summary>
public sealed class CommitService
{
    private readonly string _connectionString;
    private readonly Action<string> _deleteFile;

    /// <param name="connectionString">SQLite connection string shared with the repositories.</param>
    /// <param name="deleteFile">
    /// Called for Delete actions. Defaults to permanent <see cref="File.Delete"/>.
    /// Pass a Recycle-Bin wrapper from the App layer when you want safe deletion.
    /// </param>
    public CommitService(string connectionString, Action<string>? deleteFile = null)
    {
        _connectionString = connectionString;
        _deleteFile       = deleteFile ?? File.Delete;
    }

    public CommitResult ExecutePendingActions()
    {
        var stagingRepo = new OrganizationStagingRepository(_connectionString);
        var cacheRepo   = new FileCacheRepository(_connectionString);

        var pending = stagingRepo.GetPendingActions().ToList();
        var result  = new CommitResult();

        foreach (var entry in pending)
        {
            try
            {
                var fileRecord = cacheRepo.GetById(entry.FileRecordId);
                if (fileRecord is null)
                {
                    result.AddFailure(entry, $"Cache entry {entry.FileRecordId} not found.");
                    continue;
                }

                switch (entry.Action)
                {
                    case "Delete":
                        _deleteFile(fileRecord.FilePath);
                        break;

                    case "Move":
                        if (string.IsNullOrWhiteSpace(entry.TargetPath))
                        {
                            result.AddFailure(entry, "Move action has no target path.");
                            continue;
                        }
                        var dir = Path.GetDirectoryName(entry.TargetPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        File.Move(fileRecord.FilePath, entry.TargetPath, overwrite: false);
                        break;

                    default:
                        result.AddFailure(entry, $"Unknown action '{entry.Action}'.");
                        continue;
                }

                // Remove the staging row before the cache entry (FK order)
                stagingRepo.RemoveStagingEntry(entry.Id);
                cacheRepo.DeleteByPath(fileRecord.FilePath);
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.AddFailure(entry, ex.Message);
            }
        }

        return result;
    }
}
