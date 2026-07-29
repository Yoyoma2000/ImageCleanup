using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data;

/// <summary>
/// Creates the SQLite schema on first startup. Safe to call every startup —
/// all DDL statements use IF NOT EXISTS guards.
/// </summary>
public static class DbInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS FileRecords (
                Id            INTEGER PRIMARY KEY,
                FilePath      TEXT    UNIQUE NOT NULL,
                FileHash      TEXT    NOT NULL,
                PerceptualHash INTEGER,
                FileSize      INTEGER NOT NULL,
                LastModified  TEXT    NOT NULL,
                Width         INTEGER,
                Height        INTEGER,
                BlurScore     REAL,
                DateTaken     TEXT,
                CameraModel   TEXT,
                IsScreenshot  INTEGER
            );

            CREATE TABLE IF NOT EXISTS OrganizationStaging (
                Id            INTEGER PRIMARY KEY,
                FileRecordId  INTEGER NOT NULL REFERENCES FileRecords(Id),
                Action        TEXT    NOT NULL,
                TargetPath    TEXT,
                Reason        TEXT,
                Committed     INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
