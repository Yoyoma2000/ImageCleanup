using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Data.Models;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Settings page — theme preference and language/wording mode (both
/// applied immediately, persisted to settings.json) plus the two
/// destructive maintenance actions (Clear Cache / Clear Move History). The
/// actual confirmation dialogs live in SettingsPage's code-behind, same
/// pattern as every other feature's commit/execute confirmations; this
/// ViewModel only does the work once the user has confirmed.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;

    private static readonly AppTheme[] ThemeByIndex = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    private static readonly AppLanguage[] LanguageByIndex = [AppLanguage.Dev, AppLanguage.English, AppLanguage.Chinese];

    // Kept as the single source of truth for what's persisted — Theme and
    // Language setters both mutate this same instance and save it whole,
    // rather than each constructing its own `new AppSettings { ... }` (which
    // would silently reset whichever field it didn't set back to its
    // default on every save — a real bug once there were two fields).
    private readonly AppSettings _settings;

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            Notify();
            Notify(nameof(ThemeIndex));

            _settingsService.Save(_settings);
            App.ApplyTheme(value);
        }
    }

    /// <summary>
    /// Int mirror of Theme for RadioButtons.SelectedIndex binding — same
    /// pattern as FileActionViewModel.SelectedActionIndex, avoiding any
    /// by-value re-match against an ItemsSource.
    /// </summary>
    public int ThemeIndex
    {
        get => Array.IndexOf(ThemeByIndex, _settings.Theme);
        set
        {
            if (value < 0 || value >= ThemeByIndex.Length) return;
            Theme = ThemeByIndex[value];
        }
    }

    public AppLanguage Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language == value) return;
            _settings.Language = value;
            Notify();
            Notify(nameof(LanguageIndex));

            _settingsService.Save(_settings);

            // Updates the loaded dictionary immediately — any ContentDialog
            // built fresh from here on (they're constructed in code-behind
            // per show, not cached like Pages) reflects the new language
            // right away. Already-rendered Page XAML bound via {loc:Loc}
            // resolved once at construction and won't refresh until the app
            // restarts — see LocExtension's remarks for why this genuinely
            // differs from theme switching rather than being an oversight.
            _localizationService.SetLanguage(value);

            StatusText = _localizationService.GetString("Settings.LanguageHint");
        }
    }

    /// <summary>Int mirror of Language for RadioButtons.SelectedIndex binding — same reasoning as ThemeIndex.</summary>
    public int LanguageIndex
    {
        get => Array.IndexOf(LanguageByIndex, _settings.Language);
        set
        {
            if (value < 0 || value >= LanguageByIndex.Length) return;
            Language = LanguageByIndex[value];
        }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SettingsService settingsService, LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _settings = _settingsService.Load();
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
            StatusText = _localizationService.GetString("Settings.ClearCacheDoneStatus");
        }
        catch (Exception ex)
        {
            StatusText = _localizationService.GetString("Settings.ClearCacheFailedStatus", ex.Message);
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
            StatusText = _localizationService.GetString("Settings.ClearMoveHistoryDoneStatus");
        }
        catch (Exception ex)
        {
            StatusText = _localizationService.GetString("Settings.ClearMoveHistoryFailedStatus", ex.Message);
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
