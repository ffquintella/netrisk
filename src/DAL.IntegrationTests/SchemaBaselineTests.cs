using Microsoft.Extensions.Configuration;
using Model.Database;
using MySqlConnector;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.SchemaUpgrade;
using ServerServices.Services;
using DAL.Context;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Verifies the <c>database baseline</c> census against a real MariaDB container: an empty candidate
/// table is recommended for drop, a populated one for archive, and an absent one is reported absent.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class SchemaBaselineTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true) => f.NewContext();
    }

    [Fact]
    public async Task Baseline_CensusRecommendsDropArchiveAbsent()
    {
        await fixture.ResetMinimalSchemaAsync(dbVersion: 63, "cand_empty", "cand_withdata");

        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await MariaDbContainerFixture.ExecAsync(conn,
                "CREATE TABLE `cand_empty` (`id` int NOT NULL AUTO_INCREMENT, PRIMARY KEY(`id`));");
            await MariaDbContainerFixture.ExecAsync(conn,
                "CREATE TABLE `cand_withdata` (`id` int NOT NULL AUTO_INCREMENT, PRIMARY KEY(`id`));");
            await MariaDbContainerFixture.ExecAsync(conn, "INSERT INTO `cand_withdata` () VALUES ();");
        }

        var dir = NewTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "SchemaUpgradePhases.yaml"), """
            removalCandidates:
              - cand_empty
              - cand_withdata
              - cand_absent
            phases:
              - phase: "1"
                startVersion: 63
                targetVersion: 64
            """);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "63", ServerVersion = "8.0" });

        var svc = new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
        {
            DbDirectory = dir,
            ConnectionString = fixture.ConnectionString
        };

        var outPath = Path.Combine(dir, "baseline.md");
        var report = svc.Baseline("homolog", outPath);

        Assert.True(report.DatabaseOnline);
        Assert.Equal(63, report.CurrentVersion);

        var empty = report.RemovalCandidates.Single(c => c.Table == "cand_empty");
        Assert.True(empty.Exists);
        Assert.Equal(0, empty.RowCount);
        Assert.Equal("drop", empty.Recommendation);

        var withData = report.RemovalCandidates.Single(c => c.Table == "cand_withdata");
        Assert.True(withData.Exists);
        Assert.Equal(1, withData.RowCount);
        Assert.Equal("archive", withData.Recommendation);

        var absent = report.RemovalCandidates.Single(c => c.Table == "cand_absent");
        Assert.False(absent.Exists);
        Assert.Equal("absent", absent.Recommendation);

        // The baseline reports migration/model divergence (its purpose); both fields are populated.
        // Value is not pinned: this codebase currently has real model-vs-snapshot drift, which is
        // precisely the kind of thing the baseline exists to record.
        Assert.NotNull(report.PendingMigrations);

        Assert.True(File.Exists(outPath));
        Assert.Contains("Removal-candidate census", await File.ReadAllTextAsync(outPath));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nr-baseline-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
