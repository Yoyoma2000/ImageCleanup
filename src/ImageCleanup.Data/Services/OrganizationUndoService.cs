using System.Text.Json;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Reverses a previously-executed Organization move batch by reading the
/// move log OrganizationExecutor wrote before making any move. This is the
/// automated undo referenced (but not implemented) in OrganizationExecutor's
/// doc comment — the move log is now more than a manual-recovery record.
/// <para>
/// Stateless by design (every method takes the path/directory it needs
/// explicitly) since there's nothing to inject or configure — matches
/// OrganizationPlanner's style rather than OrganizationExecutor's
/// constructor-configured logDirectory, which exists only because
/// OrganizationExecutor decides where NEW logs get written; undo always
/// operates against a log the caller already has a specific path for.
/// </para>
/// </summary>
public static class OrganizationUndoService
{
    /// <summary>
    /// Reverses every entry in the move log at <paramref name="moveLogPath"/>,
    /// moving each file from its logged destination back to its logged
    /// source. Never overwrites: each entry is validated independently
    /// before being touched, so one entry's problem (missing destination,
    /// something already occupying the original source) doesn't block or
    /// corrupt any other entry, and doesn't assume a clean, first-time
    /// reversal — re-running Undo against a log that was already fully or
    /// partially reversed is safe (already-restored entries are recognized
    /// and skipped, not re-moved or errored).
    /// </summary>
    public static OrganizationUndoResult Undo(string moveLogPath)
    {
        var log = ReadMoveLog(moveLogPath);
        var result = new OrganizationUndoResult();

        foreach (var entry in log.Moves)
        {
            var destExists = File.Exists(entry.DestinationPath);
            var sourceExists = File.Exists(entry.SourcePath);

            if (!destExists && sourceExists)
            {
                // Nothing left at the destination, and the file is already
                // back where it started — this entry was already reversed
                // (a prior undo run, full or partial). Not an error. Still
                // worth a cleanup pass: an interrupted earlier undo may have
                // moved the file back without ever getting to clean up the
                // now-empty Year/Month/Category folder it left behind.
                CleanupEmptyDestinationFolders(entry.DestinationPath, log.DestinationRoot);
                result.Entries.Add(new OrganizationUndoEntryResult(
                    entry.SourcePath, entry.DestinationPath, OrganizationUndoOutcome.AlreadyReversed));
                continue;
            }

            if (!destExists && !sourceExists)
            {
                // Neither location has the file — it moved, was renamed, or
                // was deleted by something else since the original
                // execution. Nothing safe to do; report rather than guess.
                result.Entries.Add(new OrganizationUndoEntryResult(
                    entry.SourcePath, entry.DestinationPath, OrganizationUndoOutcome.SkippedDestMissing,
                    "Destination file no longer exists, and nothing is at the original source either."));
                continue;
            }

            if (destExists && sourceExists)
            {
                // Something is already sitting at the original source path
                // (a new file created there since, or a previous undo's
                // partial failure left both copies). Moving back would
                // silently overwrite it — never do that.
                result.Entries.Add(new OrganizationUndoEntryResult(
                    entry.SourcePath, entry.DestinationPath, OrganizationUndoOutcome.SkippedSourceOccupied,
                    "A different file already exists at the original source location."));
                continue;
            }

            // destExists && !sourceExists — the normal, reversible case.
            try
            {
                var sourceDir = Path.GetDirectoryName(entry.SourcePath);
                if (!string.IsNullOrEmpty(sourceDir))
                    Directory.CreateDirectory(sourceDir);

                File.Move(entry.DestinationPath, entry.SourcePath, overwrite: false);
                CleanupEmptyDestinationFolders(entry.DestinationPath, log.DestinationRoot);
                result.Entries.Add(new OrganizationUndoEntryResult(
                    entry.SourcePath, entry.DestinationPath, OrganizationUndoOutcome.Reversed));
            }
            catch (Exception ex)
            {
                result.Entries.Add(new OrganizationUndoEntryResult(
                    entry.SourcePath, entry.DestinationPath, OrganizationUndoOutcome.Failed, ex.Message));
            }
        }

        return result;
    }

