using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Quality;

namespace ImageCleanup.Core.Tests.Quality;

public sealed class ScreenshotHeuristicTests
{
    // ── Should detect as screenshot ───────────────────────────────────────

    [Theory]
    [InlineData(1920, 1080)]   // 16:9 — most common monitor/phone landscape
    [InlineData(1280, 720)]    // 16:9 — 720p
    [InlineData(3840, 2160)]   // 16:9 — 4K
    [InlineData(1920, 1200)]   // 16:10 — laptop
    [InlineData(1280, 800)]    // 16:10 — laptop
    [InlineData(2560, 1600)]   // 16:10 — retina MacBook
    [InlineData(1024, 768)]    // 4:3   — classic monitor
    [InlineData(2532, 1170)]   // ≈19.5:9 — iPhone 12/13 Pro Max
    [InlineData(1080, 1920)]   // 16:9 portrait — phone screenshot
    public void NoExif_ScreenRatio_IsScreenshot(int width, int height)
    {
        var meta = NoExif();
        Assert.True(ScreenshotHeuristic.IsLikelyScreenshot(meta, width, height),
            $"Expected screenshot for {width}×{height}");
    }

    // ── Should NOT detect as screenshot ──────────────────────────────────

    [Fact]
    public void HasExif_ScreenRatio_IsNotScreenshot()
    {
        // A real camera photo at 1920×1080 must NOT be flagged.
        var meta = WithExif();
        Assert.False(ScreenshotHeuristic.IsLikelyScreenshot(meta, 1920, 1080));
    }

    [Fact]
    public void HasExif_AnySize_IsNotScreenshot()
    {
        var meta = WithExif();
        Assert.False(ScreenshotHeuristic.IsLikelyScreenshot(meta, 4000, 3000));
    }

    [Theory]
    [InlineData(1000, 1000)]  // square (1:1) — not a screen shape
    [InlineData(700,  500)]   // 7:5 = 1.40 — nearest target 4:3 (1.333) is 5% away
    [InlineData(1200, 1000)]  // 6:5 = 1.20 — nearest target 4:3 (1.333) is 10% away
    public void NoExif_NonScreenRatio_IsNotScreenshot(int width, int height)
    {
        var meta = NoExif();
        Assert.False(ScreenshotHeuristic.IsLikelyScreenshot(meta, width, height),
            $"Did not expect screenshot for {width}×{height}");
    }

    [Fact]
    public void ZeroDimensions_IsNotScreenshot()
    {
        Assert.False(ScreenshotHeuristic.IsLikelyScreenshot(NoExif(), 0, 1080));
        Assert.False(ScreenshotHeuristic.IsLikelyScreenshot(NoExif(), 1920, 0));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static ExifMetadata NoExif()   => new() { HasExif = false };
    private static ExifMetadata WithExif() => new() { HasExif = true  };
}
