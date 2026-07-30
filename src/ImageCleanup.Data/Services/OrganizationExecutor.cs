using System.Text.Json;
using ImageCleanup.Core.Organization;

namespace ImageCleanup.Data.Services;

/// <summary>
/// Executes an OrganizationPlan: moves each planned file to
/// destinationRoot/Year/Month/Category/&lt;already-conflict-resolved
/// filename&gt;. Before moving anything, writes a durable JSON move log
/// listing every planned source→destination pair, so a record exists even
/// if the app crashes mid-operation — that log is the safety net for now;
/// there is no automated undo (see CLAUDE.md).
/// </summary>
public sealed class OrganizationExecutor
{
    private readonly string _logDirectory;

    public OrganizationExecutor(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCleanup", "move-logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public OrganizationExecutionResult Execute(OrganizationPlan plan, string destinationRoot)
    {
        var moves = ComputePlannedMoves(plan, destinationRoot);

        // Written before any move is attempted, deliberately — this is the
        // durable record a human could use to manually reverse the batch if
        // the app crashes partway through.
        var logPath = WriteMoveLog(destinationRoot, moves);

        var result = new OrganizationExecutionResult { MoveLogPath = logPath };

        foreach (var move in moves)
        {
            try
            {
                var destDir = Path.GetDirectoryName(move.DestinationPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Move(move.SourcePath, move.DestinationPath, overwrite: false);
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.AddFailure(move.SourcePath, move.DestinationPath, ex.Message);
            }
        }

        return result;
    }

    private static List<PlannedMove> ComputePlannedMoves(OrganizationPlan plan, string destinationRoot)
    {
        var moves = new List<PlannedMove>();

        foreach (var year in plan.Years)
        foreach (var month in year.Months)
        foreach (var category in month.Categories)
        foreach (var file in category.Files)
        {
            var folderSegments = file.TargetFolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var destinationPath = Path.Combine(
                [destinationRoot, .. folderSegments, file.TargetFileName]);

            moves.Add(new PlannedMove(file.SourcePath, destinationPath));
        }

        return moves;
    }

    private string WriteMoveLog(string destinationRoot, IReadOnlyList<PlannedMove> moves)
    {
        var log = new MoveLog
        {
            Timestamp       = DateTime.UtcNow,
            DestinationRoot = destinationRoot,
            Moves           = moves.Select(m => new MoveLogEntry(m.SourcePath, m.DestinationPath)).ToList(),
        };

        var fileName = $"move-log_{log.Timestamp:yyyyMMdd_HHmmss}.json";
        var path     = Path.Combine(_logDirectory, fileName);

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        return path;
    }

    private sealed record PlannedMove(string SourcePath, string DestinationPath);
}

/// <summary>Durable, human-readable record of a planned move batch — written before any file is touched.</summary>
public sealed class MoveLog
{
    public DateTime Timestamp { get; init; }
    public string DestinationRoot { get; init; } = string.Empty;
    public List<MoveLogEntry> Moves { get; init; } = [];
}

public sealed record MoveLogEntry(string SourcePath, string DestinationPath);
