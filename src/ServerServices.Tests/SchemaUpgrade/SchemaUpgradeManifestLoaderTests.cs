using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ServerServices.SchemaUpgrade;
using Xunit;

namespace ServerServices.Tests.SchemaUpgrade;

/// <summary>
/// Unit tests for the Track 6 schema-upgrade phase manifest loader. No DB; pure YAML parsing
/// and validation. See roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md (Testing Requirements).
/// </summary>
public class SchemaUpgradeManifestLoaderTests
{
    private const string ValidYaml = """
        phases:
          - phase: "1"
            description: "safe fixes"
            startVersion: 63
            targetVersion: 64
            census:
              - name: "zero-dates"
                sql: "SELECT 1;"
            validations:
              - type: "IndexExists"
                name: "idx-check"
                table: "biometric_transactions"
                target: "idx_biometric_transaction_id"
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

    [Fact]
    public void Load_ValidManifest_ParsesAllPhases()
    {
        var manifest = SchemaUpgradeManifestLoader.Load(ValidYaml);

        Assert.Equal(3, manifest.Phases.Count);
        Assert.Collection(manifest.Phases.ConvertAll(p => p.Phase),
            p => Assert.Equal("1", p),
            p => Assert.Equal("6a", p),
            p => Assert.Equal("6b", p));
    }

    [Fact]
    public void Load_ValidManifest_PopulatesPhaseDetails()
    {
        var manifest = SchemaUpgradeManifestLoader.Load(ValidYaml);

        var phase1 = manifest.GetPhase("1")!;
        Assert.Equal(63, phase1.StartVersion);
        Assert.Equal(64, phase1.TargetVersion);
        Assert.False(phase1.Destructive);
        Assert.Single(phase1.Census);
        Assert.Equal("zero-dates", phase1.Census[0].Name);
        Assert.Single(phase1.Validations);
        Assert.Equal("IndexExists", phase1.Validations[0].Type);
        Assert.Equal("idx_biometric_transaction_id", phase1.Validations[0].Target);
    }

    [Fact]
    public void Load_DestructivePhase_CarriesGateMetadata()
    {
        var manifest = SchemaUpgradeManifestLoader.Load(ValidYaml);

        var phase6b = manifest.GetPhase("6b")!;
        Assert.True(phase6b.Destructive);
        Assert.Equal("6a", phase6b.RequiresPhase);
        Assert.Equal(30, phase6b.ObservationDays);
    }

    [Fact]
    public void GetPhase_IsCaseInsensitive_AndNullForUnknown()
    {
        var manifest = SchemaUpgradeManifestLoader.Load(ValidYaml);

        Assert.NotNull(manifest.GetPhase("6A"));
        Assert.Null(manifest.GetPhase("99"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_EmptyInput_Throws(string yaml)
    {
        Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
    }

    [Fact]
    public void Load_NoPhases_Throws()
    {
        Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load("phases: []"));
    }

    [Fact]
    public void Load_DuplicatePhase_Throws()
    {
        var yaml = """
            phases:
              - phase: "1"
                startVersion: 63
                targetVersion: 64
              - phase: "1"
                startVersion: 64
                targetVersion: 65
            """;
        var ex = Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
        Assert.Contains("Duplicate phase", ex.Message);
    }

    [Fact]
    public void Load_TargetNotGreaterThanStart_Throws()
    {
        var yaml = """
            phases:
              - phase: "1"
                startVersion: 64
                targetVersion: 64
            """;
        Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
    }

    [Fact]
    public void Load_DestructiveWithoutRequiresPhase_Throws()
    {
        var yaml = """
            phases:
              - phase: "6b"
                startVersion: 69
                targetVersion: 70
                destructive: true
                observationDays: 30
            """;
        Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
    }

    [Fact]
    public void Load_DestructiveWithoutObservationDays_Throws()
    {
        var yaml = """
            phases:
              - phase: "6a"
                startVersion: 68
                targetVersion: 69
              - phase: "6b"
                startVersion: 69
                targetVersion: 70
                destructive: true
                requiresPhase: "6a"
            """;
        Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
    }

    [Fact]
    public void Load_RequiresPhaseNotDeclared_Throws()
    {
        var yaml = """
            phases:
              - phase: "6b"
                startVersion: 69
                targetVersion: 70
                destructive: true
                requiresPhase: "6a"
                observationDays: 30
            """;
        var ex = Assert.Throws<SchemaUpgradeManifestException>(() => SchemaUpgradeManifestLoader.Load(yaml));
        Assert.Contains("not declared", ex.Message);
    }

    /// <summary>
    /// Validates the manifest actually shipped at src/ConsoleClient/DB/SchemaUpgradePhases.yaml —
    /// not just the embedded fixtures — so a malformed real manifest fails the build.
    /// </summary>
    [Fact]
    public void Load_ShippedManifest_IsValidAndGatesPhase6b()
    {
        var manifest = SchemaUpgradeManifestLoader.LoadFromFile(GetShippedManifestPath());

        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6a", "6b" },
            manifest.Phases.Select(p => p.Phase).ToArray());

        var phase6b = manifest.GetPhase("6b")!;
        Assert.True(phase6b.Destructive);
        Assert.Equal("6a", phase6b.RequiresPhase);
        Assert.True(phase6b.ObservationDays > 0);

        // Versions are contiguous across the phase chain.
        for (var i = 1; i < manifest.Phases.Count; i++)
            Assert.Equal(manifest.Phases[i - 1].TargetVersion, manifest.Phases[i].StartVersion);
    }

    [Fact]
    public void Load_ShippedManifest_DeclaresRemovalCandidates()
    {
        var manifest = SchemaUpgradeManifestLoader.LoadFromFile(GetShippedManifestPath());

        Assert.NotEmpty(manifest.RemovalCandidates);
        Assert.Contains("ContributingRisksImpact", manifest.RemovalCandidates);
        Assert.Contains("FailedLoginAttempt", manifest.RemovalCandidates);
        // RiskGrouping is intentionally excluded (still referenced in StatisticsService).
        Assert.DoesNotContain("RiskGrouping", manifest.RemovalCandidates);
    }

    private static string GetShippedManifestPath([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../src/ServerServices.Tests/SchemaUpgrade/SchemaUpgradeManifestLoaderTests.cs
        var srcDir = Directory.GetParent(thisFile)!.Parent!.Parent!.FullName; // -> .../src
        return Path.Combine(srcDir, "ConsoleClient", "DB", "SchemaUpgradePhases.yaml");
    }
}
