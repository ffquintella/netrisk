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
/// End-to-end Track 6 Phase 1c (collation unification) against the real legacy schema on MariaDB:
/// builds the schema through Phase 2b (1..67), applies Phase 1c, and verifies no utf8mb3 tables or
/// columns remain and that data survives the charset conversion (incl. a 4-byte emoji round-trip).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase1cTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Phase1c_ConvertsAllTablesToUtf8mb4_PreservingData()
    {
        await fixture.InitializeNumberedSchemaAsync(67);

        long utf8mb3Before;
        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
            utf8mb3Before = await Scalar(conn, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_collation LIKE 'utf8mb3%'");
            Assert.True(utf8mb3Before > 0, "expected utf8mb3 tables before conversion");
            // Seed a utf8mb3 text column with ASCII content (utf8mb3 can't store emoji yet).
            await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `comments` (`text`, `is_anonymous`) VALUES ('before-convert', 0);");
        }

        var backupDir = Path.Combine(Path.GetTempPath(), "nr-p1c-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "67", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p1c.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        var report = svc.Apply("1c", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var verify = new MySqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();

        // No utf8mb3 tables or columns remain.
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_collation LIKE 'utf8mb3%'"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND character_set_name = 'utf8mb3'"));

        // Existing data survived the conversion.
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM `comments` WHERE `text`='before-convert'"));

        // utf8mb4 now genuinely stores 4-byte characters (the point of the conversion).
        await MariaDbContainerFixture.ExecAsync(verify, "INSERT INTO `comments` (`text`, `is_anonymous`) VALUES ('emoji-\U0001F512', 0);");
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM `comments` WHERE `text` = 'emoji-\U0001F512'"));

        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", verify))
            Assert.Equal("68", (await verCmd.ExecuteScalarAsync())?.ToString());

        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "1c").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(68, log.TargetVersion);
    }

    private static async Task<long> Scalar(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
