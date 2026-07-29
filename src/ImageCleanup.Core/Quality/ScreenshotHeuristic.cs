using ImageCleanup.Core.Metadata;

namespace ImageCleanup.Core.Quality;

public static class ScreenshotHeuristic
{
    // Aspect-ratio tolerance: ±4% of the target ratio.
    private const double Tolerance = 0.04;

    // Common screen aspect ratios (expressed as wider/taller, already ≥ 1).
    private static readonly double[] ScreenRatios =
    [
        16.0 / 9.0,   // monitors, phones landscape, HD/UHD video
        16.0 / 10.0,  // laptops (1280×800, 1920×1200, …)
        4.0  / 3.0,   // classic monitors, tablets
        3.0  / 2.0,   // some phones/tablets
        19.5 / 9.0,   // modern bezel-less phones (e.g. 2532×1170)
        21.0 / 9.0,   // ultrawide monitors
        9.0  / 16.0,  // phone portrait (kept for completeness, will be normalised)
    ];

    /// <summary>
    /// Returns true when the image is likely a screenshot: no EXIF data present
    /// AND the dimensions match a recognisable screen aspect ratio.
    /// A real camera photo almost always embeds EXIF; the absence of EXIF combined
    /// with a screen-shaped canvas is the strongest practical signal.
    /// </summary>
    public static bool IsLikelyScreenshot(ExifMetadata metadata, int width, int height)
    {
        if (metadata.HasExif) return false;
        if (width <= 0 || height <= 0) return false;
        return HasScreenLikeAspectRatio(width, height);
    }

    private static bool HasScreenLikeAspectRatio(int width, int height)
    {
        // Normalise to landscape (ratio ≥ 1) so we only need one set of targets.
        double ratio = (double)Math.Max(width, height) / Math.Min(width, height);

        foreach (var target in ScreenRatios)
        {
            double t = Math.Max(target, 1.0 / target); // ensure target ≥ 1
            if (Math.Abs(ratio - t) / t <= Tolerance)
                return true;
        }
        return false;
    }
}
