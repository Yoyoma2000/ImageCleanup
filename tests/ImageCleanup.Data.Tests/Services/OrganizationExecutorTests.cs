using System.Text.Json;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Organization;
using ImageCleanup.Data.Services;

namespace ImageCleanup.Data.Tests.Services;

public sealed class OrganizationExecutorTests : IDisposable
{
    private readonly string _sourceDir = CreateTempDir("source");
    private readonly string _destDir   = CreateTempDir("dest");
    private readonly string _logDir    = CreateTempDir("logs");

    public void Dispose()
    {
        TryDelete(_sourceDir);
        TryDelete(_destDir);
        TryDelete(_logDir);
    }

    [Fact]
    public void Execute_MovesFilesToComputedTargetPaths_AndReportsSuccess()
    {
        var path = WriteFile("IMG_0001.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(path, new DateTime(2024, 3, 1), hasExif: true),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);

        var expectedPath = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "IMG_0001.jpg");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Execute_CreatesDestinationDirectoriesAsNeeded()
    {
        var path = WriteFile("a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(path, new DateTime(2022, 11, 5), hasExif: false),
        ]);

        Assert.False(Directory.Exists(Path.Combine(_destDir, "2022")));

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(1, result.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(_destDir, "2022", MonthFolder(11), "NoMetadata")));
    }

    [Fact]
    public void Execute_OneMissingSourceFile_DoesNotAbortBatch_ReportsBothOutcomes()
    {
        var goodPath = WriteFile("good.jpg");
        var missingPath = Path.Combine(_sourceDir, "ghost.jpg"); // never created on disk

        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(goodPath, new DateTime(2024, 1, 1)),
            MakeRecord(missingPath, new DateTime(2024, 1, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(missingPath, result.Failures[0].SourcePath);
        Assert.NotEmpty(result.Failures[0].Message);

        // The successful file still made it despite the other failing.
        Assert.True(File.Exists(Path.Combine(_destDir, "2024", MonthFolder(1), "Photo", "good.jpg")));
    }

    [Fact]
    public void Execute_WritesMoveLog_BeforeAnyMoveAttempted_ContainingAllPlannedEntries_EvenIfEveryMoveFails()
    {
        // Every source is missing, so every move fails — if the log still
        // contains all the planned entries, that proves it was written from
        // the plan up front rather than being built up only as moves succeed.
        var missingA = Path.Combine(_sourceDir, "missing-a.jpg");
        var missingB = Path.Combine(_sourceDir, "missing-b.jpg");

        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(missingA, new DateTime(2023, 6, 1)),
            MakeRecord(missingB, new DateTime(2023, 6, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, result.Failed);

        Assert.True(File.Exists(result.MoveLogPath));
        var log = JsonSerializer.Deserialize<MoveLog>(File.ReadAllText(result.MoveLogPath));

        Assert.NotNull(log);
        Assert.Equal(_destDir, log!.DestinationRoot);
        Assert.Equal(2, log.Moves.Count);
        Assert.Contains(log.Moves, m => m.SourcePath == missingA);
        Assert.Contains(log.Moves, m => m.SourcePath == missingB);
    }

    [Fact]
    public void Execute_MoveLog_RecordsCorrectDestinationPaths()
    {
        var path = WriteFile("IMG_0002.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(path, new DateTime(2024, 3, 1), hasExif: true),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        var log = JsonSerializer.Deserialize<MoveLog>(File.ReadAllText(result.MoveLogPath));
        var entry = Assert.Single(log!.Moves);

        Assert.Equal(path, entry.SourcePath);
        Assert.Equal(Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "IMG_0002.jpg"), entry.DestinationPath);
    }

    [Fact]
    public void Execute_EmptyPlan_SucceedsWithNoMovesAndStillWritesLog()
    {
        var plan = OrganizationPlanner.BuildHierarchy([]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(result.MoveLogPath));
    }

    [Fact]
    public void Execute_MonthFolderName_IsHybridZeroPaddedNumberAndFullMonthName()
    {
        var path = WriteFile("IMG_0003.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(path, new DateTime(2024, 1, 15), hasExif: true),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir);

        Assert.Equal(1, result.Succeeded);

        var monthDir = Path.Combine(_destDir, "2024", MonthFolder(1));
        Assert.True(Directory.Exists(monthDir));
        // MonthFolder(1) mirrors production's exact format ("01 - <month name>",
        // e.g. "01 - January" on an English-locale machine) rather than
        // hardcoding the English name, so this stays correct everywhere.
        Assert.Matches(@"^01 - .+$", Path.GetFileName(monthDir));
    }

    // ── selectedSourcePaths (selective execution) ───────────────────────

    [Fact]
    public void Execute_WithSelectedSourcePaths_OnlyMovesSelectedFiles()
    {
        var pathA = WriteFile("selected.jpg");
        var pathB = WriteFile("unselected.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir, new HashSet<string> { pathA });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(File.Exists(pathA));      // moved
        Assert.True(File.Exists(pathB));       // left in place, not even attempted
        Assert.True(File.Exists(Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "selected.jpg")));
    }

    [Fact]
    public void Execute_WithSelectedSourcePaths_MoveLogOnlyContainsSelectedFiles()
    {
        var pathA = WriteFile("selected.jpg");
        var pathB = WriteFile("unselected.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir, new HashSet<string> { pathA });

        var log = JsonSerializer.Deserialize<MoveLog>(File.ReadAllText(result.MoveLogPath));
        var entry = Assert.Single(log!.Moves);
        Assert.Equal(pathA, entry.SourcePath);
    }

    [Fact]
    public void Execute_WithEmptySelectedSourcePaths_MovesNothing_ButStillWritesLog()
    {
        var path = WriteFile("a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(path, new DateTime(2024, 3, 1)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir, new HashSet<string>());

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(path)); // untouched
        Assert.True(File.Exists(result.MoveLogPath));

        var log = JsonSerializer.Deserialize<MoveLog>(File.ReadAllText(result.MoveLogPath));
        Assert.Empty(log!.Moves);
    }

    [Fact]
    public void Execute_WithNullSelectedSourcePaths_MovesEveryFile_SameAsOriginalAllOrNothingBehavior()
    {
        var pathA = WriteFile("a.jpg");
        var pathB = WriteFile("b.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var result = executor.Execute(plan, _destDir, selectedSourcePaths: null);

        Assert.Equal(2, result.Succeeded);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>Mirrors OrganizationPlanner's real folder-naming format ("01 - January") without hardcoding an English name, so this test stays correct on non-English machines.</summary>
    private static string MonthFolder(int month) =>
        $"{month:D2} - {new DateTime(1, month, 1).ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture)}";

    private string WriteFile(string name)
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static ImageRecord MakeRecord(
        string filePath,
        DateTime? dateTaken,
        bool hasExif = true) => new()
    {
        FilePath     = filePath,
        FileHash     = Guid.NewGuid().ToString("N"),
        FileSize     = 3,
        LastModified = new DateTime(2000, 1, 1),
        DateTaken    = dateTaken,
        HasExif      = hasExif,
    };

    private static string CreateTempDir(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"OrgExecTests_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
