using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

public sealed class DuplicateGroupViewModel : INotifyPropertyChanged
{
    private readonly bool _isExactMatch;

    public IReadOnlyList<FileActionViewModel> FileActions { get; }

    /// <summary>
    /// Computed rather than cached so it stays accurate after the user
    /// reassigns which file is kept (see DuplicatesViewModel.NotifyKeepChanged usage).
    /// </summary>
    public string Header
    {
        get
        {
            var loc = LocalizationService.Current;
            var kind = loc.GetString(_isExactMatch
                ? "Duplicates.GroupKind.Exact"
                : "Duplicates.GroupKind.NearDuplicate");
            var keepFile = FileActions.FirstOrDefault(f => f.SelectedActionType == ActionType.Keep);
            var keepName = keepFile is not null ? Path.GetFileName(keepFile.FilePath) : loc.GetString("Duplicates.NoneSelected");
            return loc.GetString("Duplicates.GroupHeader", FileActions.Count, kind, keepName);
        }
    }

    public DuplicateGroupViewModel(
        DuplicateGroup group,
        Dictionary<string, int> pathToFileRecordId)
    {
        _isExactMatch = group.IsExactMatch;

        FileActions = group.Files.Select(f =>
        {
            var isSuggested = string.Equals(
                f.FilePath, group.Suggested.FilePath, StringComparison.OrdinalIgnoreCase);
            pathToFileRecordId.TryGetValue(f.FilePath, out var fileRecordId);
            return new FileActionViewModel(fileRecordId, f.FilePath, isSuggested);
        }).ToList();
    }

    /// <summary>Call after a file's keep status may have changed so Header refreshes.</summary>
    public void NotifyKeepChanged() => Notify(nameof(Header));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
