using ImageCleanup.Core.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests.Metadata;

public sealed class ExifReaderTests
{
    // ── No-EXIF image ─────────────────────────────────────────────────────

    [Fact]
    public void ReadMetadata_PlainPng_HasExifFalseAndCorrectDimensions()
    {
        var tmp = TempPng(width: 120, height: 80);
        try
        {
            var meta = ExifReader.ReadMetadata(tmp);

            Assert.False(meta.HasExif);
            Assert.Null(meta.DateTaken);
            Assert.Null(meta.CameraModel);
            Assert.Equal(120, meta.Width);
            Assert.Equal(80, meta.Height);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadMetadata_PlainPng_DateTakenIsNull()
    {
        var tmp = TempPng(64, 64);
        try
        {
            Assert.Null(ExifReader.ReadMetadata(tmp).DateTaken);
        }
        finally { File.Delete(tmp); }
    }

    // ── Corrupt / non-image files ─────────────────────────────────────────

    [Fact]
    public void ReadMetadata_GarbageBytes_ReturnsHasExifFalse()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            // MetadataExtractor will fail to identify the format and throw.
            File.WriteAllBytes(tmp, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03]);

            var meta = ExifReader.ReadMetadata(tmp);

            Assert.False(meta.HasExif);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadMetadata_PlainTextFile_ReturnsHasExifFalse()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "This is not an image file.");

            var meta = ExifReader.ReadMetadata(tmp);

            Assert.False(meta.HasExif);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadMetadata_NonexistentFile_ReturnsHasExifFalse()
    {
        var meta = ExifReader.ReadMetadata(@"C:\does\not\exist\fake.jpg");
        Assert.False(meta.HasExif);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string TempPng(int width, int height)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        using var img = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        img.SaveAsPng(path);
        return path;
    }
}
