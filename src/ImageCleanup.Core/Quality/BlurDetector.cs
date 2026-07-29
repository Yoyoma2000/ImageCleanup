using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageCleanup.Core.Quality;

/// <summary>
/// Estimates image sharpness using Laplacian variance.
/// <para>
/// The image is converted to grayscale, then for each interior pixel the
/// discrete Laplacian is computed:
///   L(x,y) = −4·p(x,y) + p(x−1,y) + p(x+1,y) + p(x,y−1) + p(x,y+1)
/// The variance of all L values is the blur score: high variance → sharp edges,
/// low variance → blurry / uniform image.
/// </para>
/// </summary>
public static class BlurDetector
{
    public static double ComputeBlurScore(string filePath)
    {
        using var image = Image.Load<L8>(filePath);
        return ComputeBlurScore(image);
    }

    /// <summary>Overload for testing with an in-memory image.</summary>
    public static double ComputeBlurScore(Image<L8> image)
    {
        int w = image.Width;
        int h = image.Height;

        if (w < 3 || h < 3) return 0.0;

        double sum   = 0.0;
        double sumSq = 0.0;
        int    count = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 1; y < h - 1; y++)
            {
                var prev = accessor.GetRowSpan(y - 1);
                var curr = accessor.GetRowSpan(y);
                var next = accessor.GetRowSpan(y + 1);

                for (int x = 1; x < w - 1; x++)
                {
                    double lap = -4.0 * curr[x].PackedValue
                                 + prev[x].PackedValue
                                 + next[x].PackedValue
                                 + curr[x - 1].PackedValue
                                 + curr[x + 1].PackedValue;
                    sum   += lap;
                    sumSq += lap * lap;
                    count++;
                }
            }
        });

        if (count == 0) return 0.0;

        double mean     = sum / count;
        double variance = (sumSq / count) - (mean * mean);
        return variance;
    }
}
