namespace ImageCleanup.Data.Services;

public sealed class OrganizationExecutionResult
{
    public int Succeeded { get; set; }
    public int Failed => Failures.Count;
    public List<OrganizationMoveFailure> Failures { get; } = [];

    /// <summary>Full path to the durable move log written before any move was attempted.</summary>
    public string MoveLogPath { get; set; } = string.Empty;

    public string Summary => Failed == 0
        ? $"All {Succeeded} file(s) moved successfully."
        : $"{Succeeded} succeeded, {Failed} failed.";

    internal void AddFailure(string sourcePath, string destinationPath, string message) =>
        Failures.Add(new OrganizationMoveFailure(sourcePath, destinationPath, message));
}

public sealed class OrganizationMoveFailure
{
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public string Message { get; }

    public OrganizationMoveFailure(string sourcePath, string destinationPath, string message)
    {
        SourcePath      = sourcePath;
        DestinationPath = destinationPath;
        Message         = message;
    }
}
