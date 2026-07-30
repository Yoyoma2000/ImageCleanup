namespace ImageCleanup.Core.Metadata;

/// <summary>
/// HasExif-based classification for Organization purposes — chosen over
/// ScreenshotHeuristic's aspect-ratio approach after real-data testing showed
/// HasExif alone is the more reliable signal. ScreenshotHeuristic is kept
/// as-is (unused here) in case it's useful elsewhere later.
/// </summary>
public static class MetadataClassifier
{
    public static MetadataCategory ClassifyMetadata(ExifMetadata metadata) =>
        metadata.HasExif ? MetadataCategory.Photo : MetadataCategory.NoMetadata;
}
