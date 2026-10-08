using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// Stage 9.12 (S53 §8 J1, D1, D8; T215) — background work does not act on the AI model inventory: no source of the job host
/// names a model, a reading, an override or a model link, and no job is handed the inventory service. Readings and overrides
/// are recorded by a person through the API; a job that "evaluated" a model or recorded overrides would put a non-person
/// where MIGR-TI/IA Phase 6 and the override rate need one.
///
/// One job does reach the inventory indirectly: the nightly flag reconciliation (<c>RiskFlagsDerivationJob</c>) reads the
/// risk links for flag 11 through <c>RiskFlagsService</c>, writing as the <c>system</c> actor. That path is pinned by D11 and
/// D12 of <c>RiskFlagsServiceInMemoryTest</c> — it decides nothing and leaves the inventory unchanged —, because a grep of
/// this project cannot see it. A future job against the inventory needs an amendment of S53 first; this test fails on
/// purpose when a job starts naming it.
/// </summary>
[TestSubject(typeof(JobsManager))]
public class AiGovernanceJobsTest
{
    private static readonly string[] InventoryNames =
    [
        "AiModel", "AiModels", "AiModelMetricReading", "AiModelOverride", "AiModelRisk", "AiModelDataLink",
        "IAiGovernanceService", "ai_model", "ai_governance"
    ];

    /// <summary>J1a — no source file of the job host names the inventory.</summary>
    [Fact]
    public void TestJ1a_NoJobSourceNamesTheInventory()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BackgroundJobs"));
        Assert.True(Directory.Exists(root), root);

        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);

        var touching = sources
            .SelectMany(file => InventoryNames
                .Where(name => File.ReadAllText(file).Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(name => $"{Path.GetFileName(file)}: {name}"))
            .ToList();

        Assert.True(touching.Count == 0,
            "A job touches the AI model inventory — readings and overrides are a person's record (S53 D8):\n  " +
            string.Join("\n  ", touching));
    }

    /// <summary>J1b — no type of the job host takes the inventory service, by constructor, field or property.</summary>
    [Fact]
    public void TestJ1b_NoJobIsHandedTheInventoryService()
    {
        var types = typeof(JobsManager).Assembly.GetTypes();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var holders = types.Where(t =>
                t.GetConstructors(all).Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IAiGovernanceService))) ||
                t.GetFields(all).Any(f => f.FieldType == typeof(IAiGovernanceService)) ||
                t.GetProperties(all).Any(p => p.PropertyType == typeof(IAiGovernanceService)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(holders);
    }
}
