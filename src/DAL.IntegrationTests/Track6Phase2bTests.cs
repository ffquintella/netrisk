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
/// End-to-end Track 6 Phase 2b against the real legacy schema on MariaDB: builds the schema through
/// Phase 1b (1..66), applies Phase 2b, and verifies comments.IsAnonymous is renamed to is_anonymous
/// (old column gone) with its value preserved.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase2bTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Phase2b_RenamesIsAnonymousColumn_PreservingValue()
    {
        await fixture.InitializeNumberedSchemaAsync(66); // legacy schema + Phase 1, 2, 1b

        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
            await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `comments` (`IsAnonymous`, `text`) VALUES (1, 'seed');");
            Assert.Equal(1, await Scalar(conn, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='comments' AND BINARY column_name='IsAnonymous'"));
        }

        var backupDir = Path.Combine(Path.GetTempPath(), "nr-p2b-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "66", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p2b.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        var report = svc.Apply("2b", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var verify = new MySqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();

        // New column present, old gone, value preserved.
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='comments' AND column_name='is_anonymous'"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='comments' AND BINARY column_name='IsAnonymous'"));
        Assert.Equal(1, await Scalar(verify, "SELECT `is_anonymous` FROM `comments` WHERE `text`='seed'"));

        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", verify))
            Assert.Equal("67", (await verCmd.ExecuteScalarAsync())?.ToString());

        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "2b").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(67, log.TargetVersion);
    }

    private static async Task<long> Scalar(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
