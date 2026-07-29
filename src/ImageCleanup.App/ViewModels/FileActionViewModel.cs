using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace ImageCleanup.App.ViewModels;

public sealed class FileActionViewModel : INotifyPropertyChanged
{
    public static IReadOnlyList<string> AvailableActions { get; } = ["None", "Delete", "Move"];

    public int FileRecordId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public bool IsSuggested { get; init; }

    // XAML visibility helpers — no converter needed
    public Visibility SuggestedBadgeVisibility => IsSuggested ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionComboVisibility     => IsSuggested ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MoveTargetVisibility      => _selectedAction == "Move" ? Visibility.Visible : Visibility.Collapsed;

    private string _selectedAction;
    public string SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (_selectedAction == value) return;
            _selectedAction = value;
            Notify();
            Notify(nameof(MoveTargetVisibility));
            if (!_suppress) ActionChanged?.Invoke(this);
        }
    }

    private string? _targetPath;
    public string? TargetPath
    {
        get => _targetPath;
        set { if (_targetPath == value) return; _targetPath = value; Notify(); }
    }

    public int? StagingId { get; set; }

    /// <summary>Called when the user changes the action. Set by MainViewModel after construction.</summary>
    public Action<FileActionViewModel>? ActionChanged { get; set; }

    public FileActionViewModel(int fileRecordId, string filePath, bool isSuggested)
    {
        FileRecordId    = fileRecordId;
        FilePath        = filePath;
        IsSuggested     = isSuggested;
        _selectedAction = isSuggested ? "None" : "Delete";
    }

    /// <summary>Silently resets to None (e.g. when user removes the staging entry from the panel).</summary>
    internal void ResetToNone()
    {
        _suppress       = true;
        _selectedAction = "None";
        StagingId       = null;
        _suppress       = false;
        Notify(nameof(SelectedAction));
        Notify(nameof(MoveTargetVisibility));
    }

    private bool _suppress;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
