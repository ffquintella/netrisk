using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The object rows built from the register, the exposure sources, incidents and assessments (S39 §2–§8; T244, T245),
/// read through the caller's context: hosts, domains and host-less applications in EXP and GOV, target entities in AME
/// and CTL.A, and scenarios in REG, GOV and CTL.P — with the values of Unit X (S39 §11.1).
/// </summary>
[TestSubject(typeof(RiskIndexObjectCollector))]
public class RiskIndexObjectCollectorTest : RiskIndexSeedTestBase
{
    private static readonly RiskIndexParameters P = new();

    private async Task<RiskIndexObjectSet> Collect(RiskIndexCollectionOptions? options = null)
    {
        await using var db = OpenContext();
        return await RiskIndexObjectCollector.CollectAsync(db, P, RiskIndexMode.Interim, Now, options);
    }

    private static RiskIndexObjectRow Row(RiskIndexObjectSet set, RiskIndexObjectKind kind, object reference,
        RiskIndexCategory category) =>
        set.Rows.Single(r => r.Key == new IcrObjectKey(kind, reference.ToString()!) && r.Category == category);

    [Fact]
    public async Task TestHostExposure()
    {
        var set = await Collect();
        RiskIndexObjectRow Exp(int host) => Row(set, RiskIndexObjectKind.Host, host, RiskIndexCategory.Exp);

        Assert.Equal(0.90, Exp(Srv).R, 9);
        Assert.True(RiskIndexSourceBits.Has(Exp(Srv).SourceMask, RiskIndexSources.V1Device));
        Assert.Equal(0.641, Exp(Db).R, 3);
        Assert.Equal(0.85, Exp(Db).Weight, 9);
        Assert.Equal(0.10, Exp(Tst).R, 9);
        Assert.Equal(0.49, Exp(Accepted).R, 9);
        Assert.Equal(RiskIndexObjectState.Assessed, Exp(Db).State);
    }

    [Fact]
    public async Task TestVisionOneCvesStayOutOfExposureWhileTheDeviceScoreStandsButCountInTheSla()
    {
        var set = await Collect();

        Assert.DoesNotContain(Row(set, RiskIndexObjectKind.Host, Srv, RiskIndexCategory.Exp).Factors,
            f => f.Factor == RiskIndexFactors.ExpVulnerabilities);
        var gov = Row(set, RiskIndexObjectKind.Host, Srv, RiskIndexCategory.Gov);
        Assert.Equal(0.304, gov.R, 9);
        Assert.Equal(RiskIndexPopulation.Assets, gov.Population);
    }

    [Fact]
    public async Task TestRetiredAndScorecardPseudoHostsAreNotObjects()
    {
        var set = await Collect();

        Assert.DoesNotContain(set.Rows, r => r.Key.Kind == RiskIndexObjectKind.Host && r.Key.Ref == Retired.ToString());
        Assert.DoesNotContain(set.Rows, r => r.Key.Kind == RiskIndexObjectKind.Host && r.Key.Ref == Pseudo.ToString());
    }

    [Fact]
    public async Task TestStaleAndPresumedHosts()
    {
        var set = await Collect();

        var stale = Row(set, RiskIndexObjectKind.Host, Bkp, RiskIndexCategory.Exp);
        Assert.Equal(RiskIndexObjectState.Stale, stale.State);
        Assert.Equal(0.2125, stale.R, 9);
        Assert.Equal(0.5, stale.Freshness, 9);

        var presumed = Row(set, RiskIndexObjectKind.Host, WsAdm, RiskIndexCategory.Exp);
        Assert.Equal(RiskIndexObjectState.Presumed, presumed.State);
        Assert.Equal(0.35, presumed.R, 9);
        Assert.Equal(1.0, presumed.RMax, 9);
    }

