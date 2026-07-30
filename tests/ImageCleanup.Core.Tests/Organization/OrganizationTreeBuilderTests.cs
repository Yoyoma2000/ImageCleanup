using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Organization;

namespace ImageCleanup.Core.Tests.Organization;

public class OrganizationTreeBuilderTests
{
    [Fact]
    public void BuildTree_EmptyPlan_ReturnsEmptyList()
    {
        var plan = OrganizationPlanner.BuildHierarchy([]);

        var tree = OrganizationTreeBuilder.BuildTree(plan);

        Assert.Empty(tree);
    }

    [Fact]
    public void BuildTree_YearNode_HasCorrectKindLabelAndDisplayText()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\a.jpg", new DateTime(2024, 3, 1)),
            MakeRecord(@"C:\Photos\b.jpg", new DateTime(2024, 3, 2)),
        ]);

        var tree = OrganizationTreeBuilder.BuildTree(plan);
        var yearNode = Assert.Single(tree);

        Assert.Equal(OrganizationNodeKind.Year, yearNode.Kind);
        Assert.Equal("2024", yearNode.Label);
        Assert.Equal(2, yearNode.FileCount);
        Assert.Equal("2024 (2 files)", yearNode.DisplayText);
    }

    [Fact]
    public void BuildTree_MonthNode_UsesMonthNameNotNumber()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\a.jpg", new DateTime(2024, 3, 1)),
        ]);

        var monthNode = OrganizationTreeBuilder.BuildTree(plan).Single().Children.Single();

        Assert.Equal(OrganizationNodeKind.Month, monthNode.Kind);
        Assert.Equal("March", monthNode.Label);
        Assert.Equal(1, monthNode.FileCount);
        Assert.Equal("March (1 file)", monthNode.DisplayText);
    }

    [Fact]
    public void BuildTree_CategoryNode_LabelMatchesMetadataCategory()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\real.jpg", new DateTime(2024, 3, 1), hasExif: true),
            MakeRecord(@"C:\Photos\shot.png", null, lastModified: new DateTime(2024, 3, 2), hasExif: false),
        ]);

        var categoryNodes = OrganizationTreeBuilder.BuildTree(plan).Single().Children.Single().Children;

        Assert.Equal(2, categoryNodes.Count);
        Assert.Contains(categoryNodes, n => n.Kind == OrganizationNodeKind.Category && n.Label == "Photo" && n.FileCount == 1);
        Assert.Contains(categoryNodes, n => n.Kind == OrganizationNodeKind.Category && n.Label == "NoMetadata" && n.FileCount == 1);
    }

    [Fact]
    public void BuildTree_FileNode_CarriesSourceAndTargetNames_NotRenamedWhenNoConflict()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\IMG_0001.jpg", new DateTime(2024, 3, 1)),
        ]);

        var fileNode = OrganizationTreeBuilder.BuildTree(plan).Single().Children.Single().Children.Single().Children.Single();

        Assert.Equal(OrganizationNodeKind.File, fileNode.Kind);
        Assert.Equal(@"C:\Photos\IMG_0001.jpg", fileNode.SourcePath);
        Assert.Equal("IMG_0001.jpg", fileNode.OriginalFileName);
        Assert.Equal("IMG_0001.jpg", fileNode.TargetFileName);
        Assert.False(fileNode.WasRenamed);
        Assert.Equal("IMG_0001.jpg", fileNode.DisplayText);
        Assert.Equal(1, fileNode.FileCount);
        Assert.Empty(fileNode.Children);
    }

    [Fact]
    public void BuildTree_FileNode_WasRenamedTrue_WhenConflictResolutionChangedTheName()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\FolderA\IMG_0001.jpg", new DateTime(2024, 3, 1)),
            MakeRecord(@"C:\Photos\FolderB\IMG_0001.jpg", new DateTime(2024, 3, 2)),
        ]);

        var fileNodes = OrganizationTreeBuilder.BuildTree(plan).Single().Children.Single().Children.Single().Children;

        Assert.False(fileNodes[0].WasRenamed);
        Assert.Equal("IMG_0001.jpg", fileNodes[0].TargetFileName);

        Assert.True(fileNodes[1].WasRenamed);
        Assert.Equal("IMG_0001 (from FolderB).jpg", fileNodes[1].TargetFileName);
        Assert.Equal("IMG_0001.jpg", fileNodes[1].OriginalFileName);
    }

    private static ImageRecord MakeRecord(
        string filePath,
        DateTime? dateTaken,
        DateTime? lastModified = null,
        bool hasExif = true) => new()
    {
        FilePath     = filePath,
        FileHash     = Guid.NewGuid().ToString("N"),
        FileSize     = 1,
        LastModified = lastModified ?? new DateTime(2000, 1, 1),
        DateTaken    = dateTaken,
        HasExif      = hasExif,
    };
}
