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
/// End-to-end Track 6 Phase 4 (indexing + BLOB-to-text) against the real schema on MariaDB: builds through
/// Phase 3 (1..69), seeds multi-byte UTF-8 into each text-bearing BLOB column, applies Phase 4 through the
/// actual service + shipped manifest/SQL, and verifies the columns are now varchar/TEXT with content that
/// reads back byte-identical, the Sieve-justified hot-path indexes exist (and appear in EXPLAIN possible_keys),
/// and the redundant UNIQUE id index on framework_control_tests is gone. db_version -> 70, Success logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase4Tests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    // user.email is app-written UTF-8 -> converts directly, must read back byte-identical.
    private const string Utf8Email = "tëst@exämple.com";
    // The legacy framework/permission BLOBs hold Windows-1252/latin1 seed bytes. We seed raw cp1252 bytes via
    // UNHEX and assert the latin1->utf8mb4 round-trip transcodes them to the correct Unicode characters:
    //   43 61 66 E9 94      = C a f é(0xE9) ”(0x94)         -> "Café”"
    //   50 65 72 6D 69 73 73 E3 6F = P e r m i s s ã(0xE3) o -> "Permissão"
    //   4E F3 6F 6E 6F      = N ó(0xF3) o n o               -> "Nóono"
    private const string Cp1252NameHex = "436166E994";
    private const string ExpectedName = "Café”";
    private const string Cp1252PermHex = "5065726D697373E36F";
    private const string ExpectedPerm = "Permissão";
    private const string Cp1252DescHex = "4EF36F6E6F";
    private const string ExpectedDesc = "Nóono";

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "69", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p4.sql"), "-- dump"));
        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    [Fact]
    public async Task Phase4_ConvertsBlobToText_PreservingMultiByte_AndAddsIndexes()
    {
        await fixture.InitializeNumberedSchemaAsync(69);
        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(seed, "SET SESSION sql_mode = '';");
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 0;");
            // Legacy BLOBs: seed exact cp1252/latin1 bytes via UNHEX (proves the round-trip transcoding).
            await MariaDbContainerFixture.ExecAsync(seed,
                $"INSERT INTO `frameworks` (`value`,`parent`,`name`,`description`,`status`,`order`) VALUES (9001,0,UNHEX('{Cp1252NameHex}'),UNHEX('{Cp1252DescHex}'),1,1);");
            await MariaDbContainerFixture.ExecAsync(seed,
                $"INSERT INTO `permissions` (`id`,`key`,`name`,`description`,`order`) VALUES (9001,'zztestperm','N',UNHEX('{Cp1252PermHex}'),1);");
            // user.email: app-written UTF-8 (direct conversion path).
            await ParamExec(seed, "INSERT INTO `user` (`value`,`name`,`login`,`email`) VALUES (9001,'U','zztestuser',@e)",
                ("@e", Utf8Email));
            await MariaDbContainerFixture.ExecAsync(seed, "SET FOREIGN_KEY_CHECKS = 1;");
        }

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p4-backup-" + Guid.NewGuid().ToString("N")));
        var check = svc.Check("4", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
        var report = svc.Apply("4", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();

        // Column types converted (no longer blob).
        Assert.Equal("varchar", await Str(v, "SELECT data_type FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='user' AND column_name='email'"));
        Assert.Equal("text", await Str(v, "SELECT data_type FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='frameworks' AND column_name='name'"));
        Assert.Equal("text", await Str(v, "SELECT data_type FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='permissions' AND column_name='description'"));
        Assert.Equal("utf8mb4", await Str(v, "SELECT character_set_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='frameworks' AND column_name='name'"));

        // Legacy cp1252 bytes transcoded to the correct Unicode; app-written UTF-8 email preserved verbatim.
        Assert.Equal(ExpectedName, await Str(v, "SELECT `name` FROM `frameworks` WHERE `value`=9001"));
        Assert.Equal(ExpectedDesc, await Str(v, "SELECT `description` FROM `frameworks` WHERE `value`=9001"));
        Assert.Equal(Utf8Email, await Str(v, "SELECT `email` FROM `user` WHERE `value`=9001"));
        Assert.Equal(ExpectedPerm, await Str(v, "SELECT `description` FROM `permissions` WHERE `id`=9001"));

        // Hot-path indexes present.
        foreach (var (table, idx) in new[]
                 {
                     ("vulnerabilities", "idx_vulnerabilities_first_detection"),
                     ("vulnerabilities", "idx_vulnerabilities_last_detection"),
                     ("hosts", "idx_hosts_status"),
                     ("hosts", "idx_hosts_registration_date"),
                     ("risks", "idx_risks_status_submission_date"),
                     ("user", "idx_user_email"),
                 })
            Assert.True(0 < await Scalar(v, $"SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='{table}' AND index_name='{idx}'"),
                $"missing index {idx} on {table}");

        // The added index is usable for the indexed predicate (appears in EXPLAIN possible_keys — stable
        // regardless of row count, unlike the chosen `key` which the optimizer skips on tiny tables).
        await using (var ex = new MySqlCommand("EXPLAIN SELECT * FROM `vulnerabilities` WHERE `FirstDetection` > '2020-01-01'", v))
        await using (var r = await ex.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            var possible = r["possible_keys"]?.ToString() ?? "";
            Assert.Contains("idx_vulnerabilities_first_detection", possible);
        }

        // Redundant UNIQUE id index removed from framework_control_tests (PK remains).
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='framework_control_tests' AND index_name='id'"));
        Assert.True(0 < await Scalar(v, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='framework_control_tests' AND index_name='PRIMARY'"));

        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", v))
            Assert.Equal("70", (await verCmd.ExecuteScalarAsync())?.ToString());
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "4").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(70, log.TargetVersion);
    }

    private static async Task ParamExec(MySqlConnection conn, string sql, params (string, string)[] ps)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var (k, val) in ps) cmd.Parameters.AddWithValue(k, val);
        await cmd.ExecuteNonQueryAsync();
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
