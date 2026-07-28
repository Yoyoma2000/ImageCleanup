using ImageCleanup.Core.Hashing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests.Hashing;

public class DHasherTests
{
    /// <summary>Identical images must produce the same hash and zero Hamming distance.</summary>
    [Fact]
    public void IdenticalImages_SameHash()
    {
        using var img = CreateGradientImage(64, 64);
        ulong h1 = DHasher.Compute(img);
        ulong h2 = DHasher.Compute(img);

        Assert.Equal(h1, h2);
        Assert.Equal(0, DHasher.HammingDistance(h1, h2));
    }

    /// <summary>A solid-black and solid-white image should be maximally different.</summary>
    [Fact]
    public void BlackVsWhite_MaximalDistance()
    {
        using var black = CreateSolid(32, 32, 0);
        using var white = CreateSolid(32, 32, 255);

        ulong hBlack = DHasher.Compute(black);
        ulong hWhite = DHasher.Compute(white);

        // Solid black → all pixels equal → all gradient bits 0 → hash = 0
        Assert.Equal(0UL, hBlack);
        // Solid white → same reasoning → hash = 0
        Assert.Equal(0UL, hWhite);
        Assert.Equal(0, DHasher.HammingDistance(hBlack, hWhite));
    }

    /// <summary>
    /// A left-dark/right-bright split image should produce a non-zero hash that
    /// differs noticeably from its horizontally mirrored version.
    /// </summary>
    [Fact]
    public void MirroredImages_HighHammingDistance()
    {
        using var original = CreateHalfGradient(64, 64, leftDark: true);
        using var mirrored = CreateHalfGradient(64, 64, leftDark: false);

        ulong h1 = DHasher.Compute(original);
        ulong h2 = DHasher.Compute(mirrored);

        // Hashes should differ significantly
        Assert.True(DHasher.HammingDistance(h1, h2) > 20,
            $"Expected large distance, got {DHasher.HammingDistance(h1, h2)}");
    }

    /// <summary>HammingDistance is symmetric.</summary>
    [Fact]
    public void HammingDistance_IsSymmetric()
    {
        ulong a = 0xDEADBEEF_12345678UL;
        ulong b = 0xCAFEBABE_87654321UL;
        Assert.Equal(DHasher.HammingDistance(a, b), DHasher.HammingDistance(b, a));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Image<L8> CreateSolid(int w, int h, byte brightness)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                row.Fill(new L8(brightness));
            }
        });
        return img;
    }

    private static Image<L8> CreateGradientImage(int w, int h)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = new L8((byte)(x * 255 / (row.Length - 1)));
            }
        });
        return img;
    }

    private static Image<L8> CreateHalfGradient(int w, int h, bool leftDark)
    {
        var img = new Image<L8>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    double t = (double)x / (row.Length - 1); // 0..1
                    byte v = leftDark ? (byte)(t * 255) : (byte)((1 - t) * 255);
                    row[x] = new L8(v);
                }
            }
        });
        return img;
    }
}
