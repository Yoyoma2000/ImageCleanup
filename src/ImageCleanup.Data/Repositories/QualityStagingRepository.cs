using ImageCleanup.Data.Models;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Repositories;

/// <summary>
/// Staging for the Quality feature's flagged files — a separate table (and
/// class) from OrganizationStagingRepository/OrganizationStaging on purpose.
/// Quality's "worth reviewing" flags and Duplicates' near-certain duplicate
/// staging are different confidence levels and must not share a
/// review/commit flow; keeping them in separate tables means neither
/// feature's StageAction/GetPendingActions/ClearStaged/commit can ever
/// accidentally touch the other's rows.
/// </summary>
public sealed class QualityStagingRepository : IStagingRepository
{
    private readonly string _connectionString;

    public QualityStagingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Inserts a new staging row and returns its Id.</summary>
    public int StageAction(int fileRecordId, string action, string? targetPath, string? reason)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO QualityStaging (FileRecordId, Action, TargetPath, Reason, Committed)
            VALUES ($fileRecordId, $action, $targetPath, $reason, 0)
            RETURNING Id
            """;
        cmd.Parameters.AddWithValue("$fileRecordId", fileRecordId);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$targetPath", targetPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", reason ?? (object)DBNull.Value);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>Deletes a single uncommitted staging row by Id.</summary>
    public void RemoveStagingEntry(int stagingId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM QualityStaging WHERE Id = $id AND Committed = 0";
        cmd.Parameters.AddWithValue("$id", stagingId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Updates the TargetPath of an existing staging row (used to sync Move paths before commit).</summary>
    public void UpdateTargetPath(int stagingId, string? targetPath)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE QualityStaging SET TargetPath = $tp WHERE Id = $id";
        cmd.Parameters.AddWithValue("$tp", targetPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$id", stagingId);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<StagingEntry> GetPendingActions()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileRecordId, Action, TargetPath, Reason, Committed
            FROM QualityStaging
            WHERE Committed = 0
            """;

        using var reader = cmd.ExecuteReader();
        var results = new List<StagingEntry>();
        while (reader.Read())
            results.Add(MapRow(reader));
        return results;
    }

    public void CommitAction(int stagingId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE QualityStaging
            SET Committed = 1
            WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$id", stagingId);
        cmd.ExecuteNonQuery();
    }

    public void ClearStaged()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM QualityStaging WHERE Committed = 0";
        cmd.ExecuteNonQuery();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static StagingEntry MapRow(SqliteDataReader r) => new()
    {
        Id           = r.GetInt32(0),
        FileRecordId = r.GetInt32(1),
        Action       = r.GetString(2),
        TargetPath   = r.IsDBNull(3) ? null : r.GetString(3),
        Reason       = r.IsDBNull(4) ? null : r.GetString(4),
        Committed    = r.GetInt32(5) != 0,
    };
}
