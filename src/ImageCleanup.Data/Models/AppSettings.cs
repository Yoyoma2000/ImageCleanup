namespace ImageCleanup.Data.Models;

/// <summary>User-facing theme preference. System defers to the OS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// User-facing language/wording mode. Dev is the literal current app
/// wording (technical terms, unchanged) — the default, so anyone who never
/// touches this setting sees exactly today's behavior. English is a
/// plain-language rewrite of the same strings; Chinese is a translation.
/// See Data.Services.LocalizationService for how a language's dictionary is
/// loaded and how missing keys fall back to Dev.
/// </summary>
public enum AppLanguage
{
    Dev,
    English,
    Chinese,
}

/// <summary>Persisted app-wide preferences — see SettingsService for load/save.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = AppLanguage.Dev;
}
