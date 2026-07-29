using ImageCleanup.Core.Quality;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests.Quality;

public sealed class LowDetailDetectorTests
{
    // ── IsLowDetail ───────────────────────────────────────────────────────────

    [Fact]
    public void SolidWhite_IsLowDetail()
    {
        using var img = MakeUniform(64, 64, 255);
        Assert.True(LowDetailDetector.IsLowDetail(img));
    }

    [Fact]
    public void SolidBlack_IsLowDetail()
    {
        using var img = MakeUniform(64, 64, 0);
        Assert.True(LowDetailDetector.IsLowDetail(img));
    }

    [Fact]
    public void SolidGray_IsLowDetail()
    {
        using var img = MakeUniform(64, 64, 128);
        Assert.True(LowDetailDetector.IsLowDetail(img));
    }

    [Fact]
    public void NearlyUniform_SmallNoise_IsLowDetail()
    {
        // ±3 noise: variance ≈ E[U(-3,3)²] ≈ 4 — well below threshold of 50
        using var img = MakeNoisyUniform(64, 64, baseValue: 200, noiseRadius: 3, seed: 42);
        Assert.True(LowDetailDetector.IsLowDetail(img));
    }

    [Fact]
    public void Checkerboard_IsNotLowDetail()
    {
        // Alternating 0/255 → variance = 127.5² ≈ 16 256
        using var img = MakeCheckerboard(64, 64);
        Assert.False(LowDetailDetector.IsLowDetail(img));
    }

    [Fact]
    public void FullGradient_IsNotLowDetail()
    {
        // Pixel values 0..255 → variance ≈ 255²/12 ≈ 5 419
        using var img = MakeGradient(64, 64);
        Assert.False(LowDetailDetector.IsLowDetail(img));
    }

    // ── ComputePixelVariance ──────────────────────────────────────────────────

    [Fact]
    public void VarianceOfUniform_IsZero()
    {
        using var img = MakeUniform(32, 32, 128);
        Assert.Equal(0.0, LowDetailDetector.ComputePixelVariance(img), precision: 10);
    }

    [Fact]
    public void VarianceOfCheckerboard_IsAboveThreshold()
    {
        using var img = MakeCheckerboard(32, 32);
        double v = LowDetailDetector.ComputePixelVariance(img);
        Assert.True(v > LowDetailDetector.DefaultVarianceThreshold,
            $"Expected variance > {LowDetailDetector.DefaultVarianceThreshold}, got {v:F2}");
    }

    [Fact]
    public void VarianceOfGradient_IsAboveThreshold()
    {
        using var img = MakeGradient(64, 64);
        double v = LowDetailDetector.ComputePixelVariance(img);
        Assert.True(v > LowDetailDetector.DefaultVarianceThreshold,
            $"Expected variance > {LowDetailDetector.DefaultVarianceThreshold}, got {v:F2}");
    }

    [Fact]
    public void VarianceOfNoisyUniform_IsBelowThreshold()
    {
        using var img = MakeNoisyUniform(64, 64, baseValue: 200, noiseRadius: 3, seed: 42);
        double v = LowDetailDetector.ComputePixelVariance(img);
        Assert.True(v < LowDetailDetector.DefaultVarianceThreshold,
            $"Expected variance < {LowDetailDetector.DefaultVarianceThreshold}, got {v:F2}");
    }

    [Fact]
    public void VarianceIsDeterministic_SameImageTwice()
    {
        using var img = MakeCheckerboard(32, 32);
        double v1 = LowDetailDetector.ComputePixelVariance(img);
        double v2 = LowDetailDetector.ComputePixelVariance(img);
        Assert.Equal(v1, v2);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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

    private static Image<L8> MakeCheckerboard(int w, int h)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                    row[x] = new L8((x + y) % 2 == 0 ? (byte)255 : (byte)0);
            }
        });
        return img;
    }

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

    private static Image<L8> MakeNoisyUniform(int w, int h, byte baseValue, int noiseRadius, int seed)
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
}