    [Fact]
    public async Task TestAHostWithOpenFindingsAndNoSlaDateIsNotAssessedInGov()
    {
        var set = await Collect();

        var gov = Row(set, RiskIndexObjectKind.Host, Db, RiskIndexCategory.Gov);
        Assert.Equal(RiskIndexObjectState.NotAssessed, gov.State);
        Assert.Equal(IcrMembership.InventoryOnly, gov.Membership);
        Assert.Equal(RiskIndexObjectState.AssessedClean, Row(set, RiskIndexObjectKind.Host, Accepted, RiskIndexCategory.Gov).State);
    }

    [Fact]
    public async Task TestScorecardDomainReadsTheLatestOverallRating()
    {
        var set = await Collect();

        var domain = Row(set, RiskIndexObjectKind.ExternalDomain, 1, RiskIndexCategory.Exp);
        Assert.Equal(0.4335, domain.R, 9);
        Assert.Equal(RiskIndexAssetClass.ExternalDomain, domain.AssetClass);
        Assert.Equal([UnitX], domain.Attribution);
        Assert.DoesNotContain(set.Rows, r => r.Key.Kind == RiskIndexObjectKind.ExternalDomain && r.Category == RiskIndexCategory.Gov);
    }

    [Fact]
    public async Task TestHostlessFindingsScoreTheirApplicationOrAnUnassignedGroup()
    {
        var set = await Collect();

        var app = Row(set, RiskIndexObjectKind.Application, Portal, RiskIndexCategory.Exp);
        Assert.Equal(0.928, app.R, 3);
        Assert.Equal("entity", app.CriticalitySource);
        Assert.Contains(app.Signals, s => s.Flags.Contains("m_net"));

        var group = set.Rows.Single(r => r.Key.Kind == RiskIndexObjectKind.UnassignedGroup && r.Category == RiskIndexCategory.Exp);
        Assert.Equal("findings:sarif:77", group.Key.Ref);
        Assert.Empty(group.Attribution);
        Assert.Equal(0.14, group.R, 9);
    }

    [Fact]
    public async Task TestIncidentsDecayFromTheClosureAndTheIntakeIsActive()
    {
        var set = await Collect();

        Assert.True(set.IncidentIntakeActive);
        Assert.True(set.SourceEnabled[RiskIndexCategory.Ame]);
        Assert.Equal(0.2205, Row(set, RiskIndexObjectKind.TargetEntity, UnitX, RiskIndexCategory.Ame).R, 4);
        var portal = Row(set, RiskIndexObjectKind.TargetEntity, Portal, RiskIndexCategory.Ame);
        Assert.Equal(0.75, portal.R, 9);
        Assert.Equal(0.375, portal.ClearedR, 9);
        Assert.Equal(Now.AddDays(-20), set.IncidentClosureDates[1]);
    }

    [Fact]
    public async Task TestAFrozenClosureDateWinsOverTheLastUpdate()
    {
        var set = await Collect(new RiskIndexCollectionOptions { IncidentClosureDates = new Dictionary<int, System.DateTime> { [1] = Now.AddDays(-50) } });

        Assert.Equal(0.7 * 0.5 * System.Math.Pow(2, -50.0 / 30), Row(set, RiskIndexObjectKind.TargetEntity, UnitX, RiskIndexCategory.Ame).R, 9);
    }

    [Fact]
    public async Task TestACriticalityOutsideOneToFiveFallsBackToTheDefault()
    {
        var set = await Collect();

        var invalid = Row(set, RiskIndexObjectKind.TargetEntity, Invalid, RiskIndexCategory.Ame);
        Assert.Equal(3, invalid.Criticality);
        Assert.Equal("default", invalid.CriticalitySource);
        Assert.Equal(0.7, invalid.Weight, 9);
        Assert.Contains(Invalid, set.Map.InvalidCriticality);
    }

    [Fact]
    public async Task TestAssessmentsScoreTheirTargetAndAnUnassessedUnitIsPresumed()
    {
        var set = await Collect();

        Assert.Equal(0.196, Row(set, RiskIndexObjectKind.TargetEntity, UnitX, RiskIndexCategory.Ctl).R, 9);
        var presumed = Row(set, RiskIndexObjectKind.TargetEntity, Invalid, RiskIndexCategory.Ctl);
        Assert.Equal(RiskIndexObjectState.Presumed, presumed.State);
        Assert.Equal(0.28, presumed.R, 9);
    }

