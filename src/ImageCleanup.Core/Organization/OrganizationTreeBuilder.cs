using System.Globalization;

namespace ImageCleanup.Core.Organization;

/// <summary>
/// Flattens an OrganizationPlan's Year/Month/Category/File groups into
/// OrganizationTreeNode instances a bindable ViewModel can wrap directly —
/// pure mapping, no UI dependency.
/// </summary>
public static class OrganizationTreeBuilder
{
    public static IReadOnlyList<OrganizationTreeNode> BuildTree(OrganizationPlan plan) =>
        plan.Years.Select(BuildYearNode).ToList();

    private static OrganizationTreeNode BuildYearNode(YearGroup year) => new()
    {
        Kind      = OrganizationNodeKind.Year,
        Label     = year.Label,
        FileCount = year.FileCount,
        Children  = year.Months.Select(BuildMonthNode).ToList(),
    };

    private static OrganizationTreeNode BuildMonthNode(MonthGroup month) => new()
    {
        Kind      = OrganizationNodeKind.Month,
        Label     = new DateTime(1, month.Month, 1).ToString("MMMM", CultureInfo.CurrentCulture),
        FileCount = month.FileCount,
        Children  = month.Categories.Select(BuildCategoryNode).ToList(),
    };

    private static OrganizationTreeNode BuildCategoryNode(CategoryGroup category) => new()
    {
        Kind      = OrganizationNodeKind.Category,
        Label     = category.Label,
        FileCount = category.FileCount,
        Children  = category.Files.Select(BuildFileNode).ToList(),
    };

    private static OrganizationTreeNode BuildFileNode(PlannedFile file)
    {
        var originalFileName = Path.GetFileName(file.SourcePath);
        return new OrganizationTreeNode
        {
            Kind             = OrganizationNodeKind.File,
            Label            = originalFileName,
            FileCount        = 1,
            SourcePath       = file.SourcePath,
            OriginalFileName = originalFileName,
            TargetFileName   = file.TargetFileName,
        };
    }
}
