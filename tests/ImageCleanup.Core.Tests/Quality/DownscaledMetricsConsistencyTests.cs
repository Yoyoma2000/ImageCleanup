using ImageCleanup.Core.Quality;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageCleanup.Core.Tests.Quality;

/// <summary>
/// ScanSessionService.ScanFiles now downscales the decoded image (to fit
/// within ~400px on the longest side) before handing it to BlurDetector/
/// LowDetailDetector, instead of running those over the full-resolution
/// decode — profiling showed the two together dominated scan time (66% on
/// a 6183-file, ~12MP-per-photo scan). Downscaling changes the raw
/// BlurScore/variance numbers (fewer pixels, and bicubic resampling
/// smooths high-frequency detail), but nothing in the app thresholds those
/// raw numbers directly: Quality only sorts BlurScore relatively
/// (blurriest-first), and LowDetail's only consumer is the boolean itself
/// (SuggestionEngine excludes LowDetail==true files from perceptual-hash
/// grouping). These tests confirm that boolean/relative behavior survives
/// the downscale on images large enough to actually trigger it (real
/// photos, unlike this repo's small synthetic test fixtures elsewhere).
/// </summary>
public sealed class DownscaledMetricsConsistencyTests
{
    // Mirrors ScanSessionService.MetricsMaxDimension.
    private const int MetricsMaxDimension = 400;

    // ── Blur: relative ordering survives downscale ──────────────────────

    [Fact]
    public void SharpImage_StillScoresHigherThanUniform_AfterDownscale()
    {
        using var sharpFull  = MakeHighResCheckerboard(2400, 1600, squareSize: 40);
        using var blurryFull = MakeHighResUniform(2400, 1600, brightness: 128);

        using var sharpDown  = Downscale(sharpFull);
        using var blurryDown = Downscale(blurryFull);

        double sharpScore  = BlurDetector.ComputeBlurScore(sharpDown);
        double blurryScore = BlurDetector.ComputeBlurScore(blurryDown);

        Assert.True(sharpScore > blurryScore,
            $"Expected downscaled sharp ({sharpScore:F2}) > downscaled blurry ({blurryScore:F2})");
    }

    [Fact]
    public void RelativeBlurRanking_OfThreeImages_UnchangedByDownscale()
    {
        using var sharpFull   = MakeHighResCheckerboard(2400, 1600, squareSize: 40);
        using var mediumFull  = MakeHighResCheckerboard(2400, 1600, squareSize: 200);
        using var uniformFull = MakeHighResUniform(2400, 1600, brightness: 128);

        double sharpFullScore   = BlurDetector.ComputeBlurScore(sharpFull);
        double mediumFullScore  = BlurDetector.ComputeBlurScore(mediumFull);
        double uniformFullScore = BlurDetector.ComputeBlurScore(uniformFull);

        using var sharpDown   = Downscale(sharpFull);
        using var mediumDown  = Downscale(mediumFull);
        using var uniformDown = Downscale(uniformFull);

        double sharpDownScore   = BlurDetector.ComputeBlurScore(sharpDown);
        double mediumDownScore  = BlurDetector.ComputeBlurScore(mediumDown);
        double uniformDownScore = BlurDetector.ComputeBlurScore(uniformDown);

        // Same relative order (blurriest-first sort depends only on this).
        Assert.True(sharpFullScore > mediumFullScore && mediumFullScore > uniformFullScore);
        Assert.True(sharpDownScore > mediumDownScore && mediumDownScore > uniformDownScore);
    }

    // ── LowDetail: boolean classification survives downscale ────────────

    [Fact]
    public void SolidColor_IsLowDetail_BeforeAndAfterDownscale()
    {
        using var full = MakeHighResUniform(2400, 1600, brightness: 200);
        using var down = Downscale(full);

        Assert.True(LowDetailDetector.IsLowDetail(full));
        Assert.True(LowDetailDetector.IsLowDetail(down));
    }