    [Fact]
    public async Task TestTheScenarioRows()
    {
        var set = await Collect();

        var reg = Row(set, RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg);
        Assert.InRange(reg.R, 0.8466, 0.8469);
        Assert.Equal(1.15, reg.Weight, 9);
        Assert.Equal(new[] { UnitX, Enrolment }, reg.Attribution.OrderBy(i => i));
        Assert.Equal(UnitX, reg.ScopeEntityId);
        Assert.True(reg.ScenarioInputs!.TreatmentAssessed);
        Assert.True(reg.ScenarioInputs.AboveCeiling);
        Assert.Equal(new System.DateTime(2026, 8, 21), reg.ScenarioInputs.ReferenceDate);

        var gov = Row(set, RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Gov);
        Assert.Equal(0.84, gov.R, 6); // residual_risk is a float column
        Assert.Contains(gov.Signals, s => s.Id == "ACC-EXP");

        Assert.Equal(0.504, Row(set, RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Ctl).R, 6);
        Assert.DoesNotContain(set.Rows, r => r.Key == new IcrObjectKey(RiskIndexObjectKind.Scenario, "2"));
    }

    [Fact]
    public async Task TestAReviewSavedWithAnAcceptanceIsNotQualified()
    {
        // The audit trail correlates the two inserts of one save (S39 §6.1): the review is the acceptance's.
        SeedUnscoped(ctx =>
        {
            ctx.RiskAcceptances.Add(new DAL.Entities.RiskAcceptance
            {
                Id = 2, Name = "renewal", RiskId = 1, StartDate = Now.AddDays(-1), CreatedAt = Now.AddDays(-1),
                ExpiresAt = Now.AddDays(-0.5), Status = RiskAcceptanceStatus.Expired, AuthorizingManagerId = 51, CreatedById = 51,
                RenewedFromId = 1
            });
            ctx.MgmtReviews.Add(Review(1, Now.AddDays(-1), 77));
        });

        var set = await Collect();

        Assert.Equal(new System.DateTime(2026, 8, 21), Row(set, RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg).ScenarioInputs!.ReferenceDate);
    }

    /// <summary>A low-severity scenario linked to DB-02's finding: fresh exposure 0.641 against σ_ref 0.30 diverges.</summary>
    private void SeedDivergentScenario(System.DateTime? reviewedAt = null)
    {
        SeedUnscoped(ctx =>
        {
            var risk = NewRisk(3, UnitX, "New");
            risk.Vulnerabilities.Add(ctx.Vulnerabilities.First(v => v.HostId == Db && v.Cves == "CVE-2026-0002"));
            ctx.Risks.Add(risk);
            ctx.RiskScorings.Add(new DAL.Entities.RiskScoring { Id = 3, CalculatedRisk = 4.8f, ResidualRisk = 3.0f, ClassicImpact = 3, ScoringMethod = 1 });
        });
        if (reviewedAt is { } at) SeedUnscoped(ctx => ctx.MgmtReviews.Add(Review(3, at, Reviewer)));
    }

    private static RiskIndexCollectionOptions Since(System.DateTime date) =>
        new() { DivergenceSince = new Dictionary<int, System.DateTime> { [3] = date } };

    [Fact]
    public async Task TestDivergenceLightsAndStartsItsRunToday()
    {
        SeedDivergentScenario(new System.DateTime(2026, 9, 1));

        var set = await Collect();

        var gov = Row(set, RiskIndexObjectKind.Scenario, 3, RiskIndexCategory.Gov);
        Assert.Contains(gov.Signals, s => s.Id == "DIV");
        Assert.Equal(Now.Date, gov.ScenarioInputs!.DivergenceSince);
    }