    /// <summary>
    /// After a file is moved back out of <paramref name="destinationPath"/>,
    /// walks up its containing folder chain deleting each one that's now
    /// empty — the Year/Month/Category structure OrganizationExecutor
    /// created for it — stopping as soon as a folder is non-empty (a
    /// partial undo leaving other files behind must not have its folders
    /// removed out from under them) or once <paramref name="destinationRoot"/>
    /// itself would be next (the user-chosen root is never deleted, empty
    /// or not — only folders this app's own Year/Month/Category planning
    /// created below it). Best-effort: a folder that can't be deleted
    /// (permissions, transient lock) is left in place rather than failing
    /// the undo over a cosmetic cleanup step.
    /// </summary>
    private static void CleanupEmptyDestinationFolders(string destinationPath, string destinationRoot)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));

        while (!string.IsNullOrEmpty(dir))
        {
            var normalizedDir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedDir, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                break;

            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                break;

            try
            {
                Directory.Delete(dir);
            }
            catch
            {
                break;
            }

            dir = Path.GetDirectoryName(dir);
        }
    }

    /// <summary>Deserializes a move-log JSON file. Throws if the file is missing or not valid JSON — callers listing logs should catch/skip; callers undoing a specific, user-chosen log should let it surface.</summary>
    public static MoveLog ReadMoveLog(string moveLogPath) =>
        JsonSerializer.Deserialize<MoveLog>(File.ReadAllText(moveLogPath)) ?? new MoveLog();

    /// <summary>
    /// Lists move-log files in <paramref name="logDirectory"/> (default:
    /// OrganizationExecutor's own default directory) newest-first, for a UI
    /// to let the user browse/pick one to undo. A corrupt or unreadable log
    /// file is skipped rather than aborting the whole listing.
    /// </summary>
    public static IReadOnlyList<MoveLogSummary> ListMoveLogs(string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "move-logs");

        if (!Directory.Exists(directory))
            return [];

        var summaries = new List<MoveLogSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "move-log_*.json"))
        {
            try
            {
                var log = ReadMoveLog(path);
                summaries.Add(new MoveLogSummary(path, log.Timestamp, log.DestinationRoot, log.Moves.Count));
            }
            catch
            {
                // Corrupt/unreadable/truncated log file — skip it rather
                // than failing the whole listing over one bad file.
            }
        }

        return summaries.OrderByDescending(s => s.Timestamp).ToList();
    }
}

public enum OrganizationUndoOutcome
{
    /// <summary>Successfully moved back from destination to source.</summary>
    Reversed,

    /// <summary>Destination was already gone and the file is already at the source — a prior undo run already handled this entry.</summary>
    AlreadyReversed,

    /// <summary>Destination file no longer exists, and the source is empty too — nothing safe to do.</summary>
    SkippedDestMissing,

    /// <summary>The original source location already has a (different) file — refused to overwrite it.</summary>
    SkippedSourceOccupied,

    /// <summary>Both locations were in the expected state but the move itself failed (permissions, in-use file, etc).</summary>
    Failed,
}

public sealed record OrganizationUndoEntryResult(
    string SourcePath,
    string DestinationPath,
    OrganizationUndoOutcome Outcome,
    string? Reason = null);

public sealed class OrganizationUndoResult
{
    public List<OrganizationUndoEntryResult> Entries { get; } = [];

    public int Reversed => Entries.Count(e => e.Outcome == OrganizationUndoOutcome.Reversed);
    public int AlreadyReversed => Entries.Count(e => e.Outcome == OrganizationUndoOutcome.AlreadyReversed);
    public int Skipped => Entries.Count(e =>
        e.Outcome is OrganizationUndoOutcome.SkippedDestMissing or OrganizationUndoOutcome.SkippedSourceOccupied);
    public int Failed => Entries.Count(e => e.Outcome == OrganizationUndoOutcome.Failed);

    public string Summary =>
        $"{Reversed} reversed, {AlreadyReversed} already reversed, {Skipped} skipped, {Failed} failed.";
}

public sealed record MoveLogSummary(string Path, DateTime Timestamp, string DestinationRoot, int FileCount);
