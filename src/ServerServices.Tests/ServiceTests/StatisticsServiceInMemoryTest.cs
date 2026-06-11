using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(StatisticsService))]
public class StatisticsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IStatisticsService _svc;
    public StatisticsServiceInMemoryTest() => _svc = GetService<IStatisticsService>();

    private static Vulnerability NewVuln(int id, string severity = "3", string source = "nessus",
        double score = 5, ushort status = 1) => new()
    {
        Id = id, Title = "V", Status = status, Severity = severity, ImportSource = source, Score = score,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 2), DetectionCount = 1
    };

    private static Risk NewRisk(int id, string status = "Open", int category = 1) => new()
    {
        Id = id, Status = status, Subject = $"R{id}", ReferenceId = "R", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", Category = category,
        SubmissionDate = DateTime.Now.AddDays(-1), LastUpdate = DateTime.Now.AddDays(-1)
    };

    [Fact]
    public void TestVulnerabilityDistributionAndSources()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, severity: "3", source: "nessus"));
            ctx.Vulnerabilities.Add(NewVuln(2, severity: "4", source: "openvas"));
            ctx.Vulnerabilities.Add(NewVuln(3, severity: "3", source: "nessus"));
        });

        Assert.Equal(2, _svc.GetVulnerabilitiesDistribution().Count); // severities 3 and 4
        var sources = _svc.GetVulnerabilitySources();
        Assert.Equal(2, sources.Count);
        Assert.Equal(2, sources.First(s => s.Name == "nessus").Value);
    }

    [Fact]
    public void TestVulnerabilityNumbers()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, severity: "4"));
            ctx.Vulnerabilities.Add(NewVuln(2, severity: "3"));
            ctx.Vulnerabilities.Add(NewVuln(3, severity: "2"));
        });

        var n = _svc.GetVulnerabilityNumbers();

        Assert.Equal(3, n.Total);
        Assert.Equal(1, n.Critical);
        Assert.Equal(1, n.High);
        Assert.Equal(1, n.Medium);
    }

    [Fact]
    public void TestVulnerabilitiesVerifiedPercentage()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, score: 5, status: (ushort)Model.IntStatus.Active));
            ctx.Vulnerabilities.Add(NewVuln(2, score: 5, status: (ushort)Model.IntStatus.New));
        });

        Assert.Equal(50f, _svc.GetVulnerabilitiesVerifiedPercentage());
    }

    [Fact]
    public void TestVulnerabilitiesVerifiedPercentageEmpty()
    {
        Assert.Equal(0, _svc.GetVulnerabilitiesVerifiedPercentage());
    }

    [Fact]
    public async Task TestVulnerabilitiesSeverityByImport()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, severity: "3"));
            ctx.Vulnerabilities.Add(NewVuln(2, severity: "4"));
        });

        var list = await _svc.GetVulnerabilitiesServerityByImportAsync();

        Assert.NotEmpty(list);
    }

    [Fact]
    public void TestVulnerabilitiesNumbersByStatus()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1, severity: "4", status: 1));
            ctx.Vulnerabilities.Add(NewVuln(2, severity: "3", status: 2));
        });

        var result = _svc.GetVulnerabilitiesNumbersByStatus();

        Assert.Equal(2, result.NumbersByStatus.Count);
    }

    [Fact]
    public async Task TestVulnerabilitiesNumbersByTime()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, severity: "3")));

        var result = await _svc.GetVulnerabilitiesNumbersByTimeAsync(5);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Open);
    }

    [Fact]
    public async Task TestRisksNumbers()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open"));
            ctx.Risks.Add(NewRisk(2, status: "Closed"));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 8f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 3f });
        });

        var result = await _svc.GetRisksNumbersAsync();

        Assert.Equal(2, result.General.Total);
        Assert.Equal(1, result.General.High);
        Assert.Equal(1, result.General.Low);
    }

    [Fact]
    public async Task TestRisksTopAndGroups()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, category: 10));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 9f });
            ctx.RiskGroupings.Add(new RiskGrouping { Value = 10, Name = "GroupA" });
        });

        var top = await _svc.GetRisksTopAsync();
        Assert.Single(top);
        Assert.Equal("GroupA", top[0].Group);

        var groups = await _svc.GetRisksTopGroupsAsync();
        Assert.Single(groups);
    }

    [Fact]
    public void TestRisksImpactVsProbability()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 2 });
        });

        var points = _svc.GetRisksImpactVsProbability(0, 10);

        Assert.Single(points);
    }

    [Fact]
    public void TestRisksOverTime()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open"));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 5f });
        });

        var result = _svc.GetRisksOverTime(10);

        Assert.NotEmpty(result);
    }

    [Fact]
    public void TestRisksVsCostsEmpty()
    {
        // No mitigations seeded → empty result; exercises the query/Include path.
        Assert.Empty(_svc.GetRisksVsCosts(0, 10));
    }

    [Fact]
    public void TestEntitiesRiskValues()
    {
        Seed(ctx =>
        {
            var entity = new Entity { Id = 1, DefinitionName = "person", DefinitionVersion = "1", Status = "active",
                Created = new DateTime(2026, 1, 1), Updated = new DateTime(2026, 1, 1) };
            entity.EntitiesProperties.Add(new EntitiesProperty { Id = 1, Entity = 1, Type = "name", Value = "Alice", OldValue = "", Name = "name" });
            ctx.Entities.Add(entity);
        });

        var values = _svc.GetEntitiesRiskValues();

        Assert.Single(values);
        Assert.Equal("Alice", values[0].Name);
    }
}
