using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.Data.Tests.Services;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _dir = CreateTempDir();

    public LocalizationServiceTests()
    {
        File.WriteAllText(Path.Combine(_dir, "dev.json"), """
            {
                "Greeting": "Hello",
                "OnlyInDev": "Dev-only value"
            }
            """);

        File.WriteAllText(Path.Combine(_dir, "en.json"), """
            {
                "Greeting": "Hi there"
            }
            """);

        // Chinese intentionally ships empty in this pass — exercises the
        // fallback-to-Dev path for every key, not just a missing one.
        File.WriteAllText(Path.Combine(_dir, "zh.json"), "{}");
    }

    public void Dispose() => TryDelete(_dir);

    [Fact]
    public void GetString_DevLanguage_ReturnsDevValue()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.Dev);

        Assert.Equal("Hello", service.GetString("Greeting"));
    }

    [Fact]
    public void GetString_EnglishLanguage_KeyPresent_ReturnsEnglishValue()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.English);

        Assert.Equal("Hi there", service.GetString("Greeting"));
    }

    [Fact]
    public void GetString_EnglishLanguage_KeyMissing_FallsBackToDevValue()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.English);

        // "OnlyInDev" isn't in en.json — must fall back to Dev's value, not
        // a blank string and not the raw key.
        Assert.Equal("Dev-only value", service.GetString("OnlyInDev"));
    }

    [Fact]
    public void GetString_ChineseLanguage_EmptyDictionary_FallsBackToDevForEveryKey()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.Chinese);

        Assert.Equal("Hello", service.GetString("Greeting"));
        Assert.Equal("Dev-only value", service.GetString("OnlyInDev"));
    }

    [Fact]
    public void GetString_KeyMissingEverywhere_ReturnsRawKeyAsLastResort()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.Dev);

        Assert.Equal("Nonexistent.Key", service.GetString("Nonexistent.Key"));
    }

    [Fact]
    public void GetString_WithFormatArgs_FormatsTheResolvedTemplate()
    {
        File.WriteAllText(Path.Combine(_dir, "dev.json"), """
            { "Template": "Found {0} of {1}" }
            """);
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.Dev);

        Assert.Equal("Found 3 of 10", service.GetString("Template", 3, 10));
    }

    [Fact]
    public void Constructor_NoStringsDirectoryYet_DoesNotThrow_AndFallsBackToRawKey()
    {
        var missingDir = Path.Combine(_dir, "does-not-exist");

        var service = new LocalizationService(missingDir);

        Assert.Equal("Anything", service.GetString("Anything"));
    }

    [Fact]
    public void SetLanguage_SwitchingBackToDev_ReturnsDevValueAgain()
    {
        var service = new LocalizationService(_dir);
        service.SetLanguage(AppLanguage.English);
        Assert.Equal("Hi there", service.GetString("Greeting"));

        service.SetLanguage(AppLanguage.Dev);

        Assert.Equal("Hello", service.GetString("Greeting"));
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LocalizationServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