    [Fact]
    public async Task TestDivergenceStaysLitUntilAQualifiedReviewAndThenRestarts()
    {
        SeedDivergentScenario(new System.DateTime(2026, 9, 1));

        // Lit since 2026-09-15, reviewed before that: the latch holds and keeps its first day.
        var held = Row(await Collect(Since(new System.DateTime(2026, 9, 15))), RiskIndexObjectKind.Scenario, 3, RiskIndexCategory.Gov);
        Assert.Equal(new System.DateTime(2026, 9, 15), held.ScenarioInputs!.DivergenceSince);

        // A qualified review after the first day releases it; still diverging today, a new run starts today.
        SeedUnscoped(ctx => ctx.MgmtReviews.Add(Review(3, new System.DateTime(2026, 10, 1), Reviewer)));
        var restarted = Row(await Collect(Since(new System.DateTime(2026, 9, 15))), RiskIndexObjectKind.Scenario, 3, RiskIndexCategory.Gov);
        Assert.Equal(Now.Date, restarted.ScenarioInputs!.DivergenceSince);
    }

    private void SeedCampaign(int daysAgo) => SeedUnscoped(ctx =>
    {
        ctx.RiskReviewCampaigns.Add(new DAL.Entities.RiskReviewCampaign
        {
            Id = 1, EntityId = UnitX, Name = "Q3", PeriodStart = Now.AddDays(-daysAgo - 30), PeriodEnd = Now.AddDays(-daysAgo),
            DueDate = Now.AddDays(-daysAgo), Status = RiskReviewCampaignStatus.Completed, CreatedAt = Now.AddDays(-daysAgo - 30),
            CompletedAt = Now.AddDays(-daysAgo)
        });
        for (var i = 0; i < 6; i++)
            ctx.RiskReviewCampaignItems.Add(new DAL.Entities.RiskReviewCampaignItem
            {
                Id = i + 1, CampaignId = 1, RiskId = i == 0 ? 1 : 500 + i, Rank = i + 1, Decision = RiskReviewDecision.Accepted,
                CreatedAt = Now.AddDays(-daysAgo - 30)
            });
    });

    [Fact]
    public async Task TestTheLatestCompletedCampaignRankWeightsTheScenario()
    {
        SeedCampaign(30);

        var reg = Row(await Collect(), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg);

        Assert.Equal(1.30, reg.Weight, 9);
        Assert.Equal((1, 6), (reg.ScenarioInputs!.Rank, reg.ScenarioInputs.RankedItems));
    }

    [Fact]
    public async Task TestARankOlderThanTheProfilesValidityIsNeutral()
    {
        SeedCampaign(200);

        Assert.Equal(1.15, Row(await Collect(), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg).Weight, 9);

        await using var db = OpenContext();
        var longer = await RiskIndexObjectCollector.CollectAsync(db, P with { RankValidityCycles = 4 }, RiskIndexMode.Interim, Now);
        Assert.Equal(1.30, Row(longer, RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg).Weight, 9);
    }

    [Fact]
    public async Task TestTheCollectorReadsUnderTheCallersScope()
    {
        ScopeTo(Lab);

        var set = await Collect();

        Assert.Equal([LabHost.ToString()], set.Rows.Where(r => r.Key.Kind == RiskIndexObjectKind.Host).Select(r => r.Key.Ref).Distinct());
        Assert.DoesNotContain(set.Rows, r => r.Key.Kind == RiskIndexObjectKind.Scenario);
    }

    [Fact]
    public async Task TestADisabledSourceContributesNoRows()
    {
        await using var db = OpenContext();
        var p = P with { EnabledSources = P.EnabledSources.Where(s => s is not (RiskIndexSources.V1Device or RiskIndexSources.SscTotal)).ToList() };

        var set = await RiskIndexObjectCollector.CollectAsync(db, p, RiskIndexMode.Interim, Now);

        Assert.DoesNotContain(set.Rows, r => r.Key.Kind == RiskIndexObjectKind.ExternalDomain);
        Assert.DoesNotContain(set.Rows, r => r.Category == RiskIndexCategory.Exp &&
                                             RiskIndexSourceBits.Has(r.SourceMask, RiskIndexSources.V1Device));
    }
}
