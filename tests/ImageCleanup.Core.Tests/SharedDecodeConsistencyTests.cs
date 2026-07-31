using ImageCleanup.Core.Hashing;
using ImageCleanup.Core.Quality;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Core.Tests;

/// <summary>
/// ScanSessionService.ScanFiles decodes a file once (<c>Image.Load&lt;L8&gt;</c>)
/// and passes the shared <c>Image&lt;L8&gt;</c> to DHasher/BlurDetector/
/// LowDetailDetector's pre-loaded-image overloads, instead of each of those
/// three independently decoding the same file via their ComputeFromFile/
/// ComputeBlurScore(path)/IsLowDetail(path) overloads. These tests confirm
/// that swap produces byte-for-byte identical results.
/// </summary>
public sealed class SharedDecodeConsistencyTests : IDisposable
{
    private readonly string _tempFile;

    public SharedDecodeConsistencyTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"shared-decode-{Guid.NewGuid():N}.png");

        using var img = new Image<L8>(48, 32);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    bool white = ((x / 3) + (y / 3)) % 2 == 0;
                    row[x] = new L8(white ? (byte)230 : (byte)20);
                }
            }
        });
        img.SaveAsPng(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void DHash_FilePathAndSharedDecode_ProduceSameHash()
    {
        ulong fromPath = DHasher.ComputeFromFile(_tempFile);

        using var shared = Image.Load<L8>(_tempFile);
        ulong fromSharedDecode = DHasher.Compute(shared);

        Assert.Equal(fromPath, fromSharedDecode);
    }

    [Fact]
    public void BlurScore_FilePathAndSharedDecode_ProduceSameScore()
    {
        double fromPath = BlurDetector.ComputeBlurScore(_tempFile);

        using var shared = Image.Load<L8>(_tempFile);
        double fromSharedDecode = BlurDetector.ComputeBlurScore(shared);

        Assert.Equal(fromPath, fromSharedDecode);
    }

    [Fact]
    public void LowDetail_FilePathAndSharedDecode_ProduceSameResult()
    {
        bool fromPath = LowDetailDetector.IsLowDetail(_tempFile);

        using var shared = Image.Load<L8>(_tempFile);
        bool fromSharedDecode = LowDetailDetector.IsLowDetail(shared);

        Assert.Equal(fromPath, fromSharedDecode);
    }

    [Fact]
    public void AllThreeComputations_CanShareOneDecode_WithoutInterferingWithEachOther()
    {
        // The order ScanSessionService.ScanFiles calls them in: DHash, then
        // blur, then low-detail, all against the same Image<L8> instance.
        using var shared = Image.Load<L8>(_tempFile);
        ulong hash          = DHasher.Compute(shared);
        double blurScore    = BlurDetector.ComputeBlurScore(shared);
        bool isLowDetail    = LowDetailDetector.IsLowDetail(shared);

        ulong expectedHash       = DHasher.ComputeFromFile(_tempFile);
        double expectedBlur      = BlurDetector.ComputeBlurScore(_tempFile);
        bool expectedIsLowDetail = LowDetailDetector.IsLowDetail(_tempFile);

        Assert.Equal(expectedHash, hash);
        Assert.Equal(expectedBlur, blurScore);
        Assert.Equal(expectedIsLowDetail, isLowDetail);
    }
}
