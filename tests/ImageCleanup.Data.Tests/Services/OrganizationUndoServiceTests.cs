using System.Threading;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Organization;
using ImageCleanup.Data.Services;

namespace ImageCleanup.Data.Tests.Services;

public sealed class OrganizationUndoServiceTests : IDisposable
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
    public void Undo_FullySuccessfulExecution_MovesEveryFileBackToItsOriginalLocation()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var pathB = WriteFile(_sourceDir, "b.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);
        Assert.Equal(2, execResult.Succeeded);
        Assert.False(File.Exists(pathA));
        Assert.False(File.Exists(pathB));

        var undoResult = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(2, undoResult.Reversed);
        Assert.Equal(0, undoResult.AlreadyReversed);
        Assert.Equal(0, undoResult.Skipped);
        Assert.Equal(0, undoResult.Failed);
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
    }

    [Fact]
    public void Undo_DestinationFileMissing_SkipsThatEntry_WithoutCrashing()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);
        Assert.Equal(1, execResult.Succeeded);

        // Simulate the moved file having been deleted (or moved elsewhere)
        // after execution, before anyone tried to undo it.
        var expectedDestPath = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "a.jpg");
        Assert.True(File.Exists(expectedDestPath));
        File.Delete(expectedDestPath);

        var undoResult = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(0, undoResult.Reversed);
        Assert.Equal(1, undoResult.Skipped);
        Assert.Equal(0, undoResult.Failed);
        var entry = Assert.Single(undoResult.Entries);
        Assert.Equal(OrganizationUndoOutcome.SkippedDestMissing, entry.Outcome);
        Assert.NotNull(entry.Reason);
        Assert.False(File.Exists(pathA)); // never recreated — no data to move back
    }

    [Fact]
    public void Undo_SourceLocationOccupiedByDifferentFile_SkipsWithoutOverwriting()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);
        Assert.Equal(1, execResult.Succeeded);

        // A new, unrelated file now sits at the original source path.
        File.WriteAllBytes(pathA, [9, 9, 9]);

        var undoResult = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(0, undoResult.Reversed);
        Assert.Equal(1, undoResult.Skipped);
        var entry = Assert.Single(undoResult.Entries);
        Assert.Equal(OrganizationUndoOutcome.SkippedSourceOccupied, entry.Outcome);
        Assert.NotNull(entry.Reason);

        // The "occupying" file at the source must be untouched, and the
        // moved file must still be sitting at the destination (not lost).
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(pathA));
        var expectedDestPath = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "a.jpg");
        Assert.True(File.Exists(expectedDestPath));
    }

    [Fact]
    public void Undo_RerunAfterFullReversal_IsIdempotent_ReportsAlreadyReversed_NoErrors()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);

        var firstUndo = OrganizationUndoService.Undo(execResult.MoveLogPath);
        Assert.Equal(1, firstUndo.Reversed);

        var secondUndo = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(0, secondUndo.Reversed);
        Assert.Equal(1, secondUndo.AlreadyReversed);
        Assert.Equal(0, secondUndo.Failed);
        Assert.True(File.Exists(pathA)); // still there, not moved again or deleted
    }

    [Fact]
    public void Undo_RerunAfterPartialReversal_OnlyReversesRemainingEntries()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var pathB = WriteFile(_sourceDir, "b.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);

        // Manually reverse just one of the two entries, out-of-band, to
        // simulate "a previous undo run was interrupted partway through".
        var expectedDestPathA = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo", "a.jpg");
        File.Move(expectedDestPathA, pathA);

        var undoResult = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(1, undoResult.Reversed);          // b.jpg
        Assert.Equal(1, undoResult.AlreadyReversed);   // a.jpg
        Assert.Equal(0, undoResult.Failed);
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
    }

    // ── Empty-folder cleanup ─────────────────────────────────────────────

    [Fact]
    public void Undo_FullReversal_RemovesEmptyYearMonthCategoryFolders_ButNotDestinationRoot()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([MakeRecord(pathA, new DateTime(2024, 3, 1))]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);

        var categoryDir = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo");
        var monthDir    = Path.Combine(_destDir, "2024", MonthFolder(3));
        var yearDir     = Path.Combine(_destDir, "2024");
        Assert.True(Directory.Exists(categoryDir)); // sanity check before undo

        OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.False(Directory.Exists(categoryDir));
        Assert.False(Directory.Exists(monthDir));
        Assert.False(Directory.Exists(yearDir));
        Assert.True(Directory.Exists(_destDir)); // the chosen root itself is never deleted
        Assert.True(File.Exists(pathA));
    }

    [Fact]
    public void Undo_PartialReversal_RetainsFolderStillContainingAnUnreversedFile()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var pathB = WriteFile(_sourceDir, "b.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathA, new DateTime(2024, 3, 1)),
            MakeRecord(pathB, new DateTime(2024, 3, 2)),
        ]);

        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);

        // Occupy b.jpg's original source location with a different file so
        // its entry gets skipped (SkippedSourceOccupied) rather than
        // reversed — its destination copy must remain, which means the
        // shared Category folder must NOT be cleaned up.
        File.WriteAllBytes(pathB, [9, 9, 9]);

        var categoryDir = Path.Combine(_destDir, "2024", MonthFolder(3), "Photo");
        var bDestPath   = Path.Combine(categoryDir, "b.jpg");

        var undoResult = OrganizationUndoService.Undo(execResult.MoveLogPath);

        Assert.Equal(1, undoResult.Reversed);           // a.jpg
        Assert.Equal(1, undoResult.Skipped);             // b.jpg
        Assert.True(File.Exists(pathA));                 // a.jpg back at source
        Assert.True(File.Exists(bDestPath));             // b.jpg still at destination — not lost
        Assert.True(Directory.Exists(categoryDir));      // folder retained — not empty
    }

    [Fact]
    public void ListMoveLogs_ReturnsWrittenLogs_NewestFirst_WithCorrectFileCounts()
    {
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan1 = OrganizationPlanner.BuildHierarchy([MakeRecord(pathA, new DateTime(2024, 3, 1))]);
        var executor = new OrganizationExecutor(_logDir);
        var result1 = executor.Execute(plan1, _destDir);

        // Ensure a distinguishable timestamp in the second log's filename
        // (move-log_yyyyMMdd_HHmmss.json has 1-second resolution).
        Thread.Sleep(1100);

        var pathB = WriteFile(_sourceDir, "b.jpg");
        var pathC = WriteFile(_sourceDir, "c.jpg");
        var plan2 = OrganizationPlanner.BuildHierarchy([
            MakeRecord(pathB, new DateTime(2024, 4, 1)),
            MakeRecord(pathC, new DateTime(2024, 4, 2)),
        ]);
        var result2 = executor.Execute(plan2, _destDir);

        var logs = OrganizationUndoService.ListMoveLogs(_logDir);

        Assert.Equal(2, logs.Count);
        Assert.Equal(result2.MoveLogPath, logs[0].Path); // newest first
        Assert.Equal(2, logs[0].FileCount);
        Assert.Equal(result1.MoveLogPath, logs[1].Path);
        Assert.Equal(1, logs[1].FileCount);
    }

    [Fact]
    public void ListMoveLogs_TimestampRoundTripsWithUtcKind_SoDisplayLayerCanSafelyConvertToLocalTime()
    {
        // Regression guard for the "undo picker showed the wrong time"
        // bug: the fix is DateTime.ToLocalTime() at display time in
        // OrganizationPage (not testable here — WinUI), but that fix is
        // only correct if the round-tripped Timestamp's Kind is Utc.
        // DateTime.ToLocalTime() is a no-op on Kind.Unspecified, so if
        // System.Text.Json's serialize/deserialize round-trip ever stopped
        // preserving Utc Kind, the display-layer fix would silently stop
        // working — this test would catch that regression here instead.
        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([MakeRecord(pathA, new DateTime(2024, 3, 1))]);
        var executor = new OrganizationExecutor(_logDir);
        var execResult = executor.Execute(plan, _destDir);

        var logs = OrganizationUndoService.ListMoveLogs(_logDir);

        var summary = Assert.Single(logs);
        Assert.Equal(DateTimeKind.Utc, summary.Timestamp.Kind);
        Assert.Equal(execResult.MoveLogPath, summary.Path);
    }

    [Fact]
    public void ListMoveLogs_EmptyDirectory_ReturnsEmptyList()
    {
        var logs = OrganizationUndoService.ListMoveLogs(_logDir);
        Assert.Empty(logs);
    }

    [Fact]
    public void ListMoveLogs_SkipsCorruptLogFile_WithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_logDir, "move-log_20990101_000000.json"), "{ not valid json");

        var pathA = WriteFile(_sourceDir, "a.jpg");
        var plan = OrganizationPlanner.BuildHierarchy([MakeRecord(pathA, new DateTime(2024, 3, 1))]);
        var executor = new OrganizationExecutor(_logDir);
        executor.Execute(plan, _destDir);

        var logs = OrganizationUndoService.ListMoveLogs(_logDir);

        Assert.Single(logs);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string MonthFolder(int month) =>
        $"{month:D2} - {new DateTime(1, month, 1).ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture)}";

    private static string WriteFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static ImageRecord MakeRecord(string filePath, DateTime? dateTaken, bool hasExif = true) => new()
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
        var path = Path.Combine(Path.GetTempPath(), $"OrgUndoTests_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
