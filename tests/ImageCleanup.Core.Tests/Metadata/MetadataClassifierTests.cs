using ImageCleanup.Core.Metadata;

namespace ImageCleanup.Core.Tests.Metadata;

public class MetadataClassifierTests
{
    [Fact]
    public void ClassifyMetadata_HasExifTrue_ReturnsPhoto()
    {
        var metadata = new ExifMetadata { HasExif = true };

        var category = MetadataClassifier.ClassifyMetadata(metadata);

        Assert.Equal(MetadataCategory.Photo, category);
    }

    [Fact]
    public void ClassifyMetadata_HasExifFalse_ReturnsNoMetadata()
    {
        var metadata = new ExifMetadata { HasExif = false };

        var category = MetadataClassifier.ClassifyMetadata(metadata);

        Assert.Equal(MetadataCategory.NoMetadata, category);
    }

    [Fact]
    public void ClassifyMetadata_IgnoresDimensionsAndCameraModel_OnlyHasExifMatters()
    {
        // A file with camera model / dimensions set but HasExif false should
        // still classify as NoMetadata — the classifier is HasExif-only,
        // deliberately not gated by ScreenshotHeuristic's aspect-ratio check.
        var metadata = new ExifMetadata
        {
            HasExif     = false,
            CameraModel = "Some Camera",
            Width       = 1920,
            Height      = 1080,
        };

        var category = MetadataClassifier.ClassifyMetadata(metadata);

        Assert.Equal(MetadataCategory.NoMetadata, category);
    }
}
