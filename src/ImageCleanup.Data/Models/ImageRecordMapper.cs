using ImageCleanup.Core.Grouping;

namespace ImageCleanup.Data.Models;

/// <summary>
/// Single, centralized FileRecord (Data/SQLite) -> ImageRecord (Core) adapter.
/// Every feature that needs Core-layer logic over scanned files — Duplicates'
/// SuggestionEngine, Organization's OrganizationPlanner — should go through
/// this rather than growing its own copy.
/// </summary>
public static class ImageRecordMapper
{
    public static ImageRecord ToImageRecord(this FileRecord record) => new()
    {
        FilePath       = record.FilePath,
        FileHash       = record.FileHash,
        PerceptualHash = record.PerceptualHash,
        FileSize       = record.FileSize,
        LastModified   = record.LastModified,
        Width          = record.Width,
        Height         = record.Height,
        BlurScore      = record.BlurScore,
        LowDetail      = record.LowDetail,
        DateTaken      = record.DateTaken,
        // FileRecord doesn't persist a HasExif column. DateTaken/CameraModel
        // presence is the best available proxy without adding a new column —
        // both are only ever populated from ExifReader.ReadMetadata's EXIF
        // read during scan (see ScanSessionService.ScanFiles), so either one
        // being set already implies EXIF was present.
        HasExif        = record.DateTaken.HasValue || record.CameraModel is not null,
    };
}
