using System;
using System.IO;
using DAL.Entities;
using Model.Database;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.SchemaUpgrade;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.SchemaUpgrade;

/// <summary>
/// Unit tests for the schema-upgrade orchestration's read-only modes and gating logic. DB access is
/// mocked (<see cref="IDatabaseService"/>) or in-memory (<see cref="InMemoryDalService"/>); no real MySQL.
/// </summary>
public class SchemaUpgradeServiceTests
{
    private const string Manifest = """
        phases:
          - phase: "1"
            description: "safe fixes"
            startVersion: 63
            targetVersion: 64
          - phase: "6a"
            description: "deprecate"
            startVersion: 68
            targetVersion: 69
          - phase: "6b"
            description: "drop"
            startVersion: 69
            targetVersion: 70
            destructive: true
            requiresPhase: "6a"
            observationDays: 30
        """;

    private static (SchemaUpgradeService svc, IDatabaseService db, InMemoryDalService dal, string dir)
        Build(string dbVersion = "63", string status = "Online", string? dbName = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nr-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Structure"));
        Directory.CreateDirectory(Path.Combine(dir, "Data"));
        File.WriteAllText(Path.Combine(dir, "SchemaUpgradePhases.yaml"), Manifest);
        // Phase 1 covers db_version 64; provide its structure file by default.
        File.WriteAllText(Path.Combine(dir, "Structure", "64.sql"), "ALTER TABLE mgmt_reviews MODIFY next_review datetime NULL;");
        File.WriteAllText(Path.Combine(dir, "Data", "64.sql"), "UPDATE settings SET value='64' WHERE name='db_version';");

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = status, Version = dbVersion, ServerVersion = "8.0.36" });

        var dal = new InMemoryDalService(dbName ?? ("upgrade-" + Guid.NewGuid().ToString("N")));
        var svc = new SchemaUpgradeService(db, dal, Substitute.For<ILogger>()) { DbDirectory = dir };
        return (svc, db, dal, dir);
    }

    [Fact]
    public void Check_UnknownPhase_Fails()
    {
        var (svc, _, _, _) = Build();
        var report = svc.Check("99", "homolog");
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "phase" && !c.Passed);
    }

    [Fact]
    public void Check_DatabaseOffline_Fails()
    {
        var (svc, _, _, _) = Build(status: "Offline");
        var report = svc.Check("1", "homolog");
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "connectivity" && !c.Passed);
    }

    [Fact]
    public void Check_WrongStartVersion_Fails()
    {
        var (svc, _, _, _) = Build(dbVersion: "10");
        var report = svc.Check("1", "homolog");
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "start-version" && !c.Passed);
    }

    [Fact]
    public void Check_AtExpectedStartWithFilesPresent_Passes()
    {
        var (svc, _, _, _) = Build(dbVersion: "63");
        var report = svc.Check("1", "homolog");
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}")));
        Assert.Equal(63, report.CurrentVersion);
        Assert.Equal(64, report.TargetVersion);
    }

    [Fact]
    public void Check_MissingPhaseSqlFiles_Fails()
    {
        var (svc, _, _, dir) = Build(dbVersion: "63");
        File.Delete(Path.Combine(dir, "Structure", "64.sql"));
        var report = svc.Check("1", "homolog");
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "phase-sql-present" && !c.Passed);
    }

    [Fact]
    public void Check_DestructivePhase_ObservationWindowNotMet_Fails()
    {
        var (svc, _, dal, _) = Build(dbVersion: "69");
        SeedLog(dal, "6a", "Success", appliedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        svc.NowUtc = () => new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc); // 9 days < 30

        var report = svc.Check("6b", "prod");

        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "destructive-gate" && !c.Passed);
    }

    [Fact]
    public void Check_DestructivePhase_ObservationWindowMet_Passes()
    {
        var (svc, _, dal, dir) = Build(dbVersion: "69");
        // Provide the phase's structure file (db_version 70).
        File.WriteAllText(Path.Combine(dir, "Structure", "70.sql"), "DROP TABLE zz_deprecated_x;");
        SeedLog(dal, "6a", "Success", appliedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        svc.NowUtc = () => new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc); // ~150 days >= 30

        var report = svc.Check("6b", "prod");

        Assert.Contains(report.Checks, c => c.Name == "destructive-gate" && c.Passed);
        Assert.True(report.Success, string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));
    }

    [Fact]
    public void Check_DestructivePhase_NoPrerequisiteLogged_Fails()
    {
        var (svc, _, _, _) = Build(dbVersion: "69");
        var report = svc.Check("6b", "prod");
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "destructive-gate" && !c.Passed);
    }

    [Fact]
    public void DryRun_AssemblesSqlAndWritesOutput()
    {
        var (svc, _, _, dir) = Build();
        var outPath = Path.Combine(dir, "dryrun.sql");

        var report = svc.DryRun("1", "homolog", outPath);

        Assert.True(report.Success);
        Assert.Contains("ALTER TABLE mgmt_reviews", report.GeneratedSql);
        Assert.Equal(outPath, report.OutputPath);
        Assert.True(File.Exists(outPath));
        Assert.Contains("ALTER TABLE mgmt_reviews", File.ReadAllText(outPath));
    }

    [Fact]
    public void Apply_PreflightFails_Aborts()
    {
        var (svc, _, _, _) = Build(dbVersion: "10"); // wrong start version
        var report = svc.Apply("1", "homolog", yes: true);
        Assert.False(report.Success);
        Assert.Equal(SchemaUpgradeMode.Apply, report.Mode);
        Assert.Contains("aborted", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_PreflightPasses_IsGatedPendingHarness()
    {
        var (svc, _, _, _) = Build(dbVersion: "63");
        var report = svc.Apply("1", "homolog", yes: true);
        Assert.False(report.Success);
        Assert.Contains(report.Checks, c => c.Name == "apply-enabled" && !c.Passed);
    }

    private static void SeedLog(InMemoryDalService dal, string phase, string status, DateTime appliedAt)
    {
        using var ctx = dal.GetContext(false);
        ctx.SchemaUpgradeLogs.Add(new SchemaUpgradeLog
        {
            Phase = phase, Status = status, Environment = "prod", Operator = "test",
            StartVersion = 68, TargetVersion = 69, AppliedAt = appliedAt
        });
        ctx.SaveChanges();
    }
}
