using ImageCleanup.Data.Models;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Repositories;

public sealed class FileCacheRepository
{
    /// <summary>
    /// Increment this constant whenever a new computed field is added to
    /// <see cref="FileRecord"/> that requires re-analysis of the image file.
    /// <c>NeedsRescan</c> returns <c>true</c> for any cached row whose stored
    /// <c>SchemaVersion</c> is less than this value, forcing a fresh scan even
    /// when <c>FileSize</c> and <c>LastModified</c> are unchanged.
    ///
    /// Version history:
    ///   0 — initial schema (no SchemaVersion column)
    ///   1 — added LowDetail (pixel-variance flag)
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;

    public FileCacheRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public FileRecord? GetByPath(string path)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FilePath, FileHash, PerceptualHash, FileSize, LastModified,
                   Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot,
                   LowDetail, SchemaVersion
            FROM FileRecords
            WHERE FilePath = $path
            """;
        cmd.Parameters.AddWithValue("$path", path);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    public void Upsert(FileRecord record)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FileRecords
                (FilePath, FileHash, PerceptualHash, FileSize, LastModified,
                 Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot,
                 LowDetail, SchemaVersion)
            VALUES
                ($filePath, $fileHash, $perceptualHash, $fileSize, $lastModified,
                 $width, $height, $blurScore, $dateTaken, $cameraModel, $isScreenshot,
                 $lowDetail, $schemaVersion)
            ON CONFLICT(FilePath) DO UPDATE SET
                FileHash       = excluded.FileHash,
                PerceptualHash = excluded.PerceptualHash,
                FileSize       = excluded.FileSize,
                LastModified   = excluded.LastModified,
                Width          = excluded.Width,
                Height         = excluded.Height,
                BlurScore      = excluded.BlurScore,
                DateTaken      = excluded.DateTaken,
                CameraModel    = excluded.CameraModel,
                IsScreenshot   = excluded.IsScreenshot,
                LowDetail      = excluded.LowDetail,
                SchemaVersion  = excluded.SchemaVersion
            RETURNING Id
            """;

        cmd.Parameters.AddWithValue("$filePath", record.FilePath);
        cmd.Parameters.AddWithValue("$fileHash", record.FileHash);
        cmd.Parameters.AddWithValue("$perceptualHash", record.PerceptualHash.HasValue
            ? (object)(long)record.PerceptualHash.Value
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$fileSize", record.FileSize);
        cmd.Parameters.AddWithValue("$lastModified", record.LastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$width",
            record.Width.HasValue ? (object)record.Width.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$height",
            record.Height.HasValue ? (object)record.Height.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$blurScore",
            record.BlurScore.HasValue ? (object)record.BlurScore.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$dateTaken", record.DateTaken.HasValue
            ? (object)record.DateTaken.Value.ToString("O")
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$cameraModel",
            record.CameraModel ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isScreenshot", record.IsScreenshot.HasValue
            ? (object)(record.IsScreenshot.Value ? 1 : 0)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$lowDetail", record.LowDetail.HasValue
            ? (object)(record.LowDetail.Value ? 1 : 0)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);

        var id = cmd.ExecuteScalar();
        if (id is long lid)
            record.Id = (int)lid;
    }

    /// <summary>
    /// Returns <c>true</c> when the file must be re-hashed and re-analysed:
    /// <list type="bullet">
    ///   <item>No cached row exists for <paramref name="path"/>.</item>
    ///   <item>The cached <c>FileSize</c> or <c>LastModified</c> differs.</item>
    ///   <item>The cached <c>SchemaVersion</c> is older than
    ///         <see cref="CurrentSchemaVersion"/> (new computed fields were added).</item>
    /// </list>
    /// </summary>
    public bool NeedsRescan(string path, long fileSize, DateTime lastModified)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT FileSize, LastModified, SchemaVersion
            FROM FileRecords
            WHERE FilePath = $path
            """;
        cmd.Parameters.AddWithValue("$path", path);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return true;

        var cachedSize     = reader.GetInt64(0);
        var cachedModified = DateTime.Parse(
            reader.GetString(1), null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var cachedVersion  = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

        return cachedSize    != fileSize
            || cachedModified != lastModified
            || cachedVersion   < CurrentSchemaVersion;
    }

    public FileRecord? GetById(int id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FilePath, FileHash, PerceptualHash, FileSize, LastModified,
                   Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot,
                   LowDetail, SchemaVersion
            FROM FileRecords
            WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    public void DeleteByPath(string path)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM FileRecords WHERE FilePath = $path";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<FileRecord> GetAllRecords()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FilePath, FileHash, PerceptualHash, FileSize, LastModified,
                   Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot,
                   LowDetail, SchemaVersion
            FROM FileRecords
            """;

        using var reader = cmd.ExecuteReader();
        var results = new List<FileRecord>();
        while (reader.Read())
            results.Add(MapRow(reader));
        return results;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static FileRecord MapRow(SqliteDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        FilePath       = r.GetString(1),
        FileHash       = r.GetString(2),
        PerceptualHash = r.IsDBNull(3)  ? null : (ulong)r.GetInt64(3),
        FileSize       = r.GetInt64(4),
        LastModified   = DateTime.Parse(r.GetString(5), null,
                             System.Globalization.DateTimeStyles.RoundtripKind),
        Width          = r.IsDBNull(6)  ? null : r.GetInt32(6),
        Height         = r.IsDBNull(7)  ? null : r.GetInt32(7),
        BlurScore      = r.IsDBNull(8)  ? null : r.GetDouble(8),
        DateTaken      = r.IsDBNull(9)  ? null : DateTime.Parse(r.GetString(9), null,
                             System.Globalization.DateTimeStyles.RoundtripKind),
        CameraModel    = r.IsDBNull(10) ? null : r.GetString(10),
        IsScreenshot   = r.IsDBNull(11) ? null : r.GetInt32(11) != 0,
        LowDetail      = r.IsDBNull(12) ? null : r.GetInt32(12) != 0,
        SchemaVersion  = r.IsDBNull(13) ? 0    : r.GetInt32(13),
    };
}
