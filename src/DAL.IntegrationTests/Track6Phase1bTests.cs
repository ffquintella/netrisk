using DAL.Context;
using Microsoft.Extensions.Configuration;
using Model.Database;
using MySqlConnector;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.SchemaUpgrade;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// End-to-end Track 6 Phase 1b (deferred boolean width normalization) against the real legacy schema
/// on MariaDB: builds the schema through Phase 2 (1..65), applies Phase 1b, and verifies the boolean
/// columns become tinyint(1) without losing their 0/1 values.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase1bTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Phase1b_NormalizesBooleanColumns_ToTinyint1_PreservingValues()
    {
        await fixture.InitializeNumberedSchemaAsync(65); // legacy schema + Phase 1 + Phase 2

        // Seed a framework_controls row with deleted=1 to confirm the value survives the type change.
        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
            await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `framework_controls` (`deleted`) VALUES (1);");
            // Pre-condition: column is tinyint(4) before the phase.
            Assert.Equal("tinyint(4)", await ColType(conn, "framework_controls", "deleted"));
        }

        var backupDir = Path.Combine(Path.GetTempPath(), "nr-p1b-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "65", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p1b.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        var report = svc.Apply("1b", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var verify = new MySqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();

        // Columns are now tinyint(1).
        Assert.Equal("tinyint(1)", await ColType(verify, "framework_controls", "deleted"));
        Assert.Equal("tinyint(1)", await ColType(verify, "comments", "IsAnonymous"));

        // The seeded value is preserved.
        await using (var cmd = new MySqlCommand("SELECT `deleted` FROM `framework_controls` WHERE `deleted` = 1 LIMIT 1", verify))
            Assert.Equal(1, Convert.ToInt64(await cmd.ExecuteScalarAsync()));

        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", verify))
            Assert.Equal("66", (await verCmd.ExecuteScalarAsync())?.ToString());

        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "1b").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(66, log.TargetVersion);
    }

    private static async Task<string?> ColType(MySqlConnection conn, string table, string column)
    {
        await using var cmd = new MySqlCommand(
            "SELECT column_type FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }
}
