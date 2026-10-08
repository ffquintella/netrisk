using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// Stage 9.11 (S52 §8 J1, D5, D17; T210) — an expired retention signals and never deletes, and no job is written against
/// the LGPD catalogue: no source of the job host names a catalogue entry, purpose, location, RIPD or legal requirement, and
/// no job is handed the catalogue service. Deleting a data subject's data is the controller's decision, in the system that
/// holds it — not the side effect of a job.
///
/// One job does reach the catalogue indirectly: the nightly flag reconciliation (<c>RiskFlagsDerivationJob</c>) reads it
/// for flag 5 through <c>RiskFlagsService</c>. That path is pinned by D9 of <c>RiskFlagsServiceInMemoryTest</c> — it leaves
/// the catalogue's tables and their trail unchanged —, because a grep of this project cannot see it.
///
/// A future read-only notice of expired retention is possible, but it needs an amendment of S52 first; this test fails on
/// purpose when a job starts naming the catalogue, so that amendment cannot be skipped.
/// </summary>
[TestSubject(typeof(JobsManager))]
public class DataCatalogueRetentionTest
{
    private static readonly string[] CatalogueNames =
    [
        "DataCatalogue", "DataCatalogueEntries", "DataCataloguePurposes", "DataCatalogueLocations", "Dpias", "DpiaLinks",
        "LegalRequirements", "RiskLegalRequirements", "IDataCatalogueService", "data_catalogue", "legal_requirements", "dpia"
    ];

    /// <summary>J1a — no source file of the job host names the catalogue.</summary>
    [Fact]
    public void TestJ1a_NoJobSourceNamesTheCatalogue()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BackgroundJobs"));
        Assert.True(Directory.Exists(root), root);

        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);

        var touching = sources
            .SelectMany(file => CatalogueNames
                .Where(name => File.ReadAllText(file).Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(name => $"{Path.GetFileName(file)}: {name}"))
            .ToList();

        Assert.True(touching.Count == 0,
            "A job touches the LGPD catalogue — expired retention signals and never deletes (S52 D5, D17):\n  " +
            string.Join("\n  ", touching));
    }

    /// <summary>J1b — no type of the job host takes the catalogue service, by constructor, field or property.</summary>
    [Fact]
    public void TestJ1b_NoJobIsHandedTheCatalogueService()
    {
        var types = typeof(JobsManager).Assembly.GetTypes();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var holders = types.Where(t =>
                t.GetConstructors(all).Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IDataCatalogueService))) ||
                t.GetFields(all).Any(f => f.FieldType == typeof(IDataCatalogueService)) ||
                t.GetProperties(all).Any(p => p.PropertyType == typeof(IDataCatalogueService)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(holders);
    }
}
