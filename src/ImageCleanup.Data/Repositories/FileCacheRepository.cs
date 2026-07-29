using ImageCleanup.Data.Models;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Repositories;

public sealed class FileCacheRepository
{
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
                   Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot
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
                 Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot)
            VALUES
                ($filePath, $fileHash, $perceptualHash, $fileSize, $lastModified,
                 $width, $height, $blurScore, $dateTaken, $cameraModel, $isScreenshot)
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
                IsScreenshot   = excluded.IsScreenshot
            RETURNING Id
            """;

        cmd.Parameters.AddWithValue("$filePath", record.FilePath);
        cmd.Parameters.AddWithValue("$fileHash", record.FileHash);
        cmd.Parameters.AddWithValue("$perceptualHash", record.PerceptualHash.HasValue
            ? (object)(long)record.PerceptualHash.Value   // store ulong as signed long
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$fileSize", record.FileSize);
        cmd.Parameters.AddWithValue("$lastModified", record.LastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$width", record.Width.HasValue ? (object)record.Width.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$height", record.Height.HasValue ? (object)record.Height.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$blurScore", record.BlurScore.HasValue ? (object)record.BlurScore.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$dateTaken", record.DateTaken.HasValue
            ? (object)record.DateTaken.Value.ToString("O")
            : DBNull.Value);
        cmd.Parameters.AddWithValue("$cameraModel", record.CameraModel ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isScreenshot", record.IsScreenshot.HasValue
            ? (object)(record.IsScreenshot.Value ? 1 : 0)
            : DBNull.Value);

        var id = cmd.ExecuteScalar();
        if (id is long lid)
            record.Id = (int)lid;
    }

    public bool NeedsRescan(string path, long fileSize, DateTime lastModified)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT FileSize, LastModified
            FROM FileRecords
            WHERE FilePath = $path
            """;
        cmd.Parameters.AddWithValue("$path", path);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return true;  // no cached row

        var cachedSize = reader.GetInt64(0);
        var cachedModified = DateTime.Parse(
            reader.GetString(1), null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        return cachedSize != fileSize || cachedModified != lastModified;
    }

    public IEnumerable<FileRecord> GetAllRecords()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FilePath, FileHash, PerceptualHash, FileSize, LastModified,
                   Width, Height, BlurScore, DateTaken, CameraModel, IsScreenshot
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
        PerceptualHash = r.IsDBNull(3) ? null : (ulong)r.GetInt64(3),
        FileSize       = r.GetInt64(4),
        LastModified   = DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Width          = r.IsDBNull(6) ? null : r.GetInt32(6),
        Height         = r.IsDBNull(7) ? null : r.GetInt32(7),
        BlurScore      = r.IsDBNull(8) ? null : r.GetDouble(8),
        DateTaken      = r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
        CameraModel    = r.IsDBNull(10) ? null : r.GetString(10),
        IsScreenshot   = r.IsDBNull(11) ? null : r.GetInt32(11) != 0,
    };
}
