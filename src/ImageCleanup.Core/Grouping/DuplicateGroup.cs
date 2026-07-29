namespace ImageCleanup.Core.Grouping;

public sealed class DuplicateGroup
{
    /// <summary>All files in the group (includes Suggested).</summary>
    public List<ImageRecord> Files { get; init; } = [];

    /// <summary>
    /// The file recommended to keep: highest resolution, then highest BlurScore,
    /// then most recent LastModified as successive tiebreakers.
    /// </summary>
    public ImageRecord Suggested { get; init; } = null!;

    /// <summary>True when every file in the group has an identical SHA-256 hash.</summary>
    public bool IsExactMatch { get; init; }
}
