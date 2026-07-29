namespace ImageCleanup.Core.Grouping;

/// <summary>
/// Lightweight, Core-layer view of a file used by SuggestionEngine.
/// Data layer maps FileRecord → ImageRecord before calling the engine.
/// </summary>
public sealed class ImageRecord
{
    public string FilePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public ulong? PerceptualHash { get; init; }
    public long FileSize { get; init; }
    public DateTime LastModified { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? BlurScore { get; init; }
    /// <summary>
    /// True when pixel-value variance is below the low-detail threshold.
    /// Low-detail images are excluded from perceptual near-dup grouping to
    /// prevent false positives from solid-colour / near-blank images.
    /// </summary>
    public bool? LowDetail { get; init; }
}
