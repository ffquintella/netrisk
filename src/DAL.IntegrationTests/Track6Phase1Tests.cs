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
/// End-to-end Track 6 Phase 1 (safe fixes) against the real legacy schema on MariaDB: builds the full
/// numbered schema (1..63), applies Phase 1 through the actual <see cref="SchemaUpgradeService"/> +
/// shipped manifest/SQL, and verifies the typo indexes are renamed, the illegal 0000-00-00 defaults
/// are gone, db_version is bumped, and a Success row is logged.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track6Phase1Tests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Phase1_Applies_RenamesIndexes_And_FixesZeroDateDefaults()
    {
        await fixture.InitializeNumberedSchemaAsync(63);

        var backupDir = Path.Combine(Path.GetTempPath(), "nr-p1-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "63", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
          .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p1.sql"), "-- dump"));

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),  // the real shipped manifest + 64.sql
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };

        // Pre-flight should pass against the real legacy schema at db_version 63.
        var check = svc.Check("1", "homolog");
        Assert.True(check.Success, string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = svc.Apply("1", "homolog", yes: true);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Index typos renamed.
        Assert.Equal(1, await IndexCount(conn, "BiometricTransaction", "idx_biometric_transaction_id"));
        Assert.Equal(0, await IndexCount(conn, "BiometricTransaction", "idx_biometic_id"));
        Assert.Equal(1, await IndexCount(conn, "IncidentResponsePlanTasks", "idx_irpt_sequential"));
        Assert.Equal(0, await IndexCount(conn, "IncidentResponsePlanTasks", "idx_irpt_optinal"));

        // Illegal zero-date default removed from mgmt_reviews.next_review.
        var nextReviewDefault = await ColumnDefault(conn, "mgmt_reviews", "next_review");
        Assert.True(string.IsNullOrEmpty(nextReviewDefault) || !nextReviewDefault!.Contains("0000-00-00"),
            $"next_review default still '{nextReviewDefault}'");

        // mitigations.last_update default is now a valid current_timestamp (not 0000-00-00).
        var lastUpdateDefault = await ColumnDefault(conn, "mitigations", "last_update");
        Assert.False(lastUpdateDefault?.Contains("0000-00-00") ?? false,
            $"last_update default still '{lastUpdateDefault}'");

        // db_version bumped.
        await using var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", conn);
        Assert.Equal("64", (await verCmd.ExecuteScalarAsync())?.ToString());

        // Success row logged.
        await using var logCtx = fixture.NewContext();
        var log = logCtx.SchemaUpgradeLogs.Where(l => l.Phase == "1").OrderByDescending(l => l.Id).First();
        Assert.Equal("Success", log.Status);
        Assert.Equal(64, log.TargetVersion);
    }

    private static async Task<long> IndexCount(MySqlConnection conn, string table, string index)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND index_name = @i", conn);
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@i", index);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string?> ColumnDefault(MySqlConnection conn, string table, string column)
    {
        await using var cmd = new MySqlCommand(
            "SELECT column_default FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }
}
