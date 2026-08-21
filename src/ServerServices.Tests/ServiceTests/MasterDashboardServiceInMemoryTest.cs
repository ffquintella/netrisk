using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Cross-entity Master Dashboard aggregation (Track 2 milestone 2.3.3).
/// </summary>
public class MasterDashboardServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IMasterDashboardService _svc;

    public MasterDashboardServiceInMemoryTest() => _svc = GetService<IMasterDashboardService>();

    private static Entity NewEntity(int id, string name, string definition = "organizationUnit")
    {
        var entity = new Entity
        {
            Id = id,
            DefinitionName = definition,
            DefinitionVersion = "1",
            Status = "active",
            Created = DateTime.Now,
            Updated = DateTime.Now
        };

        entity.EntitiesProperties.Add(new EntitiesProperty
        {
            Id = id, Entity = id, Type = "name", Value = name, OldValue = "", Name = "name"
        });

        return entity;
    }

    private static Risk NewRisk(int id, int? entityId, RiskStatus status = RiskStatus.New) => new()
    {
        Id = id, Status = status.ToString(), StatusId = status, Subject = $"R{id}", ReferenceId = "R",
        Assessment = "", Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "",
        EntityId = entityId, SubmissionDate = DateTime.Now, LastUpdate = DateTime.Now
    };

    private static Vulnerability NewVuln(int id, int? entityId, string severity = "4", ushort status = 1) => new()
    {
        Id = id, Title = "V", Status = status, Severity = severity, EntityId = entityId,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 2), DetectionCount = 1
    };

    private static Incident NewIncident(int id, int? entityId, int status) => new()
    {
        Id = id, Name = $"I{id}", Description = "d", EntityId = entityId, Status = status
    };

    [Fact]
    public async Task EmptyDatabaseProducesEmptyTotals()
    {
        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        Assert.Empty(dashboard.Entities);
        Assert.Equal(0, dashboard.Totals.OpenRisks);
        Assert.Equal(0, dashboard.Totals.OpenVulnerabilities);
        Assert.Equal(0, dashboard.Totals.OpenIncidents);
    }

    [Fact]
    public async Task OrganisationalEntityWithNoWorkStillGetsACard()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1, "Clean unit")));

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        var card = Assert.Single(dashboard.Entities);
        Assert.Equal("Clean unit", card.EntityName);
        Assert.Equal(0, card.OpenRisks);
        Assert.Equal(0, card.PostureScore);
    }

    [Fact]
    public async Task CountsAreScopedToTheOwningEntity()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Alpha"));
            ctx.Entities.Add(NewEntity(2, "Beta"));

            ctx.Risks.Add(NewRisk(1, 1));
            ctx.Risks.Add(NewRisk(2, 1));
            ctx.Risks.Add(NewRisk(3, 2));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 9f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 5f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 3, CalculatedRisk = 2f });

            ctx.Vulnerabilities.Add(NewVuln(1, 1));
            ctx.Incidents.Add(NewIncident(1, 2, (int)IntStatus.New));
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        var alpha = dashboard.Entities.Single(e => e.EntityName == "Alpha");
        var beta = dashboard.Entities.Single(e => e.EntityName == "Beta");

        Assert.Equal(2, alpha.OpenRisks);
        Assert.Equal(1, alpha.RisksHigh);
        Assert.Equal(1, alpha.RisksMedium);
        Assert.Equal(1, alpha.OpenVulnerabilities);
        Assert.Equal(0, alpha.OpenIncidents);

        Assert.Equal(1, beta.OpenRisks);
        Assert.Equal(1, beta.RisksLow);
        Assert.Equal(1, beta.OpenIncidents);
    }

    [Fact]
    public async Task ClosedWorkIsExcluded()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Alpha"));

            ctx.Risks.Add(NewRisk(1, 1));
            ctx.Risks.Add(NewRisk(2, 1, RiskStatus.Closed));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 9f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 9f });

            ctx.Vulnerabilities.Add(NewVuln(1, 1, status: (ushort)IntStatus.New));
            ctx.Vulnerabilities.Add(NewVuln(2, 1, status: (ushort)IntStatus.Closed));
            ctx.Vulnerabilities.Add(NewVuln(3, 1, status: (ushort)IntStatus.Duplicated));

            ctx.Incidents.Add(NewIncident(1, 1, (int)IntStatus.New));
            ctx.Incidents.Add(NewIncident(2, 1, (int)IntStatus.Closed));
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);
        var alpha = dashboard.Entities.Single(e => e.EntityName == "Alpha");

        Assert.Equal(1, alpha.OpenRisks);
        Assert.Equal(1, alpha.OpenVulnerabilities);
        Assert.Equal(1, alpha.OpenIncidents);
    }

    [Fact]
    public async Task RecordsWithNoEntityLandInTheUnassignedBucket()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Alpha"));
            ctx.Risks.Add(NewRisk(1, null));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 6f });
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        var unassigned = dashboard.Entities.Single(e => e.EntityId == null);
        Assert.Equal(1, unassigned.OpenRisks);

        // Totals must reconcile with the per-module screens, so the bucket counts toward them.
        Assert.Equal(1, dashboard.Totals.OpenRisks);
    }

    [Fact]
    public async Task TotalsAreTheSumOfTheCards()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Alpha"));
            ctx.Entities.Add(NewEntity(2, "Beta"));

            ctx.Risks.Add(NewRisk(1, 1));
            ctx.Risks.Add(NewRisk(2, 2));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 8f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 4f });

            ctx.Vulnerabilities.Add(NewVuln(1, 1, "4"));
            ctx.Vulnerabilities.Add(NewVuln(2, 2, "3"));
            ctx.Incidents.Add(NewIncident(1, 1, (int)IntStatus.New));
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        Assert.Equal(dashboard.Entities.Sum(e => e.OpenRisks), dashboard.Totals.OpenRisks);
        Assert.Equal(dashboard.Entities.Sum(e => e.OpenVulnerabilities), dashboard.Totals.OpenVulnerabilities);
        Assert.Equal(dashboard.Entities.Sum(e => e.OpenIncidents), dashboard.Totals.OpenIncidents);
        Assert.Equal(1, dashboard.Totals.VulnerabilitiesCritical);
        Assert.Equal(1, dashboard.Totals.VulnerabilitiesHigh);
    }

    [Fact]
    public async Task OrganisationAverageIsWeightedByOpenRiskCount()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Alpha"));
            ctx.Entities.Add(NewEntity(2, "Beta"));

            // Alpha: three risks averaging 9. Beta: one risk of 1.
            ctx.Risks.Add(NewRisk(1, 1));
            ctx.Risks.Add(NewRisk(2, 1));
            ctx.Risks.Add(NewRisk(3, 1));
            ctx.Risks.Add(NewRisk(4, 2));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 9f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 9f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 3, CalculatedRisk = 9f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 4, CalculatedRisk = 1f });
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        // A plain mean of the two entity means would be 5; weighting by count gives 7.
        Assert.Equal(7.0, dashboard.Totals.AverageRiskScore, precision: 2);
    }

    [Fact]
    public async Task CardsAreOrderedWorstPostureFirst()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Quiet"));
            ctx.Entities.Add(NewEntity(2, "Busy"));

            ctx.Risks.Add(NewRisk(1, 1));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 2f });

            ctx.Risks.Add(NewRisk(2, 2));
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 9f });
            ctx.Incidents.Add(NewIncident(1, 2, (int)IntStatus.New));
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        Assert.Equal("Busy", dashboard.Entities.First().EntityName);
        Assert.True(dashboard.Entities.First().PostureScore > dashboard.Entities.Last().PostureScore);
    }

    [Fact]
    public async Task CachedCallIsFlaggedAndSkipsTheRecompute()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1, "Alpha")));

        var first = await _svc.GetMasterDashboardAsync(useCache: false);
        Assert.False(first.FromCache);

        // A row added after the first pass must not appear until the cache is bypassed.
        Seed(ctx => ctx.Entities.Add(NewEntity(2, "Beta")));

        var cached = await _svc.GetMasterDashboardAsync();
        Assert.True(cached.FromCache);
        Assert.Single(cached.Entities);

        var refreshed = await _svc.GetMasterDashboardAsync(useCache: false);
        Assert.False(refreshed.FromCache);
        Assert.Equal(2, refreshed.Entities.Count);
    }

    [Fact]
    public async Task MutatingTheResultDoesNotPoisonTheCache()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1, "Alpha")));

        var first = await _svc.GetMasterDashboardAsync(useCache: false);
        first.Entities.Clear();
        first.Totals.OpenRisks = 999;

        var second = await _svc.GetMasterDashboardAsync();

        Assert.Single(second.Entities);
        Assert.Equal(0, second.Totals.OpenRisks);
    }

    [Fact]
    public async Task NonOrganisationalEntityAppearsOnlyWhenItCarriesWork()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "Just a person", "person"));
            ctx.Entities.Add(NewEntity(2, "Loaded app", "application"));

            ctx.Risks.Add(NewRisk(1, 2));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 5f });
        });

        var dashboard = await _svc.GetMasterDashboardAsync(useCache: false);

        Assert.DoesNotContain(dashboard.Entities, e => e.EntityName == "Just a person");
        Assert.Contains(dashboard.Entities, e => e.EntityName == "Loaded app");
    }
}
