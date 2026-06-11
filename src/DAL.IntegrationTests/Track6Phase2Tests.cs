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
/// End-to-end Track 6 Phase 2 (snake_case naming) against the real legacy schema on MariaDB: builds the
/// schema through Phase 1 (1..64), seeds rows, applies Phase 2 through the actual service + shipped
/// manifest/SQL, and verifies tables/columns are renamed via RENAME (data + row counts preserved),
/// the old PascalCase names are gone, db_version is bumped, and a Success row is logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase2Tests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Phase2_RenamesTablesAndColumns_PreservingData()
    {
        await fixture.InitializeNumberedSchemaAsync(64); // full legacy schema + Phase 1

        long fixReqBefore, bioBefore;
        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';"); // legacy NOT NULL cols get implicit defaults
            // hosts has no blocking FK, so we can seed it to prove column renames preserve values.
            await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `hosts` (`FQDN`, `OS`) VALUES ('h.example.com', 'linux');");
            // The renamed tables are FK-heavy; row-count parity (here, the existing counts) proves the
            // RENAME moved the table intact. The migration was independently verified to be RENAME-only.
            fixReqBefore = await Scalar(conn, "SELECT COUNT(*) FROM `FixRequest`");
            bioBefore = await Scalar(conn, "SELECT COUNT(*) FROM `BiometricTransaction`");
        }

        var backupDir = Path.Combine(Path.GetTempPath(), "nr-p2-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "64", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p2.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        var check = svc.Check("2", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = svc.Apply("2", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var verify = new MySqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();

        // Tables renamed to snake_case; old PascalCase names gone (binary/case-sensitive check).
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND BINARY table_name='incidents'"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND BINARY table_name='Incidents'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND BINARY table_name='biometric_transactions'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND BINARY table_name='incident_response_plan_task_executions'"));

        // Row-count parity across the table renames.
        Assert.Equal(fixReqBefore, await Scalar(verify, "SELECT COUNT(*) FROM `fix_requests`"));
        Assert.Equal(bioBefore, await Scalar(verify, "SELECT COUNT(*) FROM `biometric_transactions`"));

        // Column renames preserved data and values (hosts.FQDN/OS -> fqdn/os).
        await using (var cmd = new MySqlCommand("SELECT `fqdn`, `os` FROM `hosts` WHERE `fqdn`='h.example.com'", verify))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync(), "renamed hosts row not found");
            Assert.Equal("h.example.com", r.GetString(0));
            Assert.Equal("linux", r.GetString(1));
        }

        // reports.created_at column exists (creationDate renamed).
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='reports' AND column_name='created_at'"));

        // db_version bumped + Success logged.
        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", verify))
            Assert.Equal("65", (await verCmd.ExecuteScalarAsync())?.ToString());

        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "2").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(65, log.TargetVersion);
    }

    private static async Task<long> Scalar(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
