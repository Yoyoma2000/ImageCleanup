namespace ImageCleanup.Core.Metadata;

/// <summary>
/// Classifies a file by whether it carries EXIF metadata. Real-data testing
/// showed HasExif alone is a far more reliable signal than aspect-ratio-based
/// heuristics (see ScreenshotHeuristic) for telling real photos apart from
/// everything else.
/// </summary>
public enum MetadataCategory
{
    /// <summary>Has EXIF metadata — almost always a real camera/phone photo.</summary>
    Photo,

    /// <summary>
    /// No EXIF metadata. Named for what it actually measures, not what it's
    /// assumed to be: this also catches downloads, memes, and edited/resaved
    /// images that lost their EXIF on save — not just screenshots.
    /// </summary>
    NoMetadata,
}
