using ImageCleanup.Data.Models;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Repositories;

public sealed class OrganizationStagingRepository
{
    private readonly string _connectionString;

    public OrganizationStagingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void StageAction(int fileRecordId, string action, string? targetPath, string? reason)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO OrganizationStaging (FileRecordId, Action, TargetPath, Reason, Committed)
            VALUES ($fileRecordId, $action, $targetPath, $reason, 0)
            """;
        cmd.Parameters.AddWithValue("$fileRecordId", fileRecordId);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$targetPath", targetPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", reason ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<StagingEntry> GetPendingActions()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileRecordId, Action, TargetPath, Reason, Committed
            FROM OrganizationStaging
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
            UPDATE OrganizationStaging
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
        cmd.CommandText = "DELETE FROM OrganizationStaging WHERE Committed = 0";
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
