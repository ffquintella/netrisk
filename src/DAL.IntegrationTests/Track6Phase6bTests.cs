using DAL.Context;
using DAL.Entities;
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
/// End-to-end Track 6 Phase 6b (DESTRUCTIVE drop of deprecated tables) against the real schema on MariaDB:
/// builds through Phase 6a (1..72, tables already renamed to zz_deprecated_*), records an aged 6a Success in
/// schema_upgrade_log to satisfy the observation-window gate, then verifies the destructive --yes requirement
/// (yes:false aborts) and that a real apply drops every zz_deprecated_* table plus the orphan columns
/// risks.regulation / risks.project_id while leaving the Phase 5 risks.status text in place (coexist).
/// db_version -> 73, Success logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase6bTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "72", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p6b.sql"), "-- dump"));
        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    [Fact]
    public async Task Phase6b_DropsDeprecatedTablesAndOrphanColumns_GatedByObservationAndYes()
    {
        await fixture.InitializeNumberedSchemaAsync(72);

        // Satisfy the destructive observation-window gate: an aged, successful 6a entry.
        await using (var ctx = fixture.NewContext())
        {
            ctx.SchemaUpgradeLogs.Add(new SchemaUpgradeLog
            {
                Phase = "6a", StartVersion = 71, TargetVersion = 72, Status = "Success",
                Environment = "homolog", Operator = "itest", AppliedAt = DateTime.UtcNow.AddDays(-40)
            });
            await ctx.SaveChangesAsync();
        }

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p6b-backup-" + Guid.NewGuid().ToString("N")));

        // Pre-flight passes (gate met), but a destructive phase still refuses without --yes.
        var check = svc.Check("6b", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
        var refused = svc.Apply("6b", "homolog", yes: false);
        Assert.False(refused.Success);
        Assert.Contains(refused.Checks, c => c.Name == "confirmation" && !c.Passed);

        // Sanity: deprecated tables are still present before the real apply.
        await using (var pre = new MySqlConnection(fixture.ConnectionString))
        {
            await pre.OpenAsync();
            Assert.True(0 < await Scalar(pre, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name LIKE 'zz_deprecated_%'"));
        }

        var report = svc.Apply("6b", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();

        // Every deprecated table is gone.
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name LIKE 'zz_deprecated_%'"));
        // Orphan columns dropped; legacy status text retained (its status_id replacement must coexist a release).
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name IN ('regulation','project_id')"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name='status'"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='risks' AND column_name='status_id'"));

        Assert.Equal("73", await Str(v, "SELECT value FROM settings WHERE name='db_version'"));
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "6b" && l.Status == "Success").OrderByDescending(l => l.Id).First();
        Assert.Equal(73, log.TargetVersion);
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
