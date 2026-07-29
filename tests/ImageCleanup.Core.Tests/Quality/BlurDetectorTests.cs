using ImageCleanup.Core.Quality;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests.Quality;

public sealed class BlurDetectorTests
{
    // ── Sharp image scores higher than blurry image ───────────────────────

    [Fact]
    public void SharpImage_ScoresHigherThan_UniformImage()
    {
        using var sharp  = MakeCheckerboard(64, 64, squareSize: 1);
        using var blurry = MakeUniform(64, 64, brightness: 128);

        double sharpScore  = BlurDetector.ComputeBlurScore(sharp);
        double blurryScore = BlurDetector.ComputeBlurScore(blurry);

        Assert.True(sharpScore > blurryScore,
            $"Expected sharp ({sharpScore:F2}) > blurry ({blurryScore:F2})");
    }

    [Fact]
    public void SharpImage_ScoresHigherThan_LinearGradient()
    {
        using var sharp    = MakeCheckerboard(64, 64, squareSize: 1);
        using var gradient = MakeGradient(64, 64);

        double sharpScore    = BlurDetector.ComputeBlurScore(sharp);
        double gradientScore = BlurDetector.ComputeBlurScore(gradient);

        Assert.True(sharpScore > gradientScore,
            $"Expected sharp ({sharpScore:F2}) > gradient ({gradientScore:F2})");
    }

    // ── Uniform image should score near zero ──────────────────────────────

    [Fact]
    public void UniformImage_ScoresZero()
    {
        using var img = MakeUniform(32, 32, 200);
        double score = BlurDetector.ComputeBlurScore(img);
        Assert.Equal(0.0, score, precision: 10);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void TinyImage_DoesNotThrow_ReturnsZero()
    {
        using var img = MakeUniform(2, 2, 100); // smaller than 3×3
        double score = BlurDetector.ComputeBlurScore(img);
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Score_IsNonNegative()
    {
        using var img = MakeCheckerboard(32, 32, squareSize: 2);
        Assert.True(BlurDetector.ComputeBlurScore(img) >= 0.0);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>Pixel-level checkerboard — maximum Laplacian response.</summary>
    private static Image<L8> MakeCheckerboard(int w, int h, int squareSize)
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

    /// <summary>Solid-colour image — Laplacian is everywhere zero.</summary>
    private static Image<L8> MakeUniform(int w, int h, byte brightness)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
                accessor.GetRowSpan(y).Fill(new L8(brightness));
        });
        return img;
    }

    /// <summary>Left-to-right linear gradient — very low Laplacian variance.</summary>
    private static Image<L8> MakeGradient(int w, int h)
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
}
