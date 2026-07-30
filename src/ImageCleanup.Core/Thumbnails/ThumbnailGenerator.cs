using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageCleanup.Core.Thumbnails;

/// <summary>
/// Generates small PNG-encoded preview thumbnails for the duplicate review UI.
/// Kept separate from the full-resolution decode paths used for hashing/quality
/// scoring so thumbnail generation stays cheap regardless of source image size.
/// </summary>
public static class ThumbnailGenerator
{
    /// <summary>
    /// Loads the image at <paramref name="filePath"/>, resizes it to fit within
    /// <paramref name="maxDimension"/> x <paramref name="maxDimension"/> (aspect
    /// ratio preserved), and returns PNG-encoded bytes. Returns null rather than
    /// throwing when the file is missing, unreadable, or not a decodable image.
    /// </summary>
    public static byte[]? GenerateThumbnail(string filePath, int maxDimension = 128)
    {
        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            return Generate(image, maxDimension);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Generates thumbnail bytes from an already-loaded in-memory image (for testing).</summary>
    public static byte[] Generate(Image<Rgba32> image, int maxDimension = 128)
    {
        using var thumb = image.Clone(ctx =>
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Bicubic
            }));

        using var ms = new MemoryStream();
        thumb.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
