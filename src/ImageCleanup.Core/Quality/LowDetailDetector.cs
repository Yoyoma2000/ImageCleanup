using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Quality;

/// <summary>
/// Detects images with near-uniform pixel content (solid colours, blank backgrounds)
/// by computing the variance of raw grayscale pixel values.
/// <para>
/// Low-variance images produce near-identical DHash values regardless of their
/// actual colour, which causes false-positive near-duplicate grouping. Flagging
/// them lets <c>SuggestionEngine</c> exclude them from perceptual-hash grouping
/// while still catching them as exact duplicates when they truly are identical.
/// </para>
/// </summary>
public static class LowDetailDetector
{
    /// <summary>
    /// Grayscale pixel-value variance below which an image is considered low-detail.
    /// A pure solid-colour image has variance 0; this threshold (~7 std-dev) catches
    /// near-blank images and solid backgrounds with minor JPEG/compression noise.
    /// </summary>
    public const double DefaultVarianceThreshold = 50.0;

    public static bool IsLowDetail(string filePath,
        double varianceThreshold = DefaultVarianceThreshold)
    {
        using var image = Image.Load<L8>(filePath);
        return IsLowDetail(image, varianceThreshold);
    }

    /// <summary>Overload for testing with an in-memory image.</summary>
    public static bool IsLowDetail(Image<L8> image,
        double varianceThreshold = DefaultVarianceThreshold) =>
        ComputePixelVariance(image) < varianceThreshold;

    /// <summary>
    /// Variance of the raw grayscale pixel values across the entire image.
    /// Returns 0 for a perfectly uniform image.
    /// </summary>
    public static double ComputePixelVariance(Image<L8> image)
    {
        long sum   = 0;
        long sumSq = 0;
        long count = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    long v  = row[x].PackedValue;
                    sum   += v;
                    sumSq += v * v;
                    count++;
                }
            }
        });

        if (count == 0) return 0.0;

        double mean = (double)sum / count;
        return (double)sumSq / count - mean * mean;
    }
}
