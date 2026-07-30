using ImageCleanup.Core.Grouping;

namespace ImageCleanup.Core.Tests.Grouping;

public class KeepSelectorTests
{
    [Fact]
    public void ResolveKeepConflicts_NoOtherKeepFiles_ReturnsEmpty()
    {
        var actions = new (string, string)[]
        {
            ("a.jpg", "Keep"),
            ("b.jpg", "Delete"),
            ("c.jpg", "Delete"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "a.jpg");

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ResolveKeepConflicts_OneOtherKeepFile_ReturnsIt()
    {
        var actions = new (string, string)[]
        {
            ("a.jpg", "Keep"),
            ("b.jpg", "Keep"),
            ("c.jpg", "Delete"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "b.jpg");

        Assert.Equal(["a.jpg"], conflicts);
    }

    [Fact]
    public void ResolveKeepConflicts_MultipleOtherKeepFiles_ReturnsAll()
    {
        var actions = new (string, string)[]
        {
            ("a.jpg", "Keep"),
            ("b.jpg", "Keep"),
            ("c.jpg", "Keep"),
            ("d.jpg", "Delete"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "d.jpg");

        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, conflicts);
    }

    [Fact]
    public void ResolveKeepConflicts_NewKeepFileExcludedFromItsOwnConflictList()
    {
        var actions = new (string, string)[]
        {
            ("a.jpg", "Keep"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "a.jpg");

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ResolveKeepConflicts_PathComparisonIsCaseInsensitive()
    {
        var actions = new (string, string)[]
        {
            ("A.JPG", "Keep"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "a.jpg");

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ResolveKeepConflicts_IgnoresNonKeepFilesEntirely()
    {
        var actions = new (string, string)[]
        {
            ("a.jpg", "None"),
            ("b.jpg", "Move"),
            ("c.jpg", "Delete"),
        };

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, "d.jpg");

        Assert.Empty(conflicts);
    }
}
