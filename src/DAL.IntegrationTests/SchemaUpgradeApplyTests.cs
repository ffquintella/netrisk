using DAL.Context;
using Microsoft.EntityFrameworkCore;
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
/// Exercises the full destructive <see cref="SchemaUpgradeService.Apply"/> orchestration
/// (backup → census → apply numbered SQL → validate → log) against a real MySQL container using a
/// synthetic phase, so the apply path is verified end-to-end without depending on real Track 6 phase SQL.
/// </summary>
[Collection("mysql")]
[Trait("Category", "Integration")]
public class SchemaUpgradeApplyTests(MySqlContainerFixture fixture)
{
    /// <summary>An <see cref="IDalService"/> that returns contexts bound to the test container.</summary>
    private sealed class ContainerDal(MySqlContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Apply_SyntheticPhase_AppliesValidatesAndLogs()
    {
        // Arrange: minimal real schema (settings db_version=63 + schema_upgrade_log), drop synthetic table.
        await fixture.ResetMinimalSchemaAsync(dbVersion: 63, "it_demo");

        // A synthetic phase: db_version 63 -> 64, creating table it_demo.
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Structure"));
        Directory.CreateDirectory(Path.Combine(dir, "Data"));
        await File.WriteAllTextAsync(Path.Combine(dir, "SchemaUpgradePhases.yaml"), """
            phases:
              - phase: "itest"
                description: "integration synthetic phase"
                startVersion: 63
                targetVersion: 64
                census:
                  - name: "settings-count"
                    sql: "SELECT COUNT(*) FROM settings;"
                validations:
                  - type: "TableExists"
                    name: "it_demo-exists"
                    table: "it_demo"
            """);
        await File.WriteAllTextAsync(Path.Combine(dir, "Structure", "64.sql"),
            "CREATE TABLE it_demo (id int NOT NULL AUTO_INCREMENT, label varchar(50) NULL, PRIMARY KEY(id));");
        await File.WriteAllTextAsync(Path.Combine(dir, "Data", "64.sql"),
            "UPDATE settings SET value='64' WHERE name='db_version';");

        var backupDir = NewTempDir();
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "63", ServerVersion = "8.0" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_itest.sql"), "-- dump"));

        var config = Substitute.For<IConfiguration>();
        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), config, Substitute.For<ILogger>())
        {
            DbDirectory = dir,
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        // Act.
        var report = svc.Apply("itest", "homolog", yes: true);

        // Assert: orchestration succeeded and reported a passing validation.
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
        Assert.Contains(report.Checks, c => c.Name == "backup" && c.Passed);
        Assert.Contains(report.Checks, c => c.Name == "apply" && c.Passed);
        Assert.Contains(report.Checks, c => c.Name == "validate:it_demo-exists" && c.Passed);

        await using var verify = new MySqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();

        // The table was actually created.
        Assert.Equal(1, await ScalarAsync(verify,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'it_demo'"));

        // db_version was bumped by the Data file.
        Assert.Equal("64", await StringScalarAsync(verify, "SELECT value FROM settings WHERE name='db_version'"));

        // A Success row was written to the audit log with the backup hash captured.
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs
            .Where(l => l.Phase == "itest")
            .OrderByDescending(l => l.Id)
            .First();
        Assert.Equal("Success", log.Status);
        Assert.False(string.IsNullOrEmpty(log.BackupHash));
        Assert.Equal(64, log.TargetVersion);
    }

    [Fact]
    public async Task Apply_ValidationFailure_RecordsFailedAndKeepsBackupReference()
    {
        await fixture.ResetMinimalSchemaAsync(dbVersion: 63, "it_demo2");

        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Structure"));
        Directory.CreateDirectory(Path.Combine(dir, "Data"));
        await File.WriteAllTextAsync(Path.Combine(dir, "SchemaUpgradePhases.yaml"), """
            phases:
              - phase: "itestfail"
                description: "phase whose validation cannot pass"
                startVersion: 63
                targetVersion: 64
                validations:
                  - type: "TableExists"
                    name: "missing-table"
                    table: "table_that_is_never_created"
            """);
        await File.WriteAllTextAsync(Path.Combine(dir, "Structure", "64.sql"),
            "CREATE TABLE it_demo2 (id int NOT NULL AUTO_INCREMENT, PRIMARY KEY(id));");
        await File.WriteAllTextAsync(Path.Combine(dir, "Data", "64.sql"),
            "UPDATE settings SET value='64' WHERE name='db_version';");

        var backupDir = NewTempDir();
        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "63", ServerVersion = "8.0" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_fail.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = dir, BackupDirectory = backupDir, ConnectionString = fixture.ConnectionString, Operator = "itest"
        };

        var report = svc.Apply("itestfail", "homolog", yes: true);

        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "validate:missing-table" && !c.Passed);
        Assert.Contains("restore", report.Message, StringComparison.OrdinalIgnoreCase);

        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "itestfail").OrderByDescending(l => l.Id).First();
        Assert.Equal("Failed", log.Status);
        Assert.False(string.IsNullOrEmpty(log.BackupPath));
    }

    private static async Task<long> ScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string?> StringScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nr-apply-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
