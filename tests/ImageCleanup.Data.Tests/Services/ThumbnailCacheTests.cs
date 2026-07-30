using ImageCleanup.Data.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCleanup.Data.Tests.Services;

public class ThumbnailCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"thumbcache_{Guid.NewGuid():N}");
    private readonly string _sourcePath = Path.ChangeExtension(Path.GetTempFileName(), ".png");

    public ThumbnailCacheTests()
    {
        using var img = new Image<Rgba32>(64, 64, Color.CornflowerBlue);
        img.SaveAsPng(_sourcePath);
    }

    [Fact]
    public void GetOrCreateThumbnail_GeneratesAndWritesCacheFile()
    {
        var cache = new ThumbnailCache(_cacheDir);
        var lastModified = File.GetLastWriteTimeUtc(_sourcePath);

        var bytes = cache.GetOrCreateThumbnail(_sourcePath, lastModified);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes!);
        Assert.Single(Directory.GetFiles(_cacheDir));
    }

    [Fact]
    public void GetOrCreateThumbnail_SecondCall_ReturnsCachedBytesWithoutSourceFile()
    {
        var cache = new ThumbnailCache(_cacheDir);
        var lastModified = File.GetLastWriteTimeUtc(_sourcePath);

        var first = cache.GetOrCreateThumbnail(_sourcePath, lastModified);
        Assert.NotNull(first);

        // Remove the source — a cache-miss regeneration attempt would now fail.
        File.Delete(_sourcePath);

        var second = cache.GetOrCreateThumbnail(_sourcePath, lastModified);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrCreateThumbnail_DifferentLastModified_RegeneratesRatherThanReusingCache()
    {
        var cache = new ThumbnailCache(_cacheDir);
        var t1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        cache.GetOrCreateThumbnail(_sourcePath, t1);
        cache.GetOrCreateThumbnail(_sourcePath, t2);

        Assert.Equal(2, Directory.GetFiles(_cacheDir).Length);
    }

    [Fact]
    public async Task GetOrCreateThumbnail_ConcurrentCallsForSameKey_DoNotThrow_AndReturnConsistentResults()
    {
        var lastModified = File.GetLastWriteTimeUtc(_sourcePath);

        const int concurrency = 32;
        var tasks = new Task<byte[]?>[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            // A fresh ThumbnailCache instance per task, all pointing at the same
            // directory — mirrors the real scenario where Duplicates and Quality
            // each own a separate ThumbnailCache instance over the same shared
            // cache folder, so the lock must work across instances too.
            var cache = new ThumbnailCache(_cacheDir);
            tasks[i] = Task.Run(() => cache.GetOrCreateThumbnail(_sourcePath, lastModified));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.NotNull(r));
        var first = results[0]!;
        Assert.All(results, r => Assert.Equal(first, r));

        // Exactly one cache file for this key — no corrupted/partial duplicates
        // left behind by racing writers.
        Assert.Single(Directory.GetFiles(_cacheDir));
    }

    [Fact]
    public void GetOrCreateThumbnail_CorruptFile_ReturnsNull()
    {
        var corruptPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        File.WriteAllBytes(corruptPath, [0xDE, 0xAD, 0xBE, 0xEF]);
        try
        {
            var cache = new ThumbnailCache(_cacheDir);
            var result = cache.GetOrCreateThumbnail(corruptPath, DateTime.UtcNow);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_sourcePath)) File.Delete(_sourcePath);
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }
}
