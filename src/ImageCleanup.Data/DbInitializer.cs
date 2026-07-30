using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data;

/// <summary>
/// Creates and upgrades the SQLite schema. Safe to call on every startup —
/// all DDL uses IF NOT EXISTS, and ADD COLUMN migrations are idempotent
/// (duplicate-column exceptions are silently swallowed).
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
        // Each ALTER TABLE ADD COLUMN is a no-op if the column already exists.
        AddColumnIfMissing(connection, "ALTER TABLE FileRecords ADD COLUMN LowDetail INTEGER");
        AddColumnIfMissing(connection, "ALTER TABLE FileRecords ADD COLUMN SchemaVersion INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string alterSql)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = alterSql;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* duplicate column name — already present */ }
    }
}
