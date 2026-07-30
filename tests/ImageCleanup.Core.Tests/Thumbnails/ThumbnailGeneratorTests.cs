using ImageCleanup.Core.Thumbnails;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests.Thumbnails;

public class ThumbnailGeneratorTests
{
    [Fact]
    public void GenerateThumbnail_ValidImage_ReturnsNonNullPngBytes()
    {
        var path = TempPng(64, 64);
        try
        {
            var bytes = ThumbnailGenerator.GenerateThumbnail(path);

            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes!);
            // PNG signature
            Assert.Equal(0x89, bytes![0]);
            Assert.Equal((byte)'P', bytes[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GenerateThumbnail_CorruptFile_ReturnsNull()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        File.WriteAllBytes(path, [0xDE, 0xAD, 0xBE, 0xEF]);
        try
        {
            var bytes = ThumbnailGenerator.GenerateThumbnail(path);
            Assert.Null(bytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GenerateThumbnail_MissingFile_ReturnsNull()
    {
        var bytes = ThumbnailGenerator.GenerateThumbnail(
            Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png"));
        Assert.Null(bytes);
    }

    [Theory]
    [InlineData(500, 300)]
    [InlineData(300, 500)]
    [InlineData(50, 50)]
    public void GenerateThumbnail_RespectsMaxDimension(int width, int height)
    {
        var path = TempPng(width, height);
        try
        {
            var bytes = ThumbnailGenerator.GenerateThumbnail(path, maxDimension: 128);
            Assert.NotNull(bytes);

            using var ms = new MemoryStream(bytes!);
            using var result = Image.Load<Rgba32>(ms);

            Assert.True(result.Width <= 128, $"Width {result.Width} exceeded max dimension.");
            Assert.True(result.Height <= 128, $"Height {result.Height} exceeded max dimension.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPng(int width, int height)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        using var img = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        img.SaveAsPng(path);
        return path;
    }
}
