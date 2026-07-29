using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;

namespace ImageCleanup.Core.Metadata;

public static class ExifReader
{
    /// <summary>
    /// Reads EXIF metadata from a file.  Returns a result with HasExif=false
    /// rather than throwing when the file has no EXIF data or is unreadable.
    /// </summary>
    public static ExifMetadata ReadMetadata(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            var ifd0    = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd  = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            bool hasExif = ifd0 is not null || subIfd is not null;

            // Dimensions: Image.Identify reads only the file header — no pixel decode.
            var info = Image.Identify(filePath);

            return new ExifMetadata
            {
                HasExif    = hasExif,
                DateTaken  = ParseDateTaken(subIfd),
                CameraModel = TrimToNull(ifd0?.GetDescription(ExifDirectoryBase.TagModel)),
                Width      = info?.Width,
                Height     = info?.Height,
            };
        }
        catch
        {
            // Corrupt file, unsupported format, or I/O error — treat as no metadata.
            return new ExifMetadata { HasExif = false };
        }
    }

    private static DateTime? ParseDateTaken(ExifSubIfdDirectory? dir)
    {
        if (dir is null) return null;
        return dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) ? dt : null;
    }

    private static string? TrimToNull(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
