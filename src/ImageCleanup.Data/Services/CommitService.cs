using ImageCleanup.Data.Repositories;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Executes all pending staged actions against a given staging repository.
/// The caller supplies the delete strategy via <paramref name="deleteFile"/> so this
/// assembly stays free of platform-specific APIs (e.g. Windows Recycle Bin).
/// </summary>
public sealed class CommitService
{
    private readonly string _connectionString;
    private readonly IStagingRepository _stagingRepo;
    private readonly Action<string> _deleteFile;

    /// <param name="connectionString">SQLite connection string shared with the repositories.</param>
    /// <param name="deleteFile">
    /// Called for Delete actions. Defaults to permanent <see cref="File.Delete"/>.
    /// Pass a Recycle-Bin wrapper from the App layer when you want safe deletion.
    /// </param>
    /// <remarks>Defaults to OrganizationStagingRepository — the Duplicates feature's staging table.</remarks>
    public CommitService(string connectionString, Action<string>? deleteFile = null)
        : this(connectionString, new OrganizationStagingRepository(connectionString), deleteFile)
    {
    }

    /// <param name="connectionString">SQLite connection string shared with the repositories.</param>
    /// <param name="stagingRepository">
    /// The feature-specific staging table to read/commit against (e.g.
    /// OrganizationStagingRepository for Duplicates, QualityStagingRepository
    /// for Quality) — keeps each feature's commit flow scoped to its own rows.
    /// </param>
    /// <param name="deleteFile">
    /// Called for Delete actions. Defaults to permanent <see cref="File.Delete"/>.
    /// Pass a Recycle-Bin wrapper from the App layer when you want safe deletion.
    /// </param>
    public CommitService(string connectionString, IStagingRepository stagingRepository, Action<string>? deleteFile = null)
    {
        _connectionString = connectionString;
        _stagingRepo      = stagingRepository;
        _deleteFile       = deleteFile ?? File.Delete;
    }

    public CommitResult ExecutePendingActions()
    {
        var cacheRepo = new FileCacheRepository(_connectionString);

        var pending = _stagingRepo.GetPendingActions().ToList();
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
                _stagingRepo.RemoveStagingEntry(entry.Id);
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
