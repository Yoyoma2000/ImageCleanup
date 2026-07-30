using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Metadata;
using ImageCleanup.Core.Organization;

namespace ImageCleanup.Core.Tests.Organization;

public class OrganizationPlannerTests
{
    [Fact]
    public void BuildHierarchy_EmptyInput_ReturnsEmptyPlan()
    {
        var plan = OrganizationPlanner.BuildHierarchy([]);

        Assert.Empty(plan.Years);
        Assert.Equal(0, plan.FileCount);
    }

    [Fact]
    public void BuildHierarchy_GroupsByYearAndMonth()
    {
        var records = new[]
        {
            MakeRecord(@"C:\Photos\a.jpg", dateTaken: new DateTime(2023, 6, 1)),
            MakeRecord(@"C:\Photos\b.jpg", dateTaken: new DateTime(2024, 3, 1)),
            MakeRecord(@"C:\Photos\c.jpg", dateTaken: new DateTime(2024, 3, 15)),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        Assert.Equal(3, plan.FileCount);
        Assert.Equal(2, plan.Years.Count);

        var year2023 = plan.Years.Single(y => y.Year == 2023);
        Assert.Equal("2023", year2023.Label);
        Assert.Single(year2023.Months);
        Assert.Equal(6, year2023.Months[0].Month);
        Assert.Equal("06", year2023.Months[0].Label);
        Assert.Equal(1, year2023.FileCount);

        var year2024 = plan.Years.Single(y => y.Year == 2024);
        Assert.Single(year2024.Months);
        Assert.Equal(3, year2024.Months[0].Month);
        Assert.Equal(2, year2024.Months[0].FileCount);
    }

    [Fact]
    public void BuildHierarchy_YearsAndMonthsAreOrdered()
    {
        var records = new[]
        {
            MakeRecord(@"C:\Photos\a.jpg", dateTaken: new DateTime(2024, 12, 1)),
            MakeRecord(@"C:\Photos\b.jpg", dateTaken: new DateTime(2022, 1, 1)),
            MakeRecord(@"C:\Photos\c.jpg", dateTaken: new DateTime(2024, 2, 1)),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        Assert.Equal([2022, 2024], plan.Years.Select(y => y.Year));
        var year2024 = plan.Years.Single(y => y.Year == 2024);
        Assert.Equal([2, 12], year2024.Months.Select(m => m.Month));
    }

    [Fact]
    public void BuildHierarchy_NoDateTaken_FallsBackToLastModified()
    {
        var record = MakeRecord(
            @"C:\Downloads\meme.png",
            dateTaken: null,
            lastModified: new DateTime(2021, 7, 4));

        var plan = OrganizationPlanner.BuildHierarchy([record]);

        var year = Assert.Single(plan.Years);
        Assert.Equal(2021, year.Year);
        var month = Assert.Single(year.Months);
        Assert.Equal(7, month.Month);
    }

    [Fact]
    public void BuildHierarchy_DateTakenPresent_TakesPrecedenceOverLastModified()
    {
        var record = MakeRecord(
            @"C:\Photos\a.jpg",
            dateTaken: new DateTime(2020, 1, 1),
            lastModified: new DateTime(2024, 12, 31));

        var plan = OrganizationPlanner.BuildHierarchy([record]);

        var year = Assert.Single(plan.Years);
        Assert.Equal(2020, year.Year);
    }

    [Fact]
    public void BuildHierarchy_SplitsPhotoAndNoMetadataWithinSameMonth()
    {
        var records = new[]
        {
            MakeRecord(@"C:\Photos\real.jpg", dateTaken: new DateTime(2024, 5, 1), hasExif: true),
            MakeRecord(@"C:\Photos\screenshot.png", dateTaken: null, lastModified: new DateTime(2024, 5, 2), hasExif: false),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        var month = plan.Years.Single().Months.Single();
        Assert.Equal(2, month.Categories.Count);

        var photo = month.Categories.Single(c => c.Category == MetadataCategory.Photo);
        Assert.Equal(1, photo.FileCount);
        Assert.Equal("Photo", photo.Label);
        Assert.Equal(@"C:\Photos\real.jpg", photo.Files[0].SourcePath);

        var noMetadata = month.Categories.Single(c => c.Category == MetadataCategory.NoMetadata);
        Assert.Equal(1, noMetadata.FileCount);
        Assert.Equal("NoMetadata", noMetadata.Label);
        Assert.Equal(@"C:\Photos\screenshot.png", noMetadata.Files[0].SourcePath);
    }

    [Fact]
    public void BuildHierarchy_TargetFolderReflectsYearMonthCategory()
    {
        var record = MakeRecord(@"C:\Photos\a.jpg", dateTaken: new DateTime(2024, 3, 1), hasExif: true);

        var plan = OrganizationPlanner.BuildHierarchy([record]);

        var file = plan.Years.Single().Months.Single().Categories.Single().Files.Single();
        Assert.Equal("2024/03/Photo", file.TargetFolder);
        Assert.Equal("2024/03/Photo/a.jpg", file.TargetPath);
    }

    [Fact]
    public void BuildHierarchy_NameConflict_DifferentParentFolder_AppendsFolderName()
    {
        var records = new[]
        {
            MakeRecord(@"C:\Photos\FolderA\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 1)),
            MakeRecord(@"C:\Photos\FolderB\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 2)),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        var files = plan.Years.Single().Months.Single().Categories.Single().Files;
        Assert.Equal(2, files.Count);

        // First claim keeps the plain name.
        Assert.Equal("IMG_0001.jpg", files[0].TargetFileName);
        // Second gets its parent folder name appended to disambiguate.
        Assert.Equal("IMG_0001 (from FolderB).jpg", files[1].TargetFileName);
    }

    [Fact]
    public void BuildHierarchy_NameConflict_SameNameAndSameParentFolderName_FallsBackToNumbering()
    {
        var records = new[]
        {
            MakeRecord(@"C:\Photos\Test\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 1)),
            MakeRecord(@"D:\Backup\Test\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 2)),
            MakeRecord(@"E:\Other\Test\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 3)),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        var files = plan.Years.Single().Months.Single().Categories.Single().Files;
        Assert.Equal(3, files.Count);

        Assert.Equal("IMG_0001.jpg", files[0].TargetFileName);
        Assert.Equal("IMG_0001 (from Test).jpg", files[1].TargetFileName);
        Assert.Equal("IMG_0001 (from Test) (2).jpg", files[2].TargetFileName);
    }

    [Fact]
    public void BuildHierarchy_NameConflict_IsScopedPerDestinationFolder()
    {
        // Same filename, but different months → different destination
        // folders → no conflict, both keep the plain name.
        var records = new[]
        {
            MakeRecord(@"C:\Photos\FolderA\IMG_0001.jpg", dateTaken: new DateTime(2024, 3, 1)),
            MakeRecord(@"C:\Photos\FolderB\IMG_0001.jpg", dateTaken: new DateTime(2024, 4, 1)),
        };

        var plan = OrganizationPlanner.BuildHierarchy(records);

        foreach (var month in plan.Years.Single().Months)
        {
            var file = month.Categories.Single().Files.Single();
            Assert.Equal("IMG_0001.jpg", file.TargetFileName);
        }
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
