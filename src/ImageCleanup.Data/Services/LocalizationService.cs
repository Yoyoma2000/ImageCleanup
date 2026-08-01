using System.Text.Json;
using ImageCleanup.Data.Models;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Loads a language's string dictionary and resolves keys, with a fallback
/// to the Dev dictionary so a missing key (expected for English/Chinese
/// until they're actually translated) never shows a raw key or blank text.
///
/// Dictionaries are app-bundled content (one flat JSON key->string map per
/// language — dev.json/en.json/zh.json), not user data, so they live under
/// the App project's Strings/ folder and ship alongside the exe rather than
/// in %LOCALAPPDATA% the way settings.json does. This service only needs a
/// directory to read from, so it stays in Data (same layer/style as
/// SettingsService/OrganizationExecutor) with the directory resolved by the
/// caller — defaults to "Strings" next to the running exe.
/// </summary>
public sealed class LocalizationService
{
    /// <summary>
    /// Static accessor so XAML markup extensions (which the XAML parser
    /// constructs directly, with no DI) can resolve strings without a
    /// reference to the DI container. Set once at app startup to the
    /// DI-registered singleton instance — see App.xaml.cs.
    /// </summary>
    public static LocalizationService Current { get; set; } = new();

    private readonly string _stringsDirectory;
    private Dictionary<string, string> _dev = new();
    private Dictionary<string, string> _active = new();

    public AppLanguage Language { get; private set; } = AppLanguage.Dev;

    public LocalizationService(string? stringsDirectory = null)
    {
        _stringsDirectory = stringsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Strings");

        // Self-initializing default: even if nothing ever calls SetLanguage
        // (e.g. a page constructed outside the normal App.OnLaunched startup
        // path), GetString still has a real Dev dictionary loaded rather
        // than returning raw keys everywhere.
        SetLanguage(AppLanguage.Dev);
    }

    /// <summary>
    /// Loads <paramref name="language"/>'s dictionary (and Dev's, always,
    /// for fallback) and makes it the active one for GetString.
    /// </summary>
    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        _dev = LoadDictionary(AppLanguage.Dev);
        _active = language == AppLanguage.Dev ? _dev : LoadDictionary(language);
    }

    /// <summary>
    /// Resolves a key in the active language, falling back to Dev's value
    /// for that key if missing/empty (expected for English/Chinese until
    /// they're translated), and finally to the raw key itself if even Dev
    /// doesn't have it (should never happen once Dev is fully populated —
    /// this last resort only guards against a typo'd key, never a blank
    /// control).
    /// </summary>
    public string GetString(string key)
    {
        if (_active.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;

        if (_dev.TryGetValue(key, out var devValue) && !string.IsNullOrEmpty(devValue))
            return devValue;

        return key;
    }

    /// <summary>Convenience for templated strings, e.g. GetString("Duplicates.FilesFound", count).</summary>
    public string GetString(string key, params object[] args) =>
        string.Format(GetString(key), args);

    private Dictionary<string, string> LoadDictionary(AppLanguage language)
    {
        var path = Path.Combine(_stringsDirectory, FileNameFor(language));
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            // Corrupt/unreadable dictionary file — fall back to an empty
            // map rather than crash; GetString's Dev fallback (or the raw
            // key, worst case) still keeps the UI functional.
            return new Dictionary<string, string>();
        }
    }

    private static string FileNameFor(AppLanguage language) => language switch
    {
        AppLanguage.English => "en.json",
        AppLanguage.Chinese => "zh.json",
        _                   => "dev.json",
    };
}
