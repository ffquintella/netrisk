using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebSiteData;
using WebSiteData.Entities;

namespace WebSite.Tests.Sync;

/// <summary>
/// Test double for <see cref="IDbContextFactory{TContext}"/> backed by a real in-memory SQLite
/// database. The connection is kept open for the lifetime of the instance so every context
/// handed out sees the same schema and data, exactly like the production factory does over a
/// file-backed SQLite database.
/// </summary>
public sealed class SqliteDbContextFactory : IDbContextFactory<WebSiteDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public WebSiteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebSiteDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;
        return new WebSiteDbContext(options);
    }

    /// <summary>Writes the single <see cref="SyncState"/> row, simulating an enrolled website.</summary>
    public void SeedSyncState(string? keyId, string? publicKeyPem)
    {
        using var db = CreateDbContext();
        var state = db.SyncState.FirstOrDefault();
        if (state == null)
        {
            state = new SyncState { Id = 1 };
            db.SyncState.Add(state);
        }
        state.ApiKeyId = keyId;
        state.ApiPublicKeyPem = publicKeyPem;
        state.EnrolledAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
