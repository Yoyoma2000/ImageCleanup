namespace ImageCleanup.Core.Grouping;

/// <summary>
/// Enforces the invariant that at most one file per duplicate group is marked
/// "Keep". Extracted as pure logic so it stays unit-testable without the
/// WinUI-dependent ViewModels that call it.
/// </summary>
public static class KeepSelector
{
    /// <summary>The action string used to mark a file as the group's keep choice.</summary>
    public const string KeepAction = "Keep";

    /// <summary>
    /// Given every file's current action in a group and the file that was just
    /// set to Keep, returns the paths of any other files that were previously
    /// Keep and must be reset (since only one file per group may be kept at a time).
    /// </summary>
    public static IReadOnlyList<string> ResolveKeepConflicts(
        IEnumerable<(string FilePath, string Action)> currentActions,
        string newKeepFilePath)
    {
        return currentActions
            .Where(f => f.Action == KeepAction
                     && !string.Equals(f.FilePath, newKeepFilePath, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FilePath)
            .ToList();
    }
}
