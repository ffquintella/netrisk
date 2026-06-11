using System.Runtime.CompilerServices;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Boots a throwaway MySQL container (Testcontainers) once per test run and hands tests a connection
/// string to it. Requires a working Docker daemon on the host. No external/shared database is touched.
///
/// Note: EF <c>Database.Migrate()</c> cannot build the schema from scratch here — the EF migrations
/// sit on top of the legacy numbered-SQL base schema (the first migration is IncidentResponsePlan;
/// <c>settings</c>/<c>risks</c> are created by the numbered SQL, not EF). Tests therefore stand up a
/// minimal real schema (the <c>settings</c> table plus the actual shipped <c>schema_upgrade_log</c>
/// DDL from <c>Structure/63.sql</c>) via <see cref="ResetMinimalSchemaAsync"/>.
/// </summary>
public class MySqlContainerFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithDatabase("netrisk")
        .WithUsername("netrisk")
        .WithPassword("netrisk")
        .Build();

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString() + ";AllowUserVariables=true;";
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A fresh EF context bound to the container.</summary>
    public AuditableContext NewContext()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
            .Options;
        return new AuditableContext(options);
    }

    /// <summary>
    /// Drops and recreates the minimal schema the schema-upgrade tool touches — <c>settings</c> (seeded
    /// with <c>db_version</c>) and the audit table built from the shipped <c>Structure/63.sql</c> — plus
    /// removes any synthetic test tables so each test starts clean.
    /// </summary>
    public async Task ResetMinimalSchemaAsync(int dbVersion = 63, params string[] extraTablesToDrop)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var t in new[] { "schema_upgrade_log", "settings", "audit" }.Concat(extraTablesToDrop))
            await ExecAsync(conn, $"DROP TABLE IF EXISTS `{t}`;");

        await ExecAsync(conn,
            "CREATE TABLE `settings` (`name` varchar(100) NOT NULL DEFAULT '', `value` mediumtext NULL, PRIMARY KEY(`name`));");
        await ExecAsync(conn, $"INSERT INTO `settings` (`name`,`value`) VALUES ('db_version','{dbVersion}');");

        // The auditing pipeline (AuditableContext.SaveChanges) writes a row to `audit` on every save,
        // so the schema-upgrade tool's own log write needs this table present (as it is in prod).
        // Columns are PascalCase because the Audit entity has no HasColumnName overrides.
        await ExecAsync(conn,
            "CREATE TABLE `audit` (`Id` int NOT NULL AUTO_INCREMENT, `UserId` int NOT NULL, " +
            "`Type` longtext NULL, `TableName` longtext NULL, `DateTime` datetime NULL, " +
            "`OldValues` longtext NULL, `NewValues` longtext NULL, `AffectedColumns` text NULL, " +
            "`PrimaryKey` longtext NULL, PRIMARY KEY(`Id`));");

        // Apply the real shipped DDL for the audit table so the integration test validates that artifact.
        await ExecAsync(conn, File.ReadAllText(Path.Combine(RepoDbDir(), "Structure", "63.sql")));
    }

    public static async Task ExecAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Absolute path to <c>src/ConsoleClient/DB</c>, resolved from this source file's location.</summary>
    public static string RepoDbDir([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../src/DAL.IntegrationTests/MySqlContainerFixture.cs
        var srcDir = Directory.GetParent(thisFile)!.Parent!.FullName; // -> .../src
        return Path.Combine(srcDir, "ConsoleClient", "DB");
    }
}

[CollectionDefinition("mysql")]
public class MySqlCollection : ICollectionFixture<MySqlContainerFixture>;
