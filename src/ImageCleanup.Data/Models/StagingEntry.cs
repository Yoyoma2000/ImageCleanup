namespace ImageCleanup.Data.Models;

public sealed class StagingEntry
{
    public int Id { get; set; }
    public int FileRecordId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? Reason { get; set; }
    public bool Committed { get; set; }
}
