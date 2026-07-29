namespace ImageCleanup.Core.Metadata;

public sealed class ExifMetadata
{
    public bool HasExif { get; init; }
    public DateTime? DateTaken { get; init; }
    public string? CameraModel { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}
