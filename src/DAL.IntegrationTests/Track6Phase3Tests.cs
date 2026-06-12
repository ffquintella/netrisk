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
/// End-to-end Track 6 Phase 3 (relationships) against the real legacy schema on MariaDB: builds the schema
/// through Phase 2b/1c (1..68), seeds valid + dangling correlation values and incidents with matching /
/// non-matching free-text reported_by, applies Phase 3 through the actual service + shipped manifest/SQL,
/// and verifies: orphan rows are logged BEFORE nulling, dangling refs are NULLed (valid untouched),
/// reported_by_id is backfilled only for an unambiguous user.name match (text preserved), every FK
/// constraint + index exists, db_version is bumped to 69, and a Success row is logged. Two focused tests
/// prove the cleanup is mandatory (ADD CONSTRAINT fails on un-cleaned data) and ON DELETE SET NULL behavior.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase3Tests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "68", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p3.sql"), "-- dump"));
        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    private static async Task SeedAsync(MySqlConnection conn)
    {
        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;"); // seed without satisfying unrelated legacy FKs (e.g. user.role_id)
        // Two real users (PK = value); name drives the reported_by backfill.
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `user` (`value`,`name`,`login`) VALUES (10,'Alice','alice'),(20,'Bob','bob');");
        // Risks: id=1 all valid; id=2 dangling owner + dangling submitted_by, valid manager.
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `risks` (`id`,`owner`,`manager`,`submitted_by`,`status`,`subject`) VALUES " +
            "(1,10,20,10,'New','valid'),(2,999,10,999,'New','dangling');");
        // Framework controls / tests: one valid, one dangling.
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `framework_controls` (`id`,`control_owner`) VALUES (1,10),(2,888);");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `framework_control_tests` (`id`,`tester`) VALUES (1,20),(2,777);");
        // Incidents: id=1 reported_by matches a unique user.name -> backfilled; id=2 external text -> stays NULL.
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `incidents` (`Id`,`CreatedById`,`ReportedBy`) VALUES (1,10,'Alice'),(2,10,'External Vendor');");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");
    }

    [Fact]
    public async Task Phase3_CleansOrphans_BackfillsReportedBy_AddsConstraints()
    {
        await fixture.InitializeNumberedSchemaAsync(68);
        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();
            await SeedAsync(seed);
        }

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p3-backup-" + Guid.NewGuid().ToString("N")));

        var check = svc.Check("3", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = svc.Apply("3", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();

        // (a) Orphan rows captured (3 dangling: risks.owner=999, risks.submitted_by=999, fc.control_owner=888,
        //     fct.tester=777 -> 4 rows). Assert the dangling values were logged before nulling.
        Assert.Equal(4, await Scalar(v, "SELECT COUNT(*) FROM `schema_upgrade_orphans` WHERE phase='3'"));
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM `schema_upgrade_orphans` WHERE table_name='risks' AND column_name='owner' AND dangling_value='999'"));

        // (b) Dangling refs NULLed; valid refs untouched.
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `risks` WHERE id=2 AND `owner` IS NOT NULL"));
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `risks` WHERE id=2 AND `submitted_by` IS NOT NULL"));
        Assert.Equal(10L, await Scalar(v, "SELECT `owner` FROM `risks` WHERE id=1"));
        Assert.Equal(10L, await Scalar(v, "SELECT `manager` FROM `risks` WHERE id=2"));
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `framework_controls` WHERE id=2 AND `control_owner` IS NOT NULL"));
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `framework_control_tests` WHERE id=2 AND `tester` IS NOT NULL"));

        // (c) reported_by_id backfilled only for the unambiguous match; the free text is preserved both ways.
        Assert.Equal(10L, await Scalar(v, "SELECT `reported_by_id` FROM `incidents` WHERE Id=1"));
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `incidents` WHERE Id=2 AND `reported_by_id` IS NOT NULL"));
        await using (var cmd = new MySqlCommand("SELECT `ReportedBy` FROM `incidents` WHERE Id=2", v))
            Assert.Equal("External Vendor", (await cmd.ExecuteScalarAsync())?.ToString());

        // (d) FK constraints present.
        foreach (var fk in new[] { "fk_risks_owner", "fk_risks_manager", "fk_risks_submitted_by",
                     "fk_framework_controls_control_owner", "fk_framework_control_tests_tester", "fk_incidents_reported_by" })
            Assert.Equal(1, await Scalar(v, $"SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND constraint_type='FOREIGN KEY' AND constraint_name='{fk}'"));

        // (e) Every FK column is indexed.
        Assert.True(0 < await Scalar(v, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='incidents' AND column_name='reported_by_id'"));
        Assert.True(0 < await Scalar(v, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='framework_controls' AND column_name='control_owner'"));
        Assert.True(0 < await Scalar(v, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='framework_control_tests' AND column_name='tester'"));

        // db_version bumped + Success logged.
        await using (var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", v))
            Assert.Equal("69", (await verCmd.ExecuteScalarAsync())?.ToString());
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "3").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(69, log.TargetVersion);
    }

    [Fact]
    public async Task Phase3_AddConstraint_FailsWithoutOrphanCleanup()
    {
        await fixture.InitializeNumberedSchemaAsync(68);
        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");
        await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `user` (`value`,`name`,`login`) VALUES (10,'Alice','alice');");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");
        // A dangling owner with NO matching user, and the column widened to NULL — but NOT cleaned.
        await MariaDbContainerFixture.ExecAsync(conn, "ALTER TABLE `risks` MODIFY COLUMN `owner` int(11) NULL;");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `risks` (`id`,`owner`,`manager`,`submitted_by`,`status`,`subject`) VALUES (1,999,10,10,'New','x');");

        // Adding the FK against un-cleaned data must fail — proving the cleanup step is mandatory and ordered.
        var ex = await Assert.ThrowsAsync<MySqlException>(() => MariaDbContainerFixture.ExecAsync(conn,
            "ALTER TABLE `risks` ADD CONSTRAINT `fk_risks_owner` FOREIGN KEY (`owner`) REFERENCES `user` (`value`) ON DELETE SET NULL;"));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Phase3_OnDeleteSetNull_NullsReferencingColumn()
    {
        await fixture.InitializeNumberedSchemaAsync(68);
        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();
            await SeedAsync(seed);
        }
        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p3sn-backup-" + Guid.NewGuid().ToString("N")));
        Assert.True(svc.Apply("3", "homolog", yes: true).Success);

        await using var v = new MySqlConnection(fixture.ConnectionString);
        await v.OpenAsync();
        // Risk 1 owner = user 10. Deleting user 10 must SET NULL the referencing column, not block/cascade.
        await MariaDbContainerFixture.ExecAsync(v, "DELETE FROM `user` WHERE `value`=10;");
        Assert.Equal(0, await Scalar(v, "SELECT COUNT(*) FROM `risks` WHERE id=1 AND `owner` IS NOT NULL"));
        // The risk row itself survives (SET NULL, not cascade delete).
        Assert.Equal(1, await Scalar(v, "SELECT COUNT(*) FROM `risks` WHERE id=1"));
    }

    private static async Task<long> Scalar(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
