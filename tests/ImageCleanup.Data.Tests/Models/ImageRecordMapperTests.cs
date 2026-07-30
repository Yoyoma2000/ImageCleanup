using ImageCleanup.Data.Models;

namespace ImageCleanup.Data.Tests.Models;

public class ImageRecordMapperTests
{
    [Fact]
    public void ToImageRecord_ExifDataPresent_RoundTripsDateTakenAndSetsHasExifTrue()
    {
        var dateTaken = new DateTime(2023, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var record = new FileRecord
        {
            FilePath     = @"C:\Photos\real.jpg",
            FileHash     = "hash",
            FileSize     = 1024,
            LastModified = DateTime.UtcNow,
            DateTaken    = dateTaken,
            CameraModel  = "Pixel 7",
        };

        var imageRecord = record.ToImageRecord();

        Assert.Equal(dateTaken, imageRecord.DateTaken);
        Assert.True(imageRecord.HasExif);
    }

    [Fact]
    public void ToImageRecord_NoExifData_HasExifFalseAndDateTakenNull()
    {
        var record = new FileRecord
        {
            FilePath     = @"C:\Downloads\screenshot.png",
            FileHash     = "hash",
            FileSize     = 2048,
            LastModified = DateTime.UtcNow,
            DateTaken    = null,
            CameraModel  = null,
        };

        var imageRecord = record.ToImageRecord();

        Assert.Null(imageRecord.DateTaken);
        Assert.False(imageRecord.HasExif);
    }

    [Fact]
    public void ToImageRecord_CameraModelOnlyNoDateTaken_StillReportsHasExifTrue()
    {
        // EXIF can be present with a Model tag but no DateTimeOriginal tag —
        // either field alone should be enough to imply EXIF was present.
        var record = new FileRecord
        {
            FilePath     = @"C:\Photos\no-date.jpg",
            FileHash     = "hash",
            FileSize     = 512,
            LastModified = DateTime.UtcNow,
            DateTaken    = null,
            CameraModel  = "Canon EOS 90D",
        };

        var imageRecord = record.ToImageRecord();

        Assert.Null(imageRecord.DateTaken);
        Assert.True(imageRecord.HasExif);
    }

    [Fact]
    public void ToImageRecord_CopiesRemainingFieldsUnchanged()
    {
        var record = new FileRecord
        {
            Id             = 42,
            FilePath       = @"C:\Photos\a.jpg",
            FileHash       = "abc123",
            PerceptualHash = 999UL,
            FileSize       = 4096,
            LastModified   = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Width          = 1920,
            Height         = 1080,
            BlurScore      = 12.5,
            LowDetail      = false,
        };

        var imageRecord = record.ToImageRecord();

        Assert.Equal(record.FilePath, imageRecord.FilePath);
        Assert.Equal(record.FileHash, imageRecord.FileHash);
        Assert.Equal(record.PerceptualHash, imageRecord.PerceptualHash);
        Assert.Equal(record.FileSize, imageRecord.FileSize);
        Assert.Equal(record.LastModified, imageRecord.LastModified);
        Assert.Equal(record.Width, imageRecord.Width);
        Assert.Equal(record.Height, imageRecord.Height);
        Assert.Equal(record.BlurScore, imageRecord.BlurScore);
        Assert.Equal(record.LowDetail, imageRecord.LowDetail);
    }
}
