using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Risks.Chain;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.1 (S41 §8, L1–L8, T148) — the legacy single entity link keeps working while the chain
/// coexists with it, and the chain stays a complete mirror of it.
///
/// The legacy path is <c>RisksService</c>'s three association methods, which back
/// <c>PUT/DELETE /Risks/{id}/Entity</c>; the <c>PUT</c> is a clean followed by an associate, two calls
/// and two saves, exactly as the controller makes them.
/// </summary>
[TestSubject(typeof(RiskChainPersistence))]
public class RiskChainLegacyCoexistenceTest : RiskChainTestBase
{
    /// <summary>What <c>PUT /Risks/{id}/Entity</c> does.</summary>
    private void PutEntity(int riskId, int entityId)
    {
        Risks.CleanRiskEntityAssociations(riskId);
        Risks.AssociateRiskWithEntity(riskId, entityId);
    }

    /// <summary>
    /// L1 — a legacy risk that only has <c>risks.entity_id</c> is untouched: visible in its unit,
    /// appetite resolved from that unit, and a projection with the unit as scope and nothing linked.
    /// </summary>
    [Fact]
    public async Task TestL1_ARiskScopedOnlyByEntityIdBehavesAsBefore()
    {
        AddRisk(1, UnitA);
        SeedUnscoped(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = 1, EntityId = UnitA, MaxAcceptableResidual = 9, DualApprovalThreshold = 6,
            CreatedAt = DateTime.UtcNow
        }));

        ScopeTo(UnitA);

        Assert.Equal(1, Risks.GetRisk(1).Id);

        var appetite = await GetService<IRiskWorkflowService>().EvaluateAppetiteAsync(1);
        Assert.True(appetite.AppetiteConfigured);
        Assert.Equal(UnitA, appetite.EntityId);

        var chain = await Chain.GetRiskChainAsync(1, RiskManager());
        Assert.Equal(UnitA, chain.ScopeEntityId);
        Assert.Equal("Unit A", chain.ScopeEntityName);
        Assert.Equal(5, chain.Levels.Count);
        Assert.Equal(5, chain.MissingLevels.Count);
        Assert.Empty(LinksOf(1));
    }

    /// <summary>L2 — associating a process writes the legacy row and its Legacy mirror, and the legacy
    /// read still returns the process.</summary>
    [Fact]
    public void TestL2_AssociatingAProcessWritesTheLegacyRowAndItsMirror()
    {
        AddRisk(1, UnitA);

        Risks.AssociateRiskWithEntity(1, Process);

        Assert.Equal([Process], LegacyRowsOf(1));

        var link = Assert.Single(LinksOf(1));
        Assert.Equal(Process, link.EntityId);
        Assert.Equal(RiskChainLevel.Process, link.ChainLevel);
        Assert.Equal(RiskChainLinkOrigin.Legacy, link.Origin);
        Assert.Null(link.CreatedById);

        Assert.Equal(Process, Risks.GetRiskEntityByRiskId(1).Id);

        // And risks.entity_id, the scope column, is not touched by either path.
        Assert.Equal(UnitA, Read(ctx => ctx.Risks.Single(r => r.Id == 1).EntityId));
    }

    /// <summary>L3 — a unit is scope, not identification: associating one creates no chain link.</summary>
    [Fact]
    public void TestL3_AssociatingAUnitCreatesNoLink()
    {
        AddRisk(1, UnitA);

        Risks.AssociateRiskWithEntity(1, UnitB);

        Assert.Equal([UnitB], LegacyRowsOf(1));
        Assert.Empty(LinksOf(1));
    }

    [Fact]
    public void TestL2_AnAssociationAlreadyDeclaredOnTheChainIsNotMirroredTwice()
    {
        AddRisk(1, UnitA);
        AddLink(1, Process);

        Risks.AssociateRiskWithEntity(1, Process);

        var link = Assert.Single(LinksOf(1));
        Assert.Equal(RiskChainLinkOrigin.Declared, link.Origin);
        AssertCoexistenceInvariants(1);
    }

    /// <summary>L4 — clearing the legacy field removes the Legacy links and keeps the Declared ones.</summary>
    [Fact]
    public void TestL4_CleaningRemovesLegacyLinksAndKeepsDeclaredOnes()
    {
        AddRisk(1, UnitA);
        Risks.AssociateRiskWithEntity(1, Process);
        Risks.AssociateRiskWithEntity(1, PortalApp);
        AddLink(1, Data);

        Risks.CleanRiskEntityAssociations(1);

        Assert.Empty(LegacyRowsOf(1));
        var remaining = Assert.Single(LinksOf(1));
        Assert.Equal(Data, remaining.EntityId);
        Assert.Equal(RiskChainLinkOrigin.Declared, remaining.Origin);
    }

    /// <summary>L5 — removing one legacy association removes only its Legacy mirror.</summary>
    [Fact]
    public async Task TestL5_DeletingAnAssociationRemovesOnlyALegacyMirror()
    {
        AddRisk(1, UnitA);
        Risks.AssociateRiskWithEntity(1, Process);
        Risks.AssociateRiskWithEntity(1, Service);

        // Service is promoted: declared on the chain while still on the legacy field.
        await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Service }, Author, RiskManager());

        Risks.DeleteEntityAssociation(1, Process);
        Risks.DeleteEntityAssociation(1, Service);

        Assert.Empty(LegacyRowsOf(1));
        var remaining = Assert.Single(LinksOf(1));
        Assert.Equal(Service, remaining.EntityId);
        Assert.Equal(RiskChainLinkOrigin.Declared, remaining.Origin);
    }

    /// <summary>L6 — swapping P1 for P2 through the legacy PUT leaves P2 as Legacy, plus whatever is
    /// declared.</summary>
    [Fact]
    public void TestL6_SwappingTheProcessThroughThePutLeavesOnlyTheNewOneAsLegacy()
    {
        AddRisk(1, UnitA);
        AddLink(1, Objective);
        PutEntity(1, Process);

        PutEntity(1, Process2);

        Assert.Equal([Process2], LegacyRowsOf(1));

        var links = LinksOf(1);
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.EntityId == Process2 && l.Origin == RiskChainLinkOrigin.Legacy);
        Assert.Contains(links, l => l.EntityId == Objective && l.Origin == RiskChainLinkOrigin.Declared);
        Assert.DoesNotContain(links, l => l.EntityId == Process);
    }

    /// <summary>
    /// L7 — both coexistence invariants hold after every step of a mixed sequence: associate, promote,
    /// delete the promoted link (a demotion), clean, swap.
    /// </summary>
    [Fact]
    public async Task TestL7_TheInvariantsHoldAfterEveryOperation()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);

        // A mixed starting point: a pre-9.1 legacy row with no mirror is the one shape the copy in
        // Data/88.sql exists for, so the sequence starts after that copy has run.
        AddLegacyRow(2, Data);
        AddLink(2, Data, origin: RiskChainLinkOrigin.Legacy);
        AddLink(1, Objective);
        AssertCoexistenceInvariants(1);
        AssertCoexistenceInvariants(2);

        Risks.AssociateRiskWithEntity(1, Process);
        AssertCoexistenceInvariants(1);

        Risks.AssociateRiskWithEntity(1, UnitA);
        AssertCoexistenceInvariants(1);

        var promoted = (await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author,
            RiskManager())).Link;
        AssertCoexistenceInvariants(1);

        var demoted = await Chain.DeleteLinkAsync(1, promoted.Id, RiskManager());
        Assert.Equal(RiskChainLinkOrigin.Legacy, demoted.Demoted!.Origin);
        AssertCoexistenceInvariants(1);

        Risks.CleanRiskEntityAssociations(1);
        AssertCoexistenceInvariants(1);
        Assert.Equal([Objective], LinksOf(1).Select(l => l.EntityId));

        PutEntity(1, PortalApp);
        AssertCoexistenceInvariants(1);

        PutEntity(1, Service);
        AssertCoexistenceInvariants(1);
        Assert.Equal(new int?[] { Objective, Service }, LinksOf(1).Select(l => l.EntityId).OrderBy(i => i).ToArray());

        // Nothing done to risk 1 touched risk 2.
        AssertCoexistenceInvariants(2);
        Assert.Single(LinksOf(2));
    }

    /// <summary>
    /// L8 — the two statistics that read <c>risk_to_entity</c> also count a risk linked only by a
    /// Declared chain link, and count a risk linked both ways once.
    /// </summary>
    [Fact]
    public async Task TestL8_TheEntityStatisticsReadTheUnionOnce()
    {
        AddRisk(1, UnitA, score: 3f);  // chain only
        AddRisk(2, UnitA, score: 4f);  // legacy and chain
        AddRisk(3, UnitA, score: 10f); // nothing to do with the process

        AddLink(1, Process);
        Risks.AssociateRiskWithEntity(2, Process);
        await Chain.AddLinkAsync(2, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager());

        var statistics = GetService<IStatisticsService>();

        var top = Assert.Single(await statistics.GetRisksTopEntities(10, "businessProcess"));
        Assert.Equal(Process, top.EntityId);
        Assert.Equal("Enrolment", top.EntityName);
        Assert.Equal(7f, top.TotalCalculatedRisk);

        var values = statistics.GetEntitiesRiskValues();
        Assert.Equal(7f, values.Single(v => v.Name == "Enrolment").Value);
    }

    [Fact]
    public async Task TestL8_UnderScopeAnotherTenantsRiskIsLeftOutOfTheSums()
    {
        AddRisk(1, UnitA, score: 3f);
        AddRisk(2, UnitB, score: 100f);
        AddRisk(3, UnitB, score: 1000f);

        AddLink(1, Process);
        AddLink(2, Process);
        Risks.AssociateRiskWithEntity(3, Process);

        ScopeTo(UnitA);
        var statistics = GetService<IStatisticsService>();

        var top = Assert.Single(await statistics.GetRisksTopEntities(10, "businessProcess"));
        Assert.Equal(3f, top.TotalCalculatedRisk);

        Assert.Equal(3f, statistics.GetEntitiesRiskValues().Single(v => v.Name == "Enrolment").Value);
    }

    /// <summary>
    /// Regression, found while making L8 pass: <c>GetEntitiesRiskValues</c> added an entity's own risks
    /// to its value and then added them again through <c>GetChildEntitiesRiskScore</c>, which sums the
    /// entity itself as well as its descendants — so every entity's own score was doubled in the
    /// Entities Risks report, and a parent outweighed a child by its own score twice over. Legacy links
    /// only, so this fails on the pre-9.1 code too.
    /// </summary>
    [Fact]
    public void TestEntityRiskValuesCountAnEntitysOwnRisksOnce()
    {
        SeedUnscoped(ctx =>
        {
            AddEntity(ctx, 300, "organizationUnit", "Parent unit");
            ctx.Entities.Add(new Entity
            {
                Id = 301, DefinitionName = "subOrganizationUnit", DefinitionVersion = "2.5", Status = "active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow, Parent = 300
            });
            ctx.EntitiesProperties.Add(new EntitiesProperty
                { Id = 9301, Entity = 301, Type = "name", Value = "Child unit", OldValue = "", Name = "name" });
        });

        AddRisk(1, UnitA, score: 3f);
        AddRisk(2, UnitA, score: 4f);
        AddLegacyRow(1, 300);
        AddLegacyRow(2, 301);

        var values = GetService<IStatisticsService>().GetEntitiesRiskValues();

        Assert.Equal(4f, values.Single(v => v.Name == "Child unit").Value);
        Assert.Equal(7f, values.Single(v => v.Name == "Parent unit").Value);
    }

    [Fact]
    public async Task TestL8_TheTypeFilterStillApplies()
    {
        AddRisk(1, UnitA, score: 3f);
        AddLink(1, Process);
        AddLink(1, Service);

        var statistics = GetService<IStatisticsService>();

        Assert.Equal([Service], (await statistics.GetRisksTopEntities(10, "itService")).Select(e => e.EntityId));
        Assert.Equal(2, (await statistics.GetRisksTopEntities()).Count);
    }
}
