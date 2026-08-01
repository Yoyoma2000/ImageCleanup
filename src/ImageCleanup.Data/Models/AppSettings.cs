namespace ImageCleanup.Data.Models;

/// <summary>User-facing theme preference. System defers to the OS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Persisted app-wide preferences — see SettingsService for load/save.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
}
