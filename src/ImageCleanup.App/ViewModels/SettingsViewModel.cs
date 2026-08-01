using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Settings page — theme preference (applied immediately, persisted to
/// settings.json) plus the two destructive maintenance actions (Clear
/// Cache / Clear Move History). The actual confirmation dialogs live in
/// SettingsPage's code-behind, same pattern as every other feature's
/// commit/execute confirmations; this ViewModel only does the work once
/// the user has confirmed.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;

    private static readonly AppTheme[] ThemeByIndex = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    private AppTheme _theme;
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            Notify();
            Notify(nameof(ThemeIndex));

            _settingsService.Save(new AppSettings { Theme = _theme });
            App.ApplyTheme(_theme);
        }
    }

    /// <summary>
    /// Int mirror of Theme for RadioButtons.SelectedIndex binding — same
    /// pattern as FileActionViewModel.SelectedActionIndex, avoiding any
    /// by-value re-match against an ItemsSource.
    /// </summary>
    public int ThemeIndex
    {
        get => Array.IndexOf(ThemeByIndex, _theme);
        set
        {
            if (value < 0 || value >= ThemeByIndex.Length) return;
            Theme = ThemeByIndex[value];
        }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _theme = _settingsService.Load().Theme;
    }

    /// <summary>Deletes the SQLite file cache — forces a full rescan next time a folder is scanned.</summary>
    public void ClearCache()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "cache.db");

        try
        {
            if (File.Exists(path)) File.Delete(path);
            StatusText = "Cache cleared — the next scan will rebuild it from scratch.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't clear cache: {ex.Message}";
        }
    }

    /// <summary>Deletes every Organization move log — removes the undo safety net for every past move.</summary>
    public void ClearMoveHistory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "move-logs");

        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "move-log_*.json"))
                    File.Delete(file);
            }
            StatusText = "Move history cleared — past Organization moves can no longer be undone.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't clear move history: {ex.Message}";
        }
    }

    /// <summary>Count of move logs currently on disk — shown in the Clear Move History confirmation.</summary>
    public int CountMoveLogs()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "move-logs");

        if (!Directory.Exists(dir)) return 0;
        return Directory.EnumerateFiles(dir, "move-log_*.json").Count();
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
