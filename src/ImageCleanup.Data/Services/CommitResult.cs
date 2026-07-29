using ImageCleanup.Data.Models;

namespace ImageCleanup.Data.Services;

public sealed class CommitResult
{
    public int Succeeded { get; set; }
    public int Failed => Failures.Count;
    public List<CommitFailure> Failures { get; } = [];
    public string Summary => Failed == 0
        ? $"All {Succeeded} operation(s) completed successfully."
        : $"{Succeeded} succeeded, {Failed} failed.";

    internal void AddFailure(StagingEntry entry, string message) =>
        Failures.Add(new CommitFailure(entry.Id, Path.GetFileName(entry.TargetPath ?? string.Empty), entry.Action, message));
}

public sealed class CommitFailure
{
    public int StagingId { get; }
    public string FileName { get; }
    public string Action { get; }
    public string Message { get; }

    public CommitFailure(int stagingId, string fileName, string action, string message)
    {
        StagingId = stagingId;
        FileName  = fileName;
        Action    = action;
        Message   = message;
    }
}
