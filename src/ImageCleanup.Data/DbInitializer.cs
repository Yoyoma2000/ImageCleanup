using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data;

/// <summary>
/// Creates and upgrades the SQLite schema. Safe to call on every startup —
/// all DDL uses IF NOT EXISTS, and ADD COLUMN migrations check column
/// existence via PRAGMA table_info first, so no exception is thrown (and
/// caught) on the normal case where the column already exists.
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
                Id             INTEGER PRIMARY KEY,
                FilePath       TEXT    UNIQUE NOT NULL,
                FileHash       TEXT    NOT NULL,
                PerceptualHash INTEGER,
                FileSize       INTEGER NOT NULL,
                LastModified   TEXT    NOT NULL,
                Width          INTEGER,
                Height         INTEGER,
                BlurScore      REAL,
                DateTaken      TEXT,
                CameraModel    TEXT,
                IsScreenshot   INTEGER,
                LowDetail      INTEGER,
                SchemaVersion  INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS OrganizationStaging (
                Id            INTEGER PRIMARY KEY,
                FileRecordId  INTEGER NOT NULL REFERENCES FileRecords(Id),
                Action        TEXT    NOT NULL,
                TargetPath    TEXT,
                Reason        TEXT,
                Committed     INTEGER NOT NULL DEFAULT 0
            );

            -- Deliberately a separate table from OrganizationStaging rather than
            -- a shared-table-with-discriminator: Quality's "worth reviewing" flags
            -- and Duplicates' near-certain dup staging represent different
            -- confidence levels and must not share a review/commit flow (e.g. one
            -- feature's ClearStaged/commit must never touch the other's rows).
            CREATE TABLE IF NOT EXISTS QualityStaging (
                Id            INTEGER PRIMARY KEY,
                FileRecordId  INTEGER NOT NULL REFERENCES FileRecords(Id),
                Action        TEXT    NOT NULL,
                TargetPath    TEXT,
                Reason        TEXT,
                Committed     INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();

        // ── Column-level upgrades for pre-existing databases ─────────────────
        // Each call is a no-op if the column already exists (checked via
        // PRAGMA table_info rather than attempt-and-catch).
        AddColumnIfMissing(connection, "FileRecords", "LowDetail", "INTEGER");
        AddColumnIfMissing(connection, "FileRecords", "SchemaVersion", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string tableName, string columnName, string columnTypeSql)
    {
        if (ColumnExists(connection, tableName, columnName)) return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnTypeSql}";
        cmd.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var cmd = connection.CreateCommand();
        // Table names can't be bound as PRAGMA parameters; tableName is always
        // one of our own hard-coded constants above, never external input.
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
            var existingColumnName = reader.GetString(1);
            if (string.Equals(existingColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
