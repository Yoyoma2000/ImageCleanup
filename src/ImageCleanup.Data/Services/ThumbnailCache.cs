using System.Security.Cryptography;
using System.Text;
using ImageCleanup.Core.Thumbnails;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Get-or-generate disk cache for preview thumbnails. Thumbnails are stored as
/// loose PNG files under the cache directory rather than as SQLite BLOBs, so
/// FileRecords stays lightweight and the cache can be cleared by deleting a
/// folder. Keyed by file path + LastModified so a changed source file is
/// regenerated rather than served stale.
/// </summary>
public sealed class ThumbnailCache
{
    private readonly string _cacheDirectory;

    public ThumbnailCache(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "thumbnails");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Returns cached thumbnail bytes for <paramref name="filePath"/> if present,
    /// otherwise generates, caches, and returns them. Returns null if the source
    /// file cannot be decoded.
    /// </summary>
    public byte[]? GetOrCreateThumbnail(string filePath, DateTime lastModified, int maxDimension = 128)
    {
        var cachePath = GetCachePath(filePath, lastModified, maxDimension);
        if (File.Exists(cachePath))
            return File.ReadAllBytes(cachePath);

        var bytes = ThumbnailGenerator.GenerateThumbnail(filePath, maxDimension);
        if (bytes is not null)
            File.WriteAllBytes(cachePath, bytes);

        return bytes;
    }

    private string GetCachePath(string filePath, DateTime lastModified, int maxDimension)
    {
        var key = ComputeKey(filePath, lastModified, maxDimension);
        return Path.Combine(_cacheDirectory, $"{key}.png");
    }

    private static string ComputeKey(string filePath, DateTime lastModified, int maxDimension)
    {
        var input = $"{filePath.ToLowerInvariant()}|{lastModified.Ticks}|{maxDimension}";
        var hash  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
