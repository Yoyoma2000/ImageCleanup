namespace ImageCleanup.Core.Grouping;

/// <summary>
/// Enforces the invariant that at most one file per duplicate group is marked
/// Keep. Extracted as pure logic so it stays unit-testable without the
/// WinUI-dependent ViewModels that call it.
/// </summary>
public static class KeepSelector
{
    /// <summary>
    /// Given every file's current action in a group and the file that was just
    /// set to Keep, returns the paths of any other files that were previously
    /// Keep and must be reset (since only one file per group may be kept at a time).
    /// Compares ActionType, the stable/language-independent value — never a
    /// display string, which may be translated.
    /// </summary>
    public static IReadOnlyList<string> ResolveKeepConflicts(
        IEnumerable<(string FilePath, ActionType Action)> currentActions,
        string newKeepFilePath)
    {
        return currentActions
            .Where(f => f.Action == ActionType.Keep
                     && !string.Equals(f.FilePath, newKeepFilePath, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FilePath)
            .ToList();
    }
}
