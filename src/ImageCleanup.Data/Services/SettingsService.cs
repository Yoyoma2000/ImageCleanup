using System.Text.Json;
using ImageCleanup.Data.Models;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Loads and saves AppSettings as JSON under
/// %LOCALAPPDATA%\ImageCleanup\settings.json. A missing or unreadable file
/// (first launch, corrupt write) just falls back to defaults rather than
/// throwing — same "don't fail the app over a non-critical read" philosophy
/// used elsewhere (OrganizationUndoService's corrupt-log skip, etc.).
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService(string? settingsDirectory = null)
    {
        var dir = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
