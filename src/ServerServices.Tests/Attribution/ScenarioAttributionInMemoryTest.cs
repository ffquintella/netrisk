using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Attribution;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Attribution;

/// <summary>
/// Scenario attribution as the deduplicated union of <c>risks.entity_id</c> and
/// <c>risk_to_entity</c> (S39 §10.2, D-05, T240).
///
/// Before this, the two links answered "which unit owns this risk" differently: campaigns and appetite
/// read the scope column, the desktop writes the link table, and no query combined them. The SQL half
/// (the union really is a UNION on MariaDB, and the backfill) is in
/// <c>DAL.IntegrationTests/M52FoundationBackfillTests</c>.
/// </summary>
[TestSubject(typeof(ScenarioAttribution))]
public class ScenarioAttributionInMemoryTest : InMemoryServiceTestBase
{
    private const int UnitA = 501;
    private const int UnitB = 502;
    private const int Process = 503;

    private static readonly DateTime Created = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public ScenarioAttributionInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            var unitA = NewEntity(UnitA, "organizationUnit");
            var unitB = NewEntity(UnitB, "organizationUnit");
            var process = NewEntity(Process, "businessProcess");
            ctx.Entities.AddRange(unitA, unitB, process);

            // 1: scope column only. 2: link table only. 3: both, the same entity (the dedup case).
            // 4: scope A, linked to B and to a process. 5: no attribution at all.
            ctx.Risks.Add(NewRisk(1, UnitA));
            ctx.Risks.Add(NewRisk(2, null, unitB));
            ctx.Risks.Add(NewRisk(3, UnitA, unitA));
            ctx.Risks.Add(NewRisk(4, UnitA, unitB, process));
            ctx.Risks.Add(NewRisk(5, null));
        });
    }

    private static Entity NewEntity(int id, string definition) => new()
    {
        Id = id, DefinitionName = definition, DefinitionVersion = "2.5", Status = "active",
        Created = Created, Updated = Created, CreatedBy = 1, UpdatedBy = 1
    };

    private static Risk NewRisk(int id, int? scope, params Entity[] links)
    {
        var risk = new Risk
        {
            Id = id, EntityId = scope, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
            Assessment = string.Empty, Notes = string.Empty, RiskCatalogMapping = string.Empty,
            ThreatCatalogMapping = string.Empty, SubmissionDate = Created, LastUpdate = Created
        };
        foreach (var link in links) risk.Entities.Add(link);
        return risk;
    }

    [Fact]
    public async Task TestPairsAreTheUnionOfBothLinksWithEachPairOnce()
    {
        await using var db = OpenContext();

        var pairs = await ScenarioAttribution.Pairs(db).ToListAsync();

        var expected = new[]
        {
            (1, UnitA),
            (2, UnitB),
            (3, UnitA),
            (4, UnitA), (4, UnitB), (4, Process)
        };

        Assert.Equal(expected.OrderBy(p => p.Item1).ThenBy(p => p.Item2),
            pairs.Select(p => (p.RiskId, p.EntityId)).OrderBy(p => p.RiskId).ThenBy(p => p.EntityId));
    }

    [Fact]
    public async Task TestARiskLinkedOnlyThroughTheLinkTableIsAttributed()
    {
        await using var db = OpenContext();

        var inB = await ScenarioAttribution.RiskIdsAttributedTo(db, [UnitB]).OrderBy(id => id).ToListAsync();
        var inProcess = await ScenarioAttribution.RiskIdsAttributedTo(db, [Process]).ToListAsync();

        // Risk 2 has no scope column at all; before the union it belonged to no unit.
        Assert.Equal(new[] { 2, 4 }, inB);
        Assert.Equal(new[] { 4 }, inProcess);
    }

    [Fact]
    public async Task TestARiskReachedThroughSeveralEntitiesIsCountedOnce()
    {
        await using var db = OpenContext();

        // Risk 3 is in A twice (scope and link) and risk 4 is in A and B: across A ∪ B each risk once.
        var risks = await ScenarioAttribution.RiskIdsAttributedTo(db, [UnitA, UnitB, UnitA]).ToListAsync();

        Assert.Equal(new[] { 1, 2, 3, 4 }, risks.OrderBy(id => id));
    }

    [Fact]
    public async Task TestEntitiesByRiskGroupsDeduplicatesAndLeavesUnattributedRisksOut()
    {
        await using var db = OpenContext();

        var byRisk = await ScenarioAttribution.EntitiesByRiskAsync(db);

        Assert.Equal(new[] { UnitA }, byRisk[3]);
        Assert.Equal(new[] { UnitA, UnitB, Process }, byRisk[4]);
        Assert.False(byRisk.ContainsKey(5));

        var onlyFour = await ScenarioAttribution.EntitiesByRiskAsync(db, [4]);
        Assert.Equal(new[] { 4 }, onlyFour.Keys);
    }

    /// <summary>
    /// The attribution reads risks under the caller's entity scope, like every other risk query: a user
    /// scoped to B does not learn, through a link row, about a risk whose scope is A.
    /// </summary>
    [Fact]
    public async Task TestAScopedCallerOnlySeesTheAttributionsOfRisksInItsScope()
    {
        ScopeTo(UnitB);
        await using var db = OpenContext();

        var pairs = await ScenarioAttribution.Pairs(db).ToListAsync();

        Assert.Empty(pairs);
    }
}