    [Fact]
    public void NearlyUniformWithNoise_IsLowDetail_BeforeAndAfterDownscale()
    {
        // Bicubic downscaling averages out per-pixel noise, so if anything
        // the downscaled variance should be lower (more confidently
        // low-detail), not higher — this asserts it doesn't flip to false.
        using var full = MakeHighResNoisyUniform(2400, 1600, baseValue: 200, noiseRadius: 3, seed: 42);
        using var down = Downscale(full);

        Assert.True(LowDetailDetector.IsLowDetail(full));
        Assert.True(LowDetailDetector.IsLowDetail(down));
    }

    [Fact]
    public void HighContrastQuadrants_IsNotLowDetail_BeforeAndAfterDownscale()
    {
        // Large-scale (not fine-grained) contrast, like a real photo's broad
        // regions (sky vs. ground, subject vs. background) — low-frequency
        // structure that survives downscaling without aliasing.
        using var full = MakeHighContrastQuadrants(2400, 1600);
        using var down = Downscale(full);

        Assert.False(LowDetailDetector.IsLowDetail(full));
        Assert.False(LowDetailDetector.IsLowDetail(down));
    }

    [Fact]
    public void FullRangeGradient_IsNotLowDetail_BeforeAndAfterDownscale()
    {
        using var full = MakeHighResGradient(2400, 1600);
        using var down = Downscale(full);

        Assert.False(LowDetailDetector.IsLowDetail(full));
        Assert.False(LowDetailDetector.IsLowDetail(down));
    }

    [Fact]
    public void ImageSmallerThanMetricsMaxDimension_IsNotResizedByDownscaleHelper()
    {
        // Mirrors ScanSessionService's "skip resize if already small enough"
        // guard — Downscale() here uses the same Max-fit logic, so a small
        // source should come back unchanged in size.
        using var small = MakeHighResUniform(200, 150, brightness: 100);
        using var down = Downscale(small);

        Assert.Equal(small.Width, down.Width);
        Assert.Equal(small.Height, down.Height);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>Same Resize call ScanSessionService.ScanFiles uses for BlurDetector/LowDetailDetector input.</summary>
    private static Image<L8> Downscale(Image<L8> source)
    {
        if (Math.Max(source.Width, source.Height) <= MetricsMaxDimension)
            return source.Clone();

        return source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size    = new Size(MetricsMaxDimension, MetricsMaxDimension),
            Mode    = ResizeMode.Max,
            Sampler = KnownResamplers.Bicubic
        }));
    }

    private static Image<L8> MakeHighResUniform(int w, int h, byte brightness)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
                accessor.GetRowSpan(y).Fill(new L8(brightness));
        });
        return img;
    }

    private static Image<L8> MakeHighResCheckerboard(int w, int h, int squareSize)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    bool white = ((x / squareSize) + (y / squareSize)) % 2 == 0;
                    row[x] = new L8(white ? (byte)255 : (byte)0);
                }
            }
        });
        return img;
    }

    private static Image<L8> MakeHighResGradient(int w, int h)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    row[x] = new L8((byte)(x * 255 / (w - 1)));
            }
        });
        return img;
    }

    private static Image<L8> MakeHighResNoisyUniform(int w, int h, byte baseValue, int noiseRadius, int seed)
    {
        var rng = new Random(seed);
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    int v = baseValue + rng.Next(-noiseRadius, noiseRadius + 1);
                    row[x] = new L8((byte)Math.Clamp(v, 0, 255));
                }
            }
        });
        return img;
    }

    private static Image<L8> MakeHighContrastQuadrants(int w, int h)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                bool topHalf = y < h / 2;
                for (int x = 0; x < w; x++)
                {
                    bool leftHalf = x < w / 2;
                    // Top-left/bottom-right black, top-right/bottom-left white.
                    row[x] = new L8(leftHalf == topHalf ? (byte)0 : (byte)255);
                }
            }
        });
        return img;
    }
}
