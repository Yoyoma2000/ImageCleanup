using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.Data.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = CreateTempDir();

    public void Dispose() => TryDelete(_dir);

    [Fact]
    public void Load_NoFileYet_ReturnsDefaults()
    {
        var settings = new SettingsService(_dir).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheme()
    {
        var service = new SettingsService(_dir);
        service.Save(new AppSettings { Theme = AppTheme.Dark });

        var loaded = service.Load();

        Assert.Equal(AppTheme.Dark, loaded.Theme);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not valid json");

        var settings = new SettingsService(_dir).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SettingsServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
