namespace ImageCleanup.Data.Models;

public sealed class FileRecord
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public ulong? PerceptualHash { get; set; }
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? BlurScore { get; set; }
    public DateTime? DateTaken { get; set; }
    public string? CameraModel { get; set; }
    public bool? IsScreenshot { get; set; }
    public bool? LowDetail { get; set; }
    public int SchemaVersion { get; set; }
}
