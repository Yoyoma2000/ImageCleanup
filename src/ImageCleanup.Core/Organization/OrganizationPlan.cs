using ImageCleanup.Core.Metadata;

namespace ImageCleanup.Core.Organization;

/// <summary>
/// A single file's computed place in the proposed Year/Month/MetadataCategory
/// hierarchy. Planning only — nothing here moves or touches the filesystem.
/// </summary>
public sealed class PlannedFile
{
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Final filename within TargetFolder, with any conflict-resolution suffix already applied.</summary>
    public string TargetFileName { get; init; } = string.Empty;

    /// <summary>Folder path relative to the organization root, e.g. "2024/03/Photo".</summary>
    public string TargetFolder { get; init; } = string.Empty;

    /// <summary>TargetFolder + TargetFileName, e.g. "2024/03/Photo/IMG_0001.jpg".</summary>
    public string TargetPath => $"{TargetFolder}/{TargetFileName}";
}

/// <summary>Innermost level of the plan — one MetadataCategory's files within a given Year/Month.</summary>
public sealed class CategoryGroup
{
    public MetadataCategory Category { get; init; }

    /// <summary>
    /// Display/folder label, e.g. "Photo" — defaults to Category.ToString()
    /// but can be overridden (see OrganizationPlanner.BuildHierarchy's
    /// categoryFolderName parameter) so a caller can supply a localized
    /// name. This is the same string used for the real on-disk folder name
    /// (see PlannedFile.TargetFolder), not just display.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<PlannedFile> Files { get; init; } = [];

    public int FileCount => Files.Count;
}

/// <summary>One calendar month within a YearGroup, split into its CategoryGroups.</summary>
public sealed class MonthGroup
{
    /// <summary>1-12.</summary>
    public int Month { get; init; }

    /// <summary>Display label for a future TreeView node, e.g. "03".</summary>
    public string Label => Month.ToString("D2");

    public IReadOnlyList<CategoryGroup> Categories { get; init; } = [];

    public int FileCount => Categories.Sum(c => c.FileCount);
}

/// <summary>One calendar year, split into its MonthGroups.</summary>
public sealed class YearGroup
{
    public int Year { get; init; }

    /// <summary>Display label for a future TreeView node, e.g. "2024".</summary>
    public string Label => Year.ToString();

    public IReadOnlyList<MonthGroup> Months { get; init; } = [];

    public int FileCount => Months.Sum(m => m.FileCount);
}

/// <summary>
/// The full proposed Year/Month/MetadataCategory hierarchy for a set of
/// scanned files — a preview tree a future Organization UI can bind to
/// directly (each node has a Label, a FileCount, and either child nodes or
/// a file list). Building this plan never touches disk; nothing is moved.
/// </summary>
public sealed class OrganizationPlan
{
    public IReadOnlyList<YearGroup> Years { get; init; } = [];

    public int FileCount => Years.Sum(y => y.FileCount);
}
