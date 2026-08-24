using System;
using System.IO;
using System.Threading.Tasks;
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
/// End-to-end Phase 7 (Track 2 milestone 2.4.3) against the real schema on MariaDB: builds the
/// numbered schema through 75, applies Phase 7 through the actual upgrade service and the shipped
/// manifest/SQL, and verifies the dependency table, its unique edge index and both cascading
/// foreign keys exist, the override columns are on the task table, and db_version reaches 76.
///
/// This is the half of the migration ritual that actually reaches a production database — the EF
/// migration only keeps the model and snapshot honest — so it is worth proving it applies rather
/// than assuming the hand-written SQL matches what EF generated.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Phase7IrpDependenciesTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) => f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "75", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
            .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p7.sql"), "-- dump"));

        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    private static async Task<string> ScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "";
    }

    [Fact]
    public async Task Phase7_CreatesDependencyEdgesAndOverrideColumns()
    {
        await fixture.InitializeNumberedSchemaAsync(75);

        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p7-backup-" + Guid.NewGuid().ToString("N")));

        var check = svc.Check("7", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = svc.Apply("7", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name = 'incident_response_plan_task_dependencies';"));

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'incident_response_plan_tasks' " +
            "AND column_name IN ('override_reason','overridden_by_id','overridden_at');"));

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema = DATABASE() " +
            "AND table_name = 'incident_response_plan_task_dependencies' AND constraint_type = 'FOREIGN KEY';"));

        Assert.Equal("76", await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version';"));
    }

    [Fact]
    public async Task Phase7_TheSameEdgeCannotBeStoredTwice()
    {
        await fixture.InitializeNumberedSchemaAsync(75);
        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p7-dup-" + Guid.NewGuid().ToString("N")));
        Assert.True(svc.Apply("7", "homolog", yes: true).Success);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `incident_response_plan_tasks` (`Id`,`Name`,`PlanId`) VALUES (1,'A',1),(2,'B',1);");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `incident_response_plan_task_dependencies` (`task_id`,`depends_on_task_id`,`created_at`) " +
            "VALUES (2,1,NOW());");

        await Assert.ThrowsAsync<MySqlException>(async () =>
            await MariaDbContainerFixture.ExecAsync(conn,
                "INSERT INTO `incident_response_plan_task_dependencies` (`task_id`,`depends_on_task_id`,`created_at`) " +
                "VALUES (2,1,NOW());"));
    }

    [Fact]
    public async Task Phase7_DeletingATaskTakesItsEdgesWithIt()
    {
        await fixture.InitializeNumberedSchemaAsync(75);
        var svc = NewService(Path.Combine(Path.GetTempPath(), "nr-p7-cascade-" + Guid.NewGuid().ToString("N")));
        Assert.True(svc.Apply("7", "homolog", yes: true).Success);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `incident_response_plan_tasks` (`Id`,`Name`,`PlanId`) VALUES (1,'A',1),(2,'B',1);");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `incident_response_plan_task_dependencies` (`task_id`,`depends_on_task_id`,`created_at`) " +
            "VALUES (2,1,NOW());");

        // Removing the predecessor must not leave an edge pointing at a row that is gone.
        await MariaDbContainerFixture.ExecAsync(conn, "DELETE FROM `incident_response_plan_tasks` WHERE `Id` = 1;");

        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM `incident_response_plan_task_dependencies`;"));
    }
}
