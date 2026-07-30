using ImageCleanup.Core.Quality;

namespace ImageCleanup.Core.Tests.Quality;

public class QualityReviewOrderTests
{
    private sealed record TestFile(string Path, double? BlurScore);

    [Fact]
    public void SortBlurriestFirst_OrdersAscendingByBlurScore()
    {
        var files = new[]
        {
            new TestFile("sharp.jpg", 400.0),
            new TestFile("blurriest.jpg", 5.0),
            new TestFile("medium.jpg", 100.0),
        };

        var sorted = QualityReviewOrder.SortBlurriestFirst(files, f => f.BlurScore);

        Assert.Equal(["blurriest.jpg", "medium.jpg", "sharp.jpg"], sorted.Select(f => f.Path));
    }

    [Fact]
    public void SortBlurriestFirst_ExcludesNullBlurScore()
    {
        var files = new[]
        {
            new TestFile("has-score.jpg", 50.0),
            new TestFile("no-score.jpg", null),
        };

        var sorted = QualityReviewOrder.SortBlurriestFirst(files, f => f.BlurScore);

        Assert.Single(sorted);
        Assert.Equal("has-score.jpg", sorted[0].Path);
    }

    [Fact]
    public void SortBlurriestFirst_AllNull_ReturnsEmpty()
    {
        var files = new[]
        {
            new TestFile("a.jpg", null),
            new TestFile("b.jpg", null),
        };

        var sorted = QualityReviewOrder.SortBlurriestFirst(files, f => f.BlurScore);

        Assert.Empty(sorted);
    }

    [Fact]
    public void SortBlurriestFirst_EmptyInput_ReturnsEmpty()
    {
        var sorted = QualityReviewOrder.SortBlurriestFirst(Array.Empty<TestFile>(), f => f.BlurScore);

        Assert.Empty(sorted);
    }

    [Fact]
    public void SortBlurriestFirst_DoesNotApplyAnyThresholdFlagging_ReturnsAllNonNullRegardlessOfScore()
    {
        var files = new[]
        {
            new TestFile("very-sharp.jpg", 10000.0),
            new TestFile("very-blurry.jpg", 0.1),
        };

        var sorted = QualityReviewOrder.SortBlurriestFirst(files, f => f.BlurScore);

        Assert.Equal(2, sorted.Count);
    }
}
