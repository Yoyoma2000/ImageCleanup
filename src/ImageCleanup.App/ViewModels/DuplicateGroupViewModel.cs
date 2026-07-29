using ImageCleanup.Core.Grouping;

namespace ImageCleanup.App.ViewModels;

public sealed class DuplicateGroupViewModel
{
    public string Header { get; }
    public IReadOnlyList<FileActionViewModel> FileActions { get; }

    public DuplicateGroupViewModel(
        DuplicateGroup group,
        Dictionary<string, int> pathToFileRecordId)
    {
        var kind = group.IsExactMatch ? "exact" : "near-dup";
        Header = $"{group.Files.Count} files ({kind}) — Keep: {Path.GetFileName(group.Suggested.FilePath)}";

        FileActions = group.Files.Select(f =>
        {
            var isSuggested = string.Equals(
                f.FilePath, group.Suggested.FilePath, StringComparison.OrdinalIgnoreCase);
            pathToFileRecordId.TryGetValue(f.FilePath, out var fileRecordId);
            return new FileActionViewModel(fileRecordId, f.FilePath, isSuggested);
        }).ToList();
    }
}
