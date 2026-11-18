using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using Model.TailRisk;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M59 through the read API (S40 §5.2.2, §5.3, §5.10, §5.11; T284–T288): a full-mode snapshot written by the real job and
/// read back — the PA-1 floor with its trigger beside the unfloored value, the Gate A and Gate B seals, M43's Top Risks
/// hosted for a node, the monetary panel with M45's portfolio, the trend's floor and reference lines, M40's confidence, and
/// the IT-service drill-down. The interim snapshot of the same data answers "not available" for each.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexFullModeReadTest : RiskIndexSeedTestBase
{
    private static readonly DateOnly D = DateOnly.FromDateTime(Now);
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);
    private const int Svc = 50, Phi = 95;

    private IRiskIndexReadService Service => GetService<IRiskIndexReadService>();

    private void Run(RiskIndexMode mode)
    {
        SeedUnscoped(ctx =>
        {
            ctx.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = 1, Version = 1, Name = "Full", Mode = mode, Status = RiskIndexProfileStatus.Active,
                // φ_A at its configurable top (S39 §12.2, 70–100), so the floor lifts whatever the seed's unfloored value.
                Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters { GateAFloor = Phi }), Justification = "full",
                SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
                EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
            });
            ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.Publication, Value = "published" });

            // Risk 1 (Unit X): flag 2 declared — Gate A —, a target, M40's confidence, and its tail above the tolerance.
            ctx.RiskFlags.Add(new RiskFlag { Id = 1, RiskId = 1, Flag = RiskFlagCode.LegalRegulatory, Declared = true, DeclaredReason = "LGPD", CreatedAt = Now.AddDays(-2) });
            ctx.RiskTargets.Add(new RiskTarget { Id = 1, RiskId = 1, TargetScore = 2.0m, Rationale = "plan", CreatedAt = Now.AddDays(-2) });
            ctx.Risks.Find(1)!.EvidenceConfidence = EvidenceConfidence.Confirmed;
            ctx.RiskScorings.Find(1)!.QuantAleMean = 120_000;
            ctx.RiskAppetiteTailLimits.Add(new RiskAppetiteTailLimit { Id = 1, AppetiteId = 1, MaxScenarioP95 = 100_000m, Rationale = "board", CreatedAt = Now.AddDays(-30) });
            ctx.RiskTailStatistics.Add(new RiskTailStatistics
            {
                Id = 1, RiskId = 1, Run = TailRun.Inherent, Iterations = 10_000, Seed = 1, ConfidenceLevel = 0.95m, ExpectedLoss = 120_000,
                P95 = 250_000, P95CiLow = 240_000, P95CiHigh = 260_000, Cvar95 = 400_000, ComputedAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1)
            });

            // An IT service serving the Enrolment process: the IT-service drill-down lists it.
            ctx.Entities.Add(new Entity
            {
                Id = Svc, DefinitionName = "itService", DefinitionVersion = "2.7", Status = "active", Created = Now.AddYears(-1),
                Updated = Now.AddYears(-1)
            });
            var id = 8000;
            AddEntityProperties(ctx, Svc, ref id, ("processes", Enrolment.ToString()), ("applications", Portal.ToString()));
        });
        var outcome = GetService<IRiskIndexSnapshotService>().RunAsync(Now).GetAwaiter().GetResult();
        Assert.Equal(RiskIndexSnapshotResult.Written, outcome.Result);
    }

    private static RiskIndexCaller Committee() =>
        new(1, false, new HashSet<string>(new[] { RiskIndexPermissions.View, RiskIndexPermissions.Approve }
            .Concat(RiskIndexModulePermissions.All)));

    private static RiskIndexCaller ViewerWithoutRegister() => new(2, false, new HashSet<string> { RiskIndexPermissions.View });

    private static string Key(int entity) => RiskIndexNodeKey.ForEntity(entity).ToString();

    [Fact]
    public async Task TestT286TheHeadlineIsFlooredAtGateAWithItsTriggerAndTheUnflooredValueBeside()
    {
        Run(RiskIndexMode.Full);

        var overview = await Service.GetOverviewAsync("org", null, Committee());
        var headline = overview.Headline!;

        Assert.Equal(RiskIndexMode.Full, overview.Mode);
        Assert.True(headline.Unfloored < Phi);
        Assert.Equal(Phi, headline.Displayed);
        Assert.Equal(Phi, headline.Floored);
        var reason = Assert.Single(headline.FloorReasons);
        Assert.Equal(("A", "Risk 1", "2"), (reason.GateCode, reason.Scenario.Label, reason.FlagKey));
        Assert.Contains(overview.Seals, s => s.Key == "gate_a_scenarios" && s.Count == 1 && s.ObjectFilter == "seal=gate_a");
        Assert.Contains(overview.Seals, s => s.Key == "gate_b_tail_exceeds" && s.Count == 1);
    }

    /// <summary>S40 §11.3: the trigger's label is the register's — a reader without <c>riskmanagement</c> sees it redacted.</summary>
    [Fact]
    public async Task TestT286TheFloorTriggerIsRedactedWithoutTheRegisterPermission()
    {
        Run(RiskIndexMode.Full);

        var reason = Assert.Single((await Service.GetOverviewAsync("org", null, ViewerWithoutRegister())).Headline!.FloorReasons);

        Assert.Equal("riskmanagement", reason.Scenario.RequiresPermission);
        Assert.Equal("Scenario #1", reason.Scenario.Label);
    }

    [Fact]
    public async Task TestT286TheInterimModeHasNoFloorNoTrackNineSealAndNoTopRisks()
    {
        Run(RiskIndexMode.Interim);

        var overview = await Service.GetOverviewAsync("org", null, Committee());
        var top = await Service.GetTopRisksAsync("org", null, 10, Committee());
        var trend = await Service.GetTrendAsync("org", 30, RiskIndexCategory.Icr, null, Committee());

        Assert.Empty(overview.Headline!.FloorReasons);
        Assert.DoesNotContain(overview.Seals, s => s.Key.StartsWith("gate_", StringComparison.Ordinal));
        Assert.Equal((false, RiskIndexErrors.RequiresFullMode), (top.Available, top.UnavailableReasonKey));
        Assert.Empty(trend.FlooredRanges);
        Assert.Null(trend.TargetLine);
    }

    /// <summary>M43's ranking (T172), restricted to the node's open scenarios: Risk 1 under Gate A heads Unit X's list.</summary>
    [Fact]
    public async Task TestT286TopRisksHostsM43sListForTheNode()
    {
        Run(RiskIndexMode.Full);

        var top = await Service.GetTopRisksAsync(Key(UnitX), null, 10, Committee());
        var redacted = await Service.GetTopRisksAsync(Key(UnitX), null, 10, ViewerWithoutRegister());

        Assert.True(top.Available);
        var first = top.Items[0];
        Assert.Equal((1, "Risk 1", true), (first.Rank, first.Scenario.Label, first.GateA));
        Assert.Equal([(int)RiskFlagCode.LegalRegulatory], first.GateAConditions);
        Assert.Equal(RiskIndexNavigationModule.Risk, first.Link!.Module);
        Assert.Equal(1, top.OpenScenarios);
        Assert.Null(redacted.Items[0].Link);
        Assert.Null(redacted.Items[0].Residual);
        Assert.Null(redacted.Items[0].EvidenceConfidence);
        Assert.True(redacted.Items[0].GateA);
        Assert.Equal("riskmanagement", redacted.Items[0].Scenario.RequiresPermission);
        await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetTopRisksAsync(Key(UnitX), null, 51, Committee()));
    }

    [Fact]
    public async Task TestT286AndT287TheTrendShowsTheFlooredStretchAndTheReferenceLines()
    {
        Run(RiskIndexMode.Full);

        var icr = await Service.GetTrendAsync("org", 30, RiskIndexCategory.Icr, null, Committee());
        var reg = await Service.GetTrendAsync("org", 30, RiskIndexCategory.Reg, null, Committee());

        var point = Assert.Single(icr.Points);
        Assert.True(point.IsFloored);
        Assert.Equal(Phi, point.DisplayedFloored);
        Assert.True(point.Displayed < Phi);
        Assert.Equal(new RiskIndexDateRange(D, D), Assert.Single(icr.FlooredRanges));
        Assert.Null(icr.AppetiteLine);
        Assert.Equal(50, reg.AppetiteLine);
        Assert.Equal(20, reg.TargetLine!.Value, 6);
    }

    /// <summary>S40 §5.11: M40's confidence beside the quality, never inside it, and the governance strip points to the seals.</summary>
    [Fact]
    public async Task TestT287TheEvidenceConfidenceAndTheGovernanceStatesAreShownInFullMode()
    {
        Run(RiskIndexMode.Full);

        var quality = await Service.GetQualityAsync("org", null, Committee());
        var governance = await Service.GetGovernanceIndicatorsAsync("org", null, Committee());

        Assert.Equal(RiskIndexQualityFilters.EvidenceConfidenceAvailable, quality.EvidenceConfidenceStateKey);
        Assert.Equal((1, 0, 0, 0), (quality.EvidenceConfidence!.Confirmed, quality.EvidenceConfidence.Indicative,
            quality.EvidenceConfidence.Hypothesis, quality.EvidenceConfidence.NotDeclared));
        Assert.Equal(("see_top_risks", "see_seals"), (governance.FlagsStateKey, governance.GateBStateKey));
    }

    /// <summary>
    /// S40 §5.10: Σ E[L] with "n of N" and REG's share, and M45's portfolio over the node's quantified scenarios — the
    /// aggregation is M45's own service, called with the node's visible open scenarios and its entity's appetite.
    /// </summary>
    [Fact]
    public async Task TestT287TheMonetaryPanelSumsTheNodeAndConsumesM45sPortfolio()
    {
        Run(RiskIndexMode.Full);
        var tail = Substitute.For<ITailRiskService>();
        tail.AggregatePortfolioAsync(Arg.Any<PortfolioTailRequest>()).Returns(new PortfolioTailDto
        {
            P95 = 250_000, Cvar95 = 400_000, ExpectedLoss = 120_000, Iterations = 100_000, Seed = 20260826,
            Dependence = PortfolioDependence.AssumedIndependent, Members = [new PortfolioMemberDto { RiskId = 1 }],
            Appetite = new TailAppetiteEvaluation { State = TailAppetiteState.ExceedsTolerance }
        });
        var service = new RiskIndexReadService(GetService<ServerServices.Services.IDalService>(), new RiskIndexReadCache(),
            new RiskIndexDerivedBudget(), GetService<Serilog.ILogger>(), GetService<IPermissionsService>(), null, tail);

        var monetary = await service.GetMonetaryAsync(Key(UnitX), null, Committee());

        Assert.Equal((120_000d, 1, 1, false), (monetary.Inherent.Sum!.Value, monetary.Inherent.Quantified,
            monetary.Inherent.Total, monetary.Inherent.Partial));
        Assert.Null(monetary.Residual.Sum);
        Assert.Equal((250_000d, RiskIndexGateBStates.Exceeds), (monetary.Portfolio!.P95, monetary.Portfolio.GateBStateKey));
        await tail.Received(1).AggregatePortfolioAsync(Arg.Is<PortfolioTailRequest>(r =>
            r.RiskIds!.SequenceEqual(new[] { 1 }) && r.EntityId == UnitX));
    }

    [Fact]
    public async Task TestT287TheInterimMonetaryPanelHasNoPortfolio()
    {
        Run(RiskIndexMode.Interim);

        var monetary = await Service.GetMonetaryAsync("org", null, Committee());

        Assert.Null(monetary.Portfolio);
        Assert.Equal(RiskIndexErrors.RequiresFullMode, monetary.PortfolioUnavailableKey);
        Assert.Equal(1, monetary.Inherent.Quantified);
    }

    /// <summary>T284: the IT-service dimension lists the services that serve a process of the parent's closure.</summary>
    [Fact]
    public async Task TestT284TheItServiceDimensionListsTheServicesOfTheParent()
    {
        Run(RiskIndexMode.Full);

        var children = await Service.GetNodesAsync(Key(UnitX), RiskIndexDimension.ItService, null, null, Committee());

        Assert.Equal(RiskIndexDimension.ItService, children.Dimension);
        Assert.Contains(children.Rows, r => r.Node.Key == Key(Svc));
    }

    /// <summary>
    /// M59, T288 (S39 §10.4, §13.1): the third line reads the profiles' previews, bridges and history through M47's assurance
    /// marker even in the shadow pilot; a plain viewer still does not, and nothing here opens a write (T198 refuses those).
    /// </summary>
    [Fact]
    public async Task TestT288TheThirdLineReadsWhatAProfileProducedDuringTheShadowPilot()
    {
        var profile = new RiskIndexProfile { Id = 9, Version = 9, Name = "x", Mode = RiskIndexMode.Full, Parameters = "{}", CreatedAt = Now };
        await using var db = OpenContext();

        var thirdLine = await ServerServices.RiskIndex.Profiles.RiskIndexShadowRule.RefusalAsync(db, profile, 5, false,
            p => p == Model.DecisionCycle.ThirdLineAssurance.PermissionKey || p == RiskIndexPermissions.View, default);
        var viewer = await ServerServices.RiskIndex.Profiles.RiskIndexShadowRule.RefusalAsync(db, profile, 6, false,
            p => p == RiskIndexPermissions.View, default);

        Assert.Null(thirdLine);
        Assert.Equal(RiskIndexErrors.InShadow, viewer!.Code);
    }

    // ---------------- the pure halves ----------------

    [Fact]
    public void TestFlooredRangesJoinConsecutiveFlooredDaysAndSkipGaps()
    {
        static RiskIndexTrendPointDto P(int day, bool floored, bool gap = false) => new(D.AddDays(day), 60, 60, floored ? 70 : 60,
            null, RiskIndexDisplayState.Point, null, null, [], gap, floored, null, []);

        var ranges = RiskIndexReadService.FlooredRanges([P(0, true), P(1, true), P(2, false), P(3, true, gap: true), P(4, true)]);

        Assert.Equal([new RiskIndexDateRange(D, D.AddDays(1)), new RiskIndexDateRange(D.AddDays(4), D.AddDays(4))], ranges);
    }

    [Fact]
    public void TestTheAppetiteLineIsTheEntitysCeilingElseTheGlobalOneTimesTen()
    {
        IReadOnlyList<(int? EntityId, double Ceiling)> appetites = [(null, 5.0), (UnitX, 3.5)];

        Assert.Equal(35, RiskIndexReadService.AppetiteLine(appetites, UnitX));
        Assert.Equal(50, RiskIndexReadService.AppetiteLine(appetites, Lab));
        Assert.Null(RiskIndexReadService.AppetiteLine([], UnitX));
    }

    /// <summary>S39 §3.2 REG-ALE: never a bare total — "n of N", REG's quantified share, and "partial" below the minimum.</summary>
    [Fact]
    public void TestALossSumSaysHowManyAreQuantifiedAndIsPartialBelowTheMinimum()
    {
        var weights = new Dictionary<string, double> { ["1"] = 1.0, ["2"] = 3.0 };
        var losses = new List<RiskIndexReadService.ScenarioLoss> { new(1, null, 100), new(2, null, null) };

        var sum = RiskIndexReadService.LossSum(losses, l => l.Inherent, weights, 2, new RiskIndexParameters());
        var none = RiskIndexReadService.LossSum(losses, l => l.Residual, weights, 2, new RiskIndexParameters());

        Assert.Equal((100d, 1, 2, 0.25, true), (sum.Sum!.Value, sum.Quantified, sum.Total, sum.RegisterWeightFraction, sum.Partial));
        Assert.Equal((null, 0, false), (none.Sum, none.Quantified, none.Partial));
    }

    /// <summary>S39 §7: the declared "no legitimate acceptance" condition has no flag number, so its reason names none.</summary>
    [Fact]
    public void TestAFloorReasonNamesTheNumberedFlagsOnly()
    {
        var values = new RiskIndexReadService.NodeValues(60, 70, 70, RiskIndexDisplayState.Point, 1, 1, 1, null, null, 1, 0, false,
            Now, [], [], [], [], null,
            [
                new StoredFloorReason("PA-1", new StoredObjectRef(RiskIndexObjectKind.Scenario, "1"), [3, 1]),
                new StoredFloorReason("PA-1", new StoredObjectRef(RiskIndexObjectKind.Scenario, "2"), [(int)RiskFlagCode.NoLegitimateAcceptance])
            ]);

        var reasons = RiskIndexReadService.FloorReasons(values, null);

        Assert.Equal(("1, 3", (string?)null), (reasons[0].FlagKey, reasons[1].FlagKey));
        Assert.All(reasons, r => Assert.Equal("A", r.GateCode));
    }

    [Fact]
    public void TestTheFullModeSealsCountGateAAndGateBAndTheInterimHasNone()
    {
        RiskIndexScenarioInputs[] scenarios =
        [
            new() { GateAConditions = [1], GateBTail = RiskIndexGateBStates.Exceeds, GateBIndicators = RiskIndexGateBStates.Within },
            new() { GateAConditions = [], GateBTail = RiskIndexGateBStates.NotAssessable, GateBIndicators = RiskIndexGateBStates.Exceeds }
        ];

        var seals = RiskIndexReadService.FullModeSeals(RiskIndexMode.Full, scenarios).ToDictionary(s => s.Key, s => s.Count);

        Assert.Equal(new Dictionary<string, int?>
        {
            ["gate_a_scenarios"] = 1, ["gate_b_tail_exceeds"] = 1, ["gate_b_kri_exceeds"] = 1, ["gate_b_not_assessable"] = 1
        }, seals);
        Assert.Empty(RiskIndexReadService.FullModeSeals(RiskIndexMode.Interim, scenarios));
    }
}
