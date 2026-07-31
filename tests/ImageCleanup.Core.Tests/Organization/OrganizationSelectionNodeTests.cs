using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Organization;

namespace ImageCleanup.Core.Tests.Organization;

/// <summary>
/// Cascading tree-checkbox selection logic (Organization's selective-move
/// feature) is deliberately implemented in a pure, WinUI-free class
/// (OrganizationSelectionNode) precisely so it can be unit-tested here
/// without a TreeView control — see that class's doc comment for the
/// derive-on-read design that makes "cascade down / recompute up" trivially
/// consistent with no explicit synchronization.
/// </summary>
public sealed class OrganizationSelectionNodeTests
{
    // Shared fixture: Year(2024) > Month(March) > [Category(Photo) = [A1, A2], Category(NoMetadata) = [B1]]
    private static (OrganizationTreeNode Year, OrganizationSelectionNode Selection) BuildFixture()
    {
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(@"C:\Photos\A1.jpg", new DateTime(2024, 3, 1), hasExif: true),
            MakeRecord(@"C:\Photos\A2.jpg", new DateTime(2024, 3, 2), hasExif: true),
            MakeRecord(@"C:\Photos\B1.png", null, lastModified: new DateTime(2024, 3, 3), hasExif: false),
        ]);

        var year = OrganizationTreeBuilder.BuildTree(plan).Single();
        return (year, new OrganizationSelectionNode(year));
    }

    private static OrganizationSelectionNode Month(OrganizationSelectionNode year) => year.Children.Single();
    private static OrganizationSelectionNode PhotoCategory(OrganizationSelectionNode year) =>
        Month(year).Children.Single(c => c.Node.Label == "Photo");
    private static OrganizationSelectionNode NoMetadataCategory(OrganizationSelectionNode year) =>
        Month(year).Children.Single(c => c.Node.Label == "NoMetadata");

    // ── Default state ────────────────────────────────────────────────────

    [Fact]
    public void AllNodes_DefaultToSelected_NoneIndeterminate()
    {
        var (_, year) = BuildFixture();

        void AssertFullySelected(OrganizationSelectionNode node)
        {
            Assert.True(node.IsSelected);
            Assert.False(node.IsIndeterminate);
            Assert.Equal(true, node.CheckBoxState);
            foreach (var child in node.Children)
                AssertFullySelected(child);
        }

        AssertFullySelected(year);
    }

    // ── Cascade down ─────────────────────────────────────────────────────

    [Fact]
    public void SetSelected_False_OnCategory_CascadesToAllDescendantFiles()
    {
        var (_, year) = BuildFixture();
        var photo = PhotoCategory(year);

        photo.SetSelected(false);

        Assert.All(photo.Children, f => Assert.False(f.IsSelected));
        Assert.False(photo.IsSelected);
        Assert.False(photo.IsIndeterminate); // fully unchecked, not partial
    }

    [Fact]
    public void SetSelected_False_OnYear_CascadesEverything()
    {
        var (_, year) = BuildFixture();

        year.SetSelected(false);

        Assert.Empty(year.SelectedFileNodes());
        Assert.False(year.IsSelected);
        Assert.False(year.IsIndeterminate);
        Assert.False(PhotoCategory(year).IsSelected);
        Assert.False(NoMetadataCategory(year).IsSelected);
    }

    [Fact]
    public void SetSelected_True_AfterFullDeselect_ReselectsEverything()
    {
        var (_, year) = BuildFixture();
        year.SetSelected(false);

        year.SetSelected(true);

        Assert.True(year.IsSelected);
        Assert.False(year.IsIndeterminate);
        Assert.Equal(3, year.SelectedFileNodes().Count());
    }

    // ── Cascade up (derived, never stored) ──────────────────────────────

    [Fact]
    public void SetSelected_False_OnSingleFile_DoesNotUncheckParent_ShowsIndeterminateInstead()
    {
        var (_, year) = BuildFixture();
        var photo = PhotoCategory(year);
        var fileA1 = photo.Children.Single(f => f.Node.SourcePath == @"C:\Photos\A1.jpg");

        fileA1.SetSelected(false);

        // The category itself is not "fully selected" anymore...
        Assert.False(photo.IsSelected);
        // ...but it must render Indeterminate, not Unchecked, since A2 is
        // still selected — this is the "don't uncheck the parent" guarantee.
        Assert.True(photo.IsIndeterminate);
        Assert.Null(photo.CheckBoxState);
    }

    [Fact]
    public void SetSelected_False_OnSingleFile_BubblesIndeterminateUpToMonthAndYear()
    {
        var (_, year) = BuildFixture();
        var photo = PhotoCategory(year);
        photo.Children.Single(f => f.Node.SourcePath == @"C:\Photos\A1.jpg").SetSelected(false);

        var month = Month(year);
        Assert.True(month.IsIndeterminate);
        Assert.False(month.IsSelected);

        Assert.True(year.IsIndeterminate);
        Assert.False(year.IsSelected);
    }

    [Fact]
    public void SetSelected_False_OnSingleFile_DoesNotAffectSiblingCategory()
    {
        var (_, year) = BuildFixture();
        PhotoCategory(year).Children.Single(f => f.Node.SourcePath == @"C:\Photos\A1.jpg").SetSelected(false);

        var noMetadata = NoMetadataCategory(year);
        Assert.True(noMetadata.IsSelected);
        Assert.False(noMetadata.IsIndeterminate);
    }

    // ── SelectedFileNodes ────────────────────────────────────────────────

    [Fact]
    public void SelectedFileNodes_ReturnsOnlyStillSelectedFiles_AfterPartialDeselection()
    {
        var (_, year) = BuildFixture();
        PhotoCategory(year).Children.Single(f => f.Node.SourcePath == @"C:\Photos\A1.jpg").SetSelected(false);

        var selectedPaths = year.SelectedFileNodes().Select(n => n.SourcePath).ToHashSet();

        Assert.Equal(2, selectedPaths.Count);
        Assert.DoesNotContain(@"C:\Photos\A1.jpg", selectedPaths);
        Assert.Contains(@"C:\Photos\A2.jpg", selectedPaths);
        Assert.Contains(@"C:\Photos\B1.png", selectedPaths);
    }

    [Fact]
    public void SelectedFileNodes_Empty_WhenCategoryFullyDeselected()
    {
        var (_, year) = BuildFixture();
        PhotoCategory(year).SetSelected(false);

        var selectedPaths = year.SelectedFileNodes().Select(n => n.SourcePath).ToHashSet();

        Assert.Single(selectedPaths);
        Assert.Contains(@"C:\Photos\B1.png", selectedPaths);
    }

    // ── Re-selecting a partially-deselected group ───────────────────────

    [Fact]
    public void SetSelected_True_OnPartiallySelectedCategory_ReselectsAllChildren()
    {
        var (_, year) = BuildFixture();
        var photo = PhotoCategory(year);
        photo.Children.Single(f => f.Node.SourcePath == @"C:\Photos\A1.jpg").SetSelected(false);
        Assert.True(photo.IsIndeterminate); // sanity check on setup

        photo.SetSelected(true);

        Assert.True(photo.IsSelected);
        Assert.False(photo.IsIndeterminate);
        Assert.All(photo.Children, f => Assert.True(f.IsSelected));
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
