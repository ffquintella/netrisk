using System.IO;
using System.Linq;
using Model.Database;
using ServerServices.SchemaUpgrade;
using Xunit;

namespace ServerServices.Tests.SchemaUpgrade;

public class SchemaUpgradePlannerTests
{
    [Fact]
    public void VersionRange_IsStartPlusOneThroughTarget()
    {
        var phase = new SchemaUpgradePhase { Phase = "x", StartVersion = 63, TargetVersion = 65 };
        Assert.Equal(new[] { 64, 65 }, SchemaUpgradePlanner.VersionRange(phase).ToArray());
    }

    [Fact]
    public void VersionRange_SingleVersion()
    {
        var phase = new SchemaUpgradePhase { Phase = "x", StartVersion = 63, TargetVersion = 64 };
        Assert.Equal(new[] { 64 }, SchemaUpgradePlanner.VersionRange(phase).ToArray());
    }

    [Fact]
    public void MissingStructureFiles_ReportsOnlyAbsentFiles()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "64.sql"), "-- 64");
        // 65.sql intentionally absent.
        var phase = new SchemaUpgradePhase { Phase = "x", StartVersion = 63, TargetVersion = 65 };

        var missing = SchemaUpgradePlanner.MissingStructureFiles(dir, phase);

        Assert.Single(missing);
        Assert.EndsWith("65.sql", missing[0]);
    }

    [Fact]
    public void AssemblePhaseSql_ConcatenatesStructureThenDataPerVersion()
    {
        var structure = NewTempDir();
        var data = NewTempDir();
        File.WriteAllText(Path.Combine(structure, "64.sql"), "ALTER TABLE a;");
        File.WriteAllText(Path.Combine(data, "64.sql"), "UPDATE settings SET value='64';");
        File.WriteAllText(Path.Combine(structure, "65.sql"), "ALTER TABLE b;");
        // 65 data file intentionally absent (optional).
        var phase = new SchemaUpgradePhase { Phase = "x", StartVersion = 63, TargetVersion = 65 };

        var sql = SchemaUpgradePlanner.AssemblePhaseSql(structure, data, phase);

        Assert.Contains("db_version 64 — structure", sql);
        Assert.Contains("ALTER TABLE a;", sql);
        Assert.Contains("db_version 64 — data", sql);
        Assert.Contains("UPDATE settings SET value='64';", sql);
        Assert.Contains("db_version 65 — structure", sql);
        Assert.Contains("ALTER TABLE b;", sql);
        // Structure 64 appears before structure 65.
        Assert.True(sql.IndexOf("ALTER TABLE a;", System.StringComparison.Ordinal)
                    < sql.IndexOf("ALTER TABLE b;", System.StringComparison.Ordinal));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nr-planner-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
