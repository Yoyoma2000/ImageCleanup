using ImageCleanup.Core.Grouping;

namespace ImageCleanup.App.ViewModels;

public sealed class DuplicateGroupViewModel
{
    public string Header { get; }
    public string SuggestedPath { get; }
    public IReadOnlyList<string> OtherFiles { get; }

    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        SuggestedPath = group.Suggested.FilePath;

        var kind = group.IsExactMatch ? "exact" : "near-dup";
        Header = $"{group.Files.Count} files ({kind}) — Keep: {Path.GetFileName(SuggestedPath)}";

        OtherFiles = group.Files
            .Where(f => !string.Equals(f.FilePath, SuggestedPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FilePath)
            .ToList();
    }
}
