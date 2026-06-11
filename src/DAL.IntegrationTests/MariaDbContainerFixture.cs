using System.Runtime.CompilerServices;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Testcontainers.MariaDb;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Boots a throwaway MariaDB container (Testcontainers) once per test run and hands tests a connection
/// string to it. NetRisk's production database is MariaDB (the legacy SimpleRisk-derived schema relies
/// on MariaDB behavior — zero-date defaults, <c>tinyint(4)</c> display widths, utf8mb3), so the harness
/// targets MariaDB rather than MySQL. Requires a working Docker daemon. No external/shared DB is touched.
///
/// Note: EF <c>Database.Migrate()</c> cannot build the schema from scratch here — the EF migrations
/// sit on top of the legacy numbered-SQL base schema (the first migration is IncidentResponsePlan;
/// <c>settings</c>/<c>risks</c> are created by the numbered SQL, not EF). Tests therefore either stand
/// up a minimal real schema via <see cref="ResetMinimalSchemaAsync"/> or the full numbered schema via
/// <see cref="InitializeNumberedSchemaAsync"/>.
/// </summary>
public class MariaDbContainerFixture : IAsyncLifetime
{
    private readonly MariaDbContainer _container = new MariaDbBuilder()
        .WithImage("mariadb:10.11")
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

    /// <summary>
    /// Builds the real production schema in the container by applying the numbered SQL files
    /// (<c>Structure/{n}.sql</c> then <c>Data/{n}.sql</c>) for n = 1..<paramref name="toVersion"/>,
    /// exactly as <c>DatabaseService.Update</c> does in production. This is the faithful test bed for
    /// schema-upgrade phases. Drops and recreates the database first so it is repeatable.
    /// </summary>
    public async Task InitializeNumberedSchemaAsync(int toVersion)
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        var schema = conn.Database;
        // The legacy numbered SQL uses '0000-00-00' defaults that a default-strict MySQL 8 rejects.
        // Production tolerates them via a permissive sql_mode; mirror that for the legacy base schema
        // so the init faithfully reproduces prod (Phase 1 then removes those defaults).
        await ExecAsync(conn, "SET SESSION sql_mode = '';");
        await ExecAsync(conn, $"DROP DATABASE IF EXISTS `{schema}`; CREATE DATABASE `{schema}`; USE `{schema}`;");
        await ExecAsync(conn, "SET SESSION sql_mode = '';");

        var dbDir = RepoDbDir();
        for (var v = 1; v <= toVersion; v++)
        {
            foreach (var sub in new[] { "Structure", "Data" })
            {
                var path = Path.Combine(dbDir, sub, $"{v}.sql");
                if (!File.Exists(path)) continue;
                var sql = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(sql)) continue;
                await ExecAsync(conn, sql);
            }
        }
    }

    /// <summary>Absolute path to <c>src/ConsoleClient/DB</c>, resolved from this source file's location.</summary>
    public static string RepoDbDir([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../src/DAL.IntegrationTests/MariaDbContainerFixture.cs
        var srcDir = Directory.GetParent(thisFile)!.Parent!.FullName; // -> .../src
        return Path.Combine(srcDir, "ConsoleClient", "DB");
    }
}

[CollectionDefinition("mariadb")]
public class MariaDbCollection : ICollectionFixture<MariaDbContainerFixture>;
