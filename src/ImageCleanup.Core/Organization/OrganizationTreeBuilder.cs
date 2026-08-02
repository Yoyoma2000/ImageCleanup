using System.Globalization;

namespace ImageCleanup.Core.Organization;

/// <summary>
/// Flattens an OrganizationPlan's Year/Month/Category/File groups into
/// OrganizationTreeNode instances a bindable ViewModel can wrap directly —
/// pure mapping, no UI dependency.
/// </summary>
public static class OrganizationTreeBuilder
{
    /// <summary>
    /// Flattens the plan. <paramref name="monthName"/> resolves a 1-12
    /// month number to its display name for Month nodes' Label — omit it
    /// (or pass null) for the original behavior, the current culture's
    /// month name via CultureInfo, which is what every existing
    /// caller/test still gets automatically. <paramref
    /// name="formatGroupDisplayText"/> resolves a group node's (label,
    /// fileCount) to its full DisplayText (e.g. "2024 (312 files)") —
    /// omit it for the original English "{label} ({count} file(s))"
    /// wording. A caller can supply localized versions of both (e.g. from
    /// LocalizationService) instead; see the App layer's
    /// OrganizationViewModel for that wiring — same pattern
    /// OrganizationPlanner.BuildHierarchy's categoryFolderName parameter
    /// already uses for the Photo/NoMetadata folder names.
    /// </summary>
    public static IReadOnlyList<OrganizationTreeNode> BuildTree(
        OrganizationPlan plan,
        Func<int, string>? monthName = null,
        Func<string, int, string>? formatGroupDisplayText = null)
    {
        var resolveMonthName = monthName ?? (month => new DateTime(1, month, 1).ToString("MMMM", CultureInfo.CurrentCulture));
        var formatGroup = formatGroupDisplayText ?? ((label, fileCount) => $"{label} ({fileCount} file{(fileCount == 1 ? "" : "s")})");

        return plan.Years.Select(year => BuildYearNode(year, resolveMonthName, formatGroup)).ToList();
    }

    private static OrganizationTreeNode BuildYearNode(YearGroup year, Func<int, string> monthName, Func<string, int, string> formatGroup) => new()
    {
        Kind        = OrganizationNodeKind.Year,
        Label       = year.Label,
        FileCount   = year.FileCount,
        DisplayText = formatGroup(year.Label, year.FileCount),
        Children    = year.Months.Select(m => BuildMonthNode(m, monthName, formatGroup)).ToList(),
    };

    private static OrganizationTreeNode BuildMonthNode(MonthGroup month, Func<int, string> monthName, Func<string, int, string> formatGroup)
    {
        var label = monthName(month.Month);
        return new OrganizationTreeNode
        {
            Kind        = OrganizationNodeKind.Month,
            Label       = label,
            FileCount   = month.FileCount,
            DisplayText = formatGroup(label, month.FileCount),
            Children    = month.Categories.Select(c => BuildCategoryNode(c, formatGroup)).ToList(),
        };
    }

    private static OrganizationTreeNode BuildCategoryNode(CategoryGroup category, Func<string, int, string> formatGroup) => new()
    {
        Kind        = OrganizationNodeKind.Category,
        Label       = category.Label,
        FileCount   = category.FileCount,
        DisplayText = formatGroup(category.Label, category.FileCount),
        Children    = category.Files.Select(BuildFileNode).ToList(),
    };

    private static OrganizationTreeNode BuildFileNode(PlannedFile file)
    {
        var originalFileName = Path.GetFileName(file.SourcePath);
        return new OrganizationTreeNode
        {
            Kind             = OrganizationNodeKind.File,
            Label            = originalFileName,
            FileCount        = 1,
            DisplayText      = originalFileName,
            SourcePath       = file.SourcePath,
            OriginalFileName = originalFileName,
            TargetFileName   = file.TargetFileName,
        };
    }
}
