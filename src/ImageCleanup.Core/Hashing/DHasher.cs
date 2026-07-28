using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageCleanup.Core.Hashing;

/// <summary>
/// Difference hash (dHash): encodes relative brightness gradients in a 9x8
/// thumbnail into a 64-bit fingerprint. Images with Hamming distance ≤ 10
/// are typically perceptual duplicates or near-duplicates.
/// </summary>
public static class DHasher
{
    private const int Width = 9;
    private const int Height = 8;

    /// <summary>Computes the dHash of an image loaded from a file path.</summary>
    public static ulong ComputeFromFile(string path)
    {
        using var image = Image.Load<L8>(path);
        return Compute(image);
    }

    /// <summary>Computes the dHash of an in-memory ImageSharp image.</summary>
    public static ulong Compute(Image<L8> image)
    {
        using var thumb = image.Clone(ctx =>
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(Width, Height),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic
            }));

        ulong hash = 0;
        int bit = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width - 1; x++)
            {
                byte left = thumb[x, y].PackedValue;
                byte right = thumb[x + 1, y].PackedValue;
                if (left < right)
                    hash |= 1UL << bit;
                bit++;
            }
        }

        return hash;
    }

    /// <summary>
    /// Returns the Hamming distance between two dHash values (0 = identical,
    /// 64 = maximally different). Values ≤ 10 indicate likely duplicates.
    /// </summary>
    public static int HammingDistance(ulong a, ulong b) =>
        BitOperations.PopCount(a ^ b);
}
