namespace ImageCleanup.Core.Organization;

public enum OrganizationNodeKind
{
    Year,
    Month,
    Category,
    File,
}

/// <summary>
/// Pure, UI-framework-free representation of one OrganizationPlan tree node —
/// what a bindable ViewModel (e.g. a WinUI TreeView node) should display.
/// Kept separate from any WinUI-aware wrapper so the OrganizationPlan ->
/// display-node mapping (labels, file counts, rename detection) stays
/// unit-testable without a UI.
/// </summary>
public sealed class OrganizationTreeNode
{
    public OrganizationNodeKind Kind { get; init; }

    /// <summary>"2024" for Year, "March" for Month, "Photo"/"NoMetadata" for Category, the original filename for File.</summary>
    public string Label { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public IReadOnlyList<OrganizationTreeNode> Children { get; init; } = [];

    // ── File-level only ──────────────────────────────────────────────────
    public string? SourcePath { get; init; }
    public string? OriginalFileName { get; init; }
    public string? TargetFileName { get; init; }

    /// <summary>True when conflict resolution gave this file a different name than it started with.</summary>
    public bool WasRenamed => Kind == OrganizationNodeKind.File
        && !string.Equals(OriginalFileName, TargetFileName, StringComparison.Ordinal);

    /// <summary>"2024 (312 files)" for group nodes; just the original filename for File nodes.</summary>
    public string DisplayText => Kind == OrganizationNodeKind.File
        ? OriginalFileName ?? Label
        : $"{Label} ({FileCount} file{(FileCount == 1 ? "" : "s")})";
}
