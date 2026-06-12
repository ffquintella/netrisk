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
/// End-to-end Track 6 Phase 6a (deprecate dead tables, reversible) against the real schema on MariaDB:
/// builds through Phase 5 (1..71), applies Phase 6a, and verifies the 23 dead tables are RENAMED to
/// zz_deprecated_* (data preserved, not dropped), the original names are gone, the orphan columns
/// risks.regulation / risks.project_id are still physically present (deprecation defers their drop to 6b),
/// and the Phase 5 risks.status / status_id pair still coexists. db_version -> 72, Success logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase6aTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "71", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p6a.sql"), "-- dump"));
        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    [Fact]
    public async Task Phase6a_RenamesDeadTablesToDeprecated_PreservingDataAndOrphanColumns()
    {
        await fixture.InitializeNumberedSchemaAsync(71);

        // Seed a row into one dead table so we can prove the rename preserves data (does not drop).
        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(seed, "SET SESSION sql_mode = '';");
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 0;");
            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `failed_login_attempts` (`id`) VALUES (7001);");
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 1;");
        }

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p6a-backup-" + Guid.NewGuid().ToString("N")));
        var check = svc.Check("6a", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
        var report = svc.Apply("6a", "homolog", yes: false);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();

        // All 23 candidates renamed to zz_deprecated_*, originals gone.
        Assert.True(23 <= await Scalar(v, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name LIKE 'zz_deprecated_%'"));
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='failed_login_attempts'"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='zz_deprecated_failed_login_attempts'"));

        // Rename preserved the data.
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM `zz_deprecated_failed_login_attempts` WHERE id=7001"));

        // Orphan columns are unmapped in code but still physically present (dropped only in 6b).
        Assert.Equal(2, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name IN ('regulation','project_id')"));
        // Phase 5 status/status_id pair still coexists.
        Assert.Equal(2, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name IN ('status','status_id')"));

        Assert.Equal("72", await Str(v, "SELECT value FROM settings WHERE name='db_version'"));
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "6a").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(72, log.TargetVersion);
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
