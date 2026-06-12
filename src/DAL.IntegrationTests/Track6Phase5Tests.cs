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
/// End-to-end Track 6 Phase 5 (status type standardization, create-copy-coexist) against the real schema on
/// MariaDB: builds through Phase 4 (1..70), seeds risks with each known status string plus an unmapped legacy
/// value, applies Phase 5 through the actual service + shipped manifest/SQL, and verifies risks.status_id is a
/// new int column backfilled per the RiskStatus mapping (New=0, Mitigation Planned=1, Mgmt Reviewed=2,
/// Closed=3) with unmapped rows left NULL, while the legacy `status` text column is retained (coexist).
/// db_version -> 71, Success logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase5Tests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "70", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p5.sql"), "-- dump"));
        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    [Fact]
    public async Task Phase5_AddsStatusIdColumn_BackfilledFromStatusText_CoexistingWithLegacy()
    {
        await fixture.InitializeNumberedSchemaAsync(70);
        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(seed, "SET SESSION sql_mode = '';");
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 0;");
            // Non-strict mode fills the other NOT NULL risk columns with implicit defaults; we only care about status.
            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `risks` (`id`,`status`) VALUES " +
                "(8001,'New'),(8002,'Mitigation Planned'),(8003,'Mgmt Reviewed'),(8004,'Closed'),(8005,'Some Legacy Value');");
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 1;");
        }

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p5-backup-" + Guid.NewGuid().ToString("N")));
        var check = svc.Check("5", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
        var report = svc.Apply("5", "homolog", yes: false);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();

        // New status_id column is an int, and the legacy text `status` column is retained (coexist).
        Assert.Equal("int", await Str(v, "SELECT data_type FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name='status_id'"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name='status'"));

        // Backfill mapped the known strings; the unmapped legacy value stayed NULL.
        Assert.Equal("0", await Str(v, "SELECT status_id FROM risks WHERE id=8001"));
        Assert.Equal("1", await Str(v, "SELECT status_id FROM risks WHERE id=8002"));
        Assert.Equal("2", await Str(v, "SELECT status_id FROM risks WHERE id=8003"));
        Assert.Equal("3", await Str(v, "SELECT status_id FROM risks WHERE id=8004"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM risks WHERE id=8005 AND status_id IS NULL"));

        Assert.Equal("71", await Str(v, "SELECT value FROM settings WHERE name='db_version'"));
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "5").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(71, log.TargetVersion);
    }

    private static async Task<long> Scalar(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string> Str(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
    }
}
