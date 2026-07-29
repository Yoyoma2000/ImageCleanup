using ImageCleanup.Data;
using Microsoft.Data.Sqlite;

namespace ImageCleanup.Data.Tests.Helpers;

/// <summary>
/// Provides an isolated in-memory SQLite database per test instance.
/// Each instance keeps the connection open so the :memory: database
/// persists for the lifetime of the fixture.
/// </summary>
public sealed class DbFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }

    public DbFixture()
    {
        // Shared-cache URI lets multiple SqliteConnection objects see the same
        // in-memory database while the keep-alive connection holds it open.
        var dbName = $"test_{Guid.NewGuid():N}";
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        DbInitializer.Initialize(ConnectionString);
    }

    public void Dispose() => _keepAlive.Dispose();
}
