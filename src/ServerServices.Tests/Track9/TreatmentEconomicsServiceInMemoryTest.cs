using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using Model.TreatmentEconomics;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.6 (S47 §8) — <see cref="TreatmentEconomicsService"/>, the Phase 5 rules of <see cref="MitigationTasksService"/>
/// and the residual mean of <see cref="QuantitativeRiskService"/> against the real model, scope filters, audit
/// interceptor and Gate A on the EF in-memory provider: guards V1–V4, economics E1–E8, target TG1–TG4, portfolio
/// PF1–PF7, tasks AT1–AT7 and the Monte Carlo QM1.
///
/// The organisation is the Stage 9.1 one (<see cref="RiskChainTestBase"/>): units 100 and 200, user 7.
/// </summary>
[TestSubject(typeof(TreatmentEconomicsService))]
public class TreatmentEconomicsServiceInMemoryTest : RiskChainTestBase
{
    private static readonly DateTime January = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime February = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private ITreatmentEconomicsService Svc => GetService<ITreatmentEconomicsService>();

    private IMitigationTasksService Tasks => GetService<IMitigationTasksService>();

    public TreatmentEconomicsServiceInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.MitigationCosts.Add(new MitigationCost { Value = 3, Name = "Considerable" });
            ctx.PlanningStrategies.Add(new PlanningStrategy { Value = 3, Name = "Mitigate" });
        });
    }

    // --- seeding ---------------------------------------------------------------------------------

    private void AddMitigation(int id, int riskId, int percent = 50, DateTime? lastUpdate = null) =>
        SeedUnscoped(ctx => ctx.Mitigations.Add(new Mitigation
        {
            Id = id, RiskId = riskId, PlanningStrategy = 3, MitigationEffort = 1, MitigationCost = 3,
            MitigationOwner = Author, SubmittedBy = Author, MitigationPercent = percent,
            CurrentSolution = "", SecurityRequirements = "", SecurityRecommendations = "",
            SubmissionDate = January, LastUpdate = lastUpdate ?? January, PlanningDate = new DateOnly(2026, 6, 1)
        }));

    /// <summary>A FAIR-lite result as the Monte Carlo would have cached it on the risk.</summary>
    private void Quantify(int riskId, double mean, double? residualMean, double p50 = 0, double? residualP50 = 0) =>
        SeedUnscoped(ctx =>
        {
            var scoring = ctx.RiskScorings.Single(s => s.Id == riskId);
            scoring.ScoringMethod = QuantitativeRiskService.QuantitativeScoringMethod;
            scoring.QuantComputedAt = February;
            scoring.QuantAleMean = mean;
            scoring.QuantAleP50 = p50;
            scoring.QuantResidualAleMean = residualMean;
            scoring.QuantResidualAleP50 = residualP50;
        });

    private void Declare(int riskId, RiskFlagCode code) => SeedUnscoped(ctx => ctx.RiskFlags.Add(new RiskFlag
    {
        RiskId = riskId, Flag = code, Declared = true, DeclaredReason = "Declared by the assessor.",
        DeclaredAt = January, DeclaredById = Author, CreatedAt = January
    }));

    private void Appetite(double ceiling) => SeedUnscoped(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        { MaxAcceptableResidual = ceiling, DualApprovalThreshold = ceiling, CreatedAt = January }));

    private static TreatmentCostRequest Annual(decimal annual) =>
        new() { OneTime = 0, Annual = annual, SideEffectsAnnual = 0 };

    private static MitigationEconomicsRequest Reduce(decimal? annualCost = 15_000m, params int[] prerequisites) => new()
    {
        Option = TreatmentOption.Reduce,
        Cost = annualCost is null ? null : Annual(annualCost.Value),
        PrerequisiteMitigationIds = prerequisites.ToList()
    };

    private int EconomicsRows() => Read(ctx => ctx.MitigationEconomics.Count());

    private static MitigationEconomicsRequest With(MitigationEconomicsRequest request, Action<MitigationEconomicsRequest> change)
    {
        change(request);
        return request;
    }

    // --- V1–V4: guards ---------------------------------------------------------------------------

    /// <summary>V1 — every invalid request is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("no-option", "Option")]
    [InlineData("undefined-option", "Option")]
    [InlineData("transfer-without-counterparty", "TransferCounterparty")]
    [InlineData("long-counterparty", "TransferCounterparty")]
    [InlineData("negative-cost", "Cost.Annual")]
    [InlineData("missing-horizon", "Cost.HorizonYears")]
    [InlineData("long-basis", "CostBasis")]
    [InlineData("negative-effort", "EffortPersonDays")]
    [InlineData("long-duration", "DurationDays")]
    [InlineData("self", "PrerequisiteMitigationIds")]
    [InlineData("unknown-prerequisite", "PrerequisiteMitigationIds")]
    [InlineData("too-many-prerequisites", "PrerequisiteMitigationIds")]
    public async Task TestV1_AnInvalidRequestIsRefusedNamingTheField(string scenario, string field)
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);

        var request = scenario switch
        {
            "no-option" => With(Reduce(), r => r.Option = null),
            "undefined-option" => With(Reduce(), r => r.Option = (TreatmentOption)9),
            "transfer-without-counterparty" => With(Reduce(), r => r.Option = TreatmentOption.TransferShare),
            "long-counterparty" => With(Reduce(), r =>
            {
                r.Option = TreatmentOption.TransferShare;
                r.TransferCounterparty = new string('x', 256);
            }),
            "negative-cost" => With(Reduce(), r => r.Cost = Annual(-1)),
            "missing-horizon" => With(Reduce(),
                r => r.Cost = new TreatmentCostRequest { OneTime = 100, Annual = 0, SideEffectsAnnual = 0 }),
            "long-basis" => With(Reduce(), r => r.CostBasis = new string('x', 1001)),
            "negative-effort" => With(Reduce(), r => r.EffortPersonDays = -1),
            "long-duration" => With(Reduce(), r => r.DurationDays = 3651),
            "self" => Reduce(15_000m, 10),
            "unknown-prerequisite" => Reduce(15_000m, 999),
            _ => Reduce(15_000m, Enumerable.Range(1_000, 51).ToArray())
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveMitigationAsync(10, request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, EconomicsRows());
    }

    /// <summary>V2 — a missing mitigation, and one outside the caller's scope, are not found for reading and writing.</summary>
    [Fact]
    public async Task TestV2_AMissingOrOutOfScopeMitigationIsNotFound()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetMitigationAsync(999));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveMitigationAsync(999, Reduce(), Author));

        ScopeTo(UnitB);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetMitigationAsync(10));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveMitigationAsync(10, Reduce(), Author));
        Assert.Equal(0, EconomicsRows());
    }

    /// <summary>
    /// V3 — Gate A precedes everything: declaring "accept" on a risk carrying Gate A is refused exactly as accepting is,
    /// and nothing is written; another option on the same risk, and "accept" on a risk without Gate A, are recorded.
    /// </summary>
    [Fact]
    public async Task TestV3_AcceptIsRefusedWhileGateAHolds()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(20, 2);
        Declare(1, RiskFlagCode.HumanSafety);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.SaveMitigationAsync(10, new MitigationEconomicsRequest { Option = TreatmentOption.Accept }, Author));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Equal(0, EconomicsRows());

        var reduce = await Svc.SaveMitigationAsync(10, Reduce(), Author);
        Assert.Equal(TreatmentOption.Reduce, reduce.Option);
        Assert.True(reduce.GateC.GateAHolds);

        var accepted = await Svc.SaveMitigationAsync(20, new MitigationEconomicsRequest { Option = TreatmentOption.Accept },
            Author);
        Assert.Equal(GateCOutcome.NotApplicable, accepted.GateC.Outcome);
    }

    /// <summary>V4 — a prerequisite that already depends on the mitigation closes a cycle: refused, nothing written.</summary>
    [Fact]
    public async Task TestV4_ADependencyCycleIsRefused()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitA);
        AddMitigation(10, 1);
        AddMitigation(11, 2);
        AddMitigation(12, 3);

        await Svc.SaveMitigationAsync(10, Reduce(15_000m, 11), Author);
        await Svc.SaveMitigationAsync(11, Reduce(15_000m, 12), Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.SaveMitigationAsync(12, Reduce(15_000m, 10), Author));

        Assert.Equal(TreatmentEconomicsService.DependencyCycleRule, ex.RuleName);
        Assert.Equal(2, Read(ctx => ctx.MitigationDependencies.Count()));
        Assert.Null(Read(ctx => ctx.MitigationEconomics.SingleOrDefault(e => e.MitigationId == 12)));
    }

    // --- E1–E8: economics and Gate C -------------------------------------------------------------

    /// <summary>E1 — the declaration round-trips with the derived figures, and the ordinal scale beside it is untouched.</summary>
    [Fact]
    public async Task TestE1_TheEconomicsRoundTripBesideTheOrdinalScale()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(11, 2);

        await Svc.SaveMitigationAsync(10, new MitigationEconomicsRequest
        {
            Option = TreatmentOption.TransferShare,
            TransferCounterparty = "  Cyber insurer S.A.  ",
            Cost = new TreatmentCostRequest { OneTime = 30_000, Annual = 12_000, SideEffectsAnnual = 1_000, HorizonYears = 3 },
            CostBasis = "Quote 2026-09-14",
            EffortPersonDays = 4.5m,
            DurationDays = 30,
            PrerequisiteMitigationIds = [11]
        }, Author);

        var dto = await Svc.GetMitigationAsync(10);

        Assert.True(dto.Declared);
        Assert.Equal(TreatmentOption.TransferShare, dto.Option);
        Assert.Equal("Cyber insurer S.A.", dto.TransferCounterparty);
        Assert.Equal((23_000m, 42_000m, 3), (dto.Cost!.AnnualizedTotal, dto.Cost.FirstYear, dto.Cost.HorizonYears));
        Assert.Equal(("Quote 2026-09-14", 4.5m, 30), (dto.CostBasis, dto.EffortPersonDays, dto.DurationDays));
        Assert.Equal([11], dto.PrerequisiteMitigationIds);
        Assert.Equal((3, "Considerable", "Mitigate"), (dto.OrdinalCost, dto.OrdinalCostName, dto.PlanningStrategyName));
        Assert.Equal(Author, dto.UpdatedById);
        Assert.Equal(3, Read(ctx => ctx.Mitigations.Single(m => m.Id == 10).MitigationCost));

        // A replacement without a cost withdraws it — "not declared", not zero — and drops the prerequisite.
        var cleared = await Svc.SaveMitigationAsync(10, Reduce(annualCost: null), Author);
        Assert.Null(cleared.Cost);
        Assert.Empty(cleared.PrerequisiteMitigationIds);
        Assert.Contains(GateCNotAssessableReason.NoMonetaryCost, cleared.GateC.NotAssessableReasons);
    }

    /// <summary>
    /// E2 — Gate C reads the means, never the medians, in both directions: the means pass where the medians (benefit 0)
    /// would fail, and the means fail where the medians (benefit 100 000) would pass.
    /// </summary>
    [Fact]
    public async Task TestE2_GateCUsesTheMeansNotTheMedians()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(20, 2);

        Quantify(1, mean: 100_000, residualMean: 40_000, p50: 0, residualP50: 0);
        Quantify(2, mean: 100_000, residualMean: 95_000, p50: 100_000, residualP50: 0);

        var low = await Svc.SaveMitigationAsync(10, Reduce(50_000m), Author);
        Assert.Equal(GateCOutcome.Passes, low.GateC.Outcome);
        Assert.Equal((100_000d, 40_000d, 60_000d), (low.GateC.ExpectedLossBefore!.Value, low.GateC.ExpectedLossAfter!.Value,
            low.GateC.Benefit!.Value));

        var high = await Svc.SaveMitigationAsync(20, Reduce(50_000m), Author);
        Assert.Equal(GateCOutcome.Fails, high.GateC.Outcome);
        Assert.Equal(5_000d, high.GateC.Benefit!.Value);
    }

    /// <summary>
    /// E3 (T181) — on a quantified risk, a mitigation with no monetary cost — declared without one, or never declared at
    /// all — enters Gate C as not assessable, and the risk view shows it.
    /// </summary>
    [Fact]
    public async Task TestE3_NoMonetaryCostIsNotAssessableInTheRiskView()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1, lastUpdate: January);
        AddMitigation(11, 1, lastUpdate: January);
        Quantify(1, mean: 1_000_000, residualMean: 10_000);

        await Svc.SaveMitigationAsync(10, Reduce(annualCost: null), Author);

        var view = await Svc.GetRiskAsync(1);

        Assert.Equal(2, view.Mitigations.Count);
        foreach (var mitigation in view.Mitigations)
        {
            Assert.Equal(GateCOutcome.NotAssessable, mitigation.GateC.Outcome);
            Assert.Contains(GateCNotAssessableReason.NoMonetaryCost, mitigation.GateC.NotAssessableReasons);
            Assert.Null(mitigation.GateC.NetBenefit);
        }

        Assert.True(view.Mitigations.Single(m => m.MitigationId == 10).Declared);
        Assert.False(view.Mitigations.Single(m => m.MitigationId == 11).Declared);
    }

    /// <summary>E4 — an analysis with a residual median but no residual mean (computed before schema 95) is not assessable.</summary>
    [Fact]
    public async Task TestE4_AResidualMedianWithoutTheMeanIsNotAssessable()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);
        Quantify(1, mean: 100_000, residualMean: null, residualP50: 20_000);

        var dto = await Svc.SaveMitigationAsync(10, Reduce(10m), Author);

        Assert.Equal([GateCNotAssessableReason.ResidualMeanNotRecorded], dto.GateC.NotAssessableReasons);
        Assert.Null(dto.GateC.Benefit);
    }

    /// <summary>E5 — the residual run belongs to the risk's most recent mitigation; an older one's Gate C does not borrow it.</summary>
    [Fact]
    public async Task TestE5_TheResidualBelongsToTheMostRecentMitigation()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1, lastUpdate: January);
        AddMitigation(12, 1, lastUpdate: February);
        Quantify(1, mean: 100_000, residualMean: 40_000);

        var older = await Svc.SaveMitigationAsync(10, Reduce(1_000m), Author);
        var newer = await Svc.SaveMitigationAsync(12, Reduce(1_000m), Author);

        Assert.Equal([GateCNotAssessableReason.ResidualForAnotherMitigation], older.GateC.NotAssessableReasons);
        Assert.Equal(GateCOutcome.Passes, newer.GateC.Outcome);
    }

    /// <summary>
    /// E6 — the reason the economics are a table of their own: <c>PUT /Mitigations/{id}</c> (<c>MitigationsService.Save</c>)
    /// copies a DTO without these fields over the stored row, and must not erase them.
    /// </summary>
    [Fact]
    public async Task TestE6_SavingTheMitigationDoesNotEraseItsEconomics()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);
        await Svc.SaveMitigationAsync(10, Reduce(15_000m), Author);

        GetService<IMitigationsService>().Save(new Mitigation
        {
            Id = 10, RiskId = 1, PlanningStrategy = 3, MitigationEffort = 1, MitigationCost = 3, MitigationOwner = Author,
            SubmittedBy = Author, MitigationPercent = 80, CurrentSolution = "edited", SecurityRequirements = "",
            SecurityRecommendations = "", SubmissionDate = January, LastUpdate = February,
            PlanningDate = new DateOnly(2026, 6, 1)
        });

        var row = Read(ctx => ctx.MitigationEconomics.Single(e => e.MitigationId == 10));
        Assert.Equal((TreatmentOption.Reduce, 15_000m), (row.TreatmentOption, row.CostAnnual!.Value));
        Assert.Equal(80, Read(ctx => ctx.Mitigations.Single(m => m.Id == 10).MitigationPercent));
    }

    /// <summary>E7 — the declaration, its change and the dependency are in the governance audit trail, with the person.</summary>
    [Fact]
    public async Task TestE7_TheEconomicsAreAudited()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(11, 2);

        await Svc.SaveMitigationAsync(10, Reduce(15_000m, 11), Author);
        await Svc.SaveMitigationAsync(10, Reduce(20_000m, 11), Author);

        var economics = Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == nameof(MitigationEconomics)).ToList());
        Assert.Contains(economics, a => a.Action == AuditLogAction.Create && a.UserId == Author);
        Assert.Contains(economics, a => a.Action == AuditLogAction.Update && a.Field == nameof(MitigationEconomics.CostAnnual)
                                        && a.UserId == Author);
        Assert.Contains(Read(ctx => ctx.AuditLogs.ToList()),
            a => a.EntityType == nameof(MitigationDependency) && a.Action == AuditLogAction.Create);
    }

    /// <summary>E7b — the risk's own trail and the evidence pack carry the economics, the dependency and the target.</summary>
    [Fact]
    public async Task TestE7b_TheRiskTrailAndTheEvidencePackCarryThem()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(11, 2);
        await Svc.SaveMitigationAsync(10, Reduce(15_000m, 11), Author);
        await Svc.SaveTargetAsync(1, new RiskTargetRequest { TargetScore = 3m, Rationale = "MFA." }, Author);

        var trail = GetService<IAuditTrailService>();
        string[] expected = [nameof(MitigationEconomics), nameof(MitigationDependency), nameof(RiskTarget)];

        var own = (await trail.GetForRiskAsync(1)).Select(a => a.EntityType).ToHashSet();
        Assert.All(expected, type => Assert.Contains(type, own));

        var pack = (await trail.GetForEntityPeriodAsync(UnitA, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)))
            .Select(a => a.EntityType).ToHashSet();
        Assert.All(expected, type => Assert.Contains(type, pack));

        Assert.DoesNotContain(nameof(RiskTarget), (await trail.GetForRiskAsync(2)).Select(a => a.EntityType));
    }

    /// <summary>
    /// E8 — a scoped caller can neither add a prerequisite it cannot see nor, by replacing the economics, delete a
    /// dependency on one; it still sees the dependency's id on its own mitigation.
    /// </summary>
    [Fact]
    public async Task TestE8_AnInvisiblePrerequisiteIsPreserved()
    {
        AddRisk(1, UnitA);
        AddRisk(3, UnitB);
        AddMitigation(10, 1);
        AddMitigation(13, 3);

        await Svc.SaveMitigationAsync(10, Reduce(15_000m, 13), Author);

        ScopeTo(UnitA);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.SaveMitigationAsync(10, Reduce(15_000m, 13), Author));
        Assert.Equal(nameof(MitigationEconomicsRequest.PrerequisiteMitigationIds), ex.ParameterName);

        var replaced = await Svc.SaveMitigationAsync(10, Reduce(9_000m), Author);

        Assert.Equal([13], replaced.PrerequisiteMitigationIds);
        Assert.Single(Read(ctx => ctx.MitigationDependencies.Where(d => d.MitigationId == 10).ToList()));
    }

    // --- TG1–TG4: the target level ---------------------------------------------------------------

    /// <summary>TG1 — every invalid target is refused naming the field.</summary>
    [Theory]
    [InlineData(null, null, "ok", "TargetScore")]
    [InlineData(10.5, null, "ok", "TargetScore")]
    [InlineData(-1.0, null, "ok", "TargetScore")]
    [InlineData(null, -1.0, "ok", "TargetExpectedLoss")]
    [InlineData(3.0, null, null, "Rationale")]
    [InlineData(3.0, null, "   ", "Rationale")]
    [InlineData(3.0, null, "LONG", "Rationale")]
    public async Task TestTG1_AnInvalidTargetIsRefusedNamingTheField(double? score, double? loss, string? rationale,
        string field)
    {
        AddRisk(1, UnitA);
        if (rationale == "LONG") rationale = new string('x', 2001);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveTargetAsync(1, new RiskTargetRequest
            { TargetScore = (decimal?)score, TargetExpectedLoss = (decimal?)loss, Rationale = rationale }, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.RiskTargets.ToList()));
    }

    /// <summary>TG2 — set, compared with the residual and the appetite, read back in the risk view, updated and removed.</summary>
    [Fact]
    public async Task TestTG2_TheTargetIsComparedWithTheResidualAndTheAppetite()
    {
        AddRisk(1, UnitA, score: 5f);
        Appetite(5.0);

        var target = await Svc.SaveTargetAsync(1, new RiskTargetRequest
            { TargetScore = 3m, TargetDate = new DateOnly(2027, 3, 31), Rationale = "MFA and segmentation." }, Author);

        Assert.Equal((3m, Author), (target.TargetScore!.Value, target.SetById!.Value));
        Assert.False(target.Status.ScoreMet);
        Assert.Equal(2.0, target.Status.ScoreGap!.Value, 6);
        Assert.True(target.Status.WithinAppetite);

        var view = await Svc.GetRiskAsync(1);
        Assert.Equal(3m, view.Target!.TargetScore);
        Assert.False(view.AboveAppetite);

        var raised = await Svc.SaveTargetAsync(1, new RiskTargetRequest { TargetScore = 6m, Rationale = "Phased." }, Author);
        Assert.False(raised.Status.WithinAppetite);
        Assert.NotNull(raised.UpdatedAt);
        Assert.Single(Read(ctx => ctx.RiskTargets.ToList()));

        await Svc.DeleteTargetAsync(1, Author);
        Assert.Null((await Svc.GetRiskAsync(1)).Target);
    }

    /// <summary>TG3 — removing a target that does not exist is not found.</summary>
    [Fact]
    public async Task TestTG3_RemovingAMissingTargetIsNotFound()
    {
        AddRisk(1, UnitA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteTargetAsync(1, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteTargetAsync(999, Author));
    }

    /// <summary>TG4 — a risk outside the caller's scope is not found for the view, the target and its removal.</summary>
    [Fact]
    public async Task TestTG4_AnOutOfScopeRiskIsNotFound()
    {
        AddRisk(1, UnitA);
        await Svc.SaveTargetAsync(1, new RiskTargetRequest { TargetScore = 3m, Rationale = "x" }, Author);

        ScopeTo(UnitB);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRiskAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.SaveTargetAsync(1, new RiskTargetRequest { TargetScore = 1m, Rationale = "x" }, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteTargetAsync(1, Author));
        Assert.Equal(3m, Read(ctx => ctx.RiskTargets.Single().TargetScore));
    }

    // --- PF1–PF7: Gate D -------------------------------------------------------------------------

    /// <summary>Two quantified treatments, each costing 15 000 a year: #10 avoids 900 000, #11 only 20 000.</summary>
    private async Task TwoTreatmentsAsync()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddMitigation(10, 1);
        AddMitigation(11, 2);
        Quantify(1, mean: 1_000_000, residualMean: 100_000);
        Quantify(2, mean: 30_000, residualMean: 10_000);
        await Svc.SaveMitigationAsync(10, Reduce(15_000m), Author);
        await Svc.SaveMitigationAsync(11, Reduce(15_000m), Author);
    }

    private static List<int> Selected(PortfolioSelectionDto selection) => selection.Items
        .Where(i => i.Status == PortfolioItemStatus.Selected).Select(i => i.MitigationId).ToList();

    /// <summary>
    /// PF1 (T181, end to end) — flag 6 declared on the moderate-E[L] risk preserves its treatment over the high-E[L] one
    /// when the budget fits one; without the flag the economic ranking picks the other.
    /// </summary>
    [Fact]
    public async Task TestPF1_ASystemicRiskAtModerateExpectedLossIsPreserved()
    {
        await TwoTreatmentsAsync();

        var control = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 15_000 });
        Assert.Equal([10], Selected(control));

        Declare(2, RiskFlagCode.SystemicSinglePointOfFailure);

        var preserved = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 15_000 });
        Assert.Equal([11], Selected(preserved));
        var systemic = preserved.Items.Single(i => i.MitigationId == 11);
        Assert.Equal((PortfolioTier.Protected, true), (systemic.Tier, systemic.Systemic));
        Assert.Equal(PortfolioItemStatus.OverBudget, preserved.Items.Single(i => i.MitigationId == 10).Status);
    }

    /// <summary>PF2 — a Gate A risk's treatment is selected first, though its Gate C fails.</summary>
    [Fact]
    public async Task TestPF2_GateAIsMandatory()
    {
        await TwoTreatmentsAsync();
        await Svc.SaveMitigationAsync(11, Reduce(25_000m), Author);
        Declare(2, RiskFlagCode.HumanSafety);

        var selection = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 25_000 });

        var mandatory = selection.Items.Single(i => i.MitigationId == 11);
        Assert.Equal((PortfolioItemStatus.Selected, PortfolioTier.Mandatory, GateCOutcome.Fails),
            (mandatory.Status, mandatory.Tier, mandatory.GateC.Outcome));
        Assert.Equal([11], Selected(selection));
        Assert.False(selection.GateAShortfall);
    }

    /// <summary>PF3 — every invalid constraint, and an unknown mitigation, are refused naming the field.</summary>
    [Theory]
    [InlineData("no-budget", "Budget")]
    [InlineData("negative-budget", "Budget")]
    [InlineData("negative-people", "PeopleCapacityPersonDays")]
    [InlineData("deadline-before-start", "Deadline")]
    [InlineData("unknown-mitigation", "MitigationIds")]
    [InlineData("too-many-mitigations", "MitigationIds")]
    public async Task TestPF3_AnInvalidRequestIsRefusedNamingTheField(string scenario, string field)
    {
        var request = scenario switch
        {
            "no-budget" => new PortfolioSelectionRequest(),
            "negative-budget" => new PortfolioSelectionRequest { Budget = -1 },
            "negative-people" => new PortfolioSelectionRequest { Budget = 1, PeopleCapacityPersonDays = -1 },
            "deadline-before-start" => new PortfolioSelectionRequest
                { Budget = 1, StartDate = new DateOnly(2026, 10, 7), Deadline = new DateOnly(2026, 10, 6) },
            "unknown-mitigation" => new PortfolioSelectionRequest { Budget = 1, MitigationIds = [999] },
            _ => new PortfolioSelectionRequest { Budget = 1, MitigationIds = Enumerable.Range(1, 1001).ToList() }
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SelectPortfolioAsync(request));

        Assert.Equal(field, ex.ParameterName);
    }

    /// <summary>PF4 — another unit's treatments are neither considered nor nameable.</summary>
    [Fact]
    public async Task TestPF4_ThePortfolioIsScoped()
    {
        await TwoTreatmentsAsync();
        AddRisk(3, UnitB);
        AddMitigation(13, 3);
        await Svc.SaveMitigationAsync(13, Reduce(1m), Author);

        ScopeTo(UnitA);

        var selection = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 1_000_000 });
        Assert.DoesNotContain(selection.Items, i => i.MitigationId == 13);
        Assert.Equal(2, selection.Considered);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 1, MitigationIds = [13] }));
    }

    /// <summary>PF5 — a completed mitigation is not a candidate, and it satisfies what depends on it.</summary>
    [Fact]
    public async Task TestPF5_ACompletedMitigationSatisfiesItsDependents()
    {
        await TwoTreatmentsAsync();
        AddRisk(3, UnitA);
        AddMitigation(12, 3);
        SeedUnscoped(ctx => ctx.MitigationTasks.Add(new MitigationTask
        {
            MitigationId = 12, Title = "Done", Status = MitigationTaskStatus.Completed, CreatedAt = January,
            CompletionEvidence = "Change 881", CompletedAt = January
        }));
        await Svc.SaveMitigationAsync(10, Reduce(15_000m, 12), Author);

        var selection = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 1_000_000 });

        Assert.DoesNotContain(selection.Items, i => i.MitigationId == 12);
        Assert.Equal(PortfolioItemStatus.Selected, selection.Items.Single(i => i.MitigationId == 10).Status);
    }

    /// <summary>PF6 — the selection is a read: nothing is written.</summary>
    [Fact]
    public async Task TestPF6_TheSelectionWritesNothing()
    {
        await TwoTreatmentsAsync();
        var before = SaveChangesCount;

        await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest
            { Budget = 30_000, PeopleCapacityPersonDays = 100, Deadline = new DateOnly(2027, 1, 1) });

        Assert.Equal(before, SaveChangesCount);
    }

    /// <summary>PF7 — a closed risk's treatment is not a candidate by default.</summary>
    [Fact]
    public async Task TestPF7_AClosedRiskIsNotACandidate()
    {
        await TwoTreatmentsAsync();
        AddRisk(4, UnitA, status: "Closed");
        AddMitigation(14, 4);

        var selection = await Svc.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 1 });

        Assert.DoesNotContain(selection.Items, i => i.MitigationId == 14);
        Assert.Equal(2, selection.Considered);
    }

    // --- AT1–AT7: completion evidence and acceptance criterion (T179) -----------------------------

    private async Task<MitigationTask> OpenTaskAsync(string? criterion = null)
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);
        return await Tasks.CreateAsync(new MitigationTaskRequest
            { MitigationId = 10, Title = "Enforce MFA", OwnerId = Author, AcceptanceCriterion = criterion }, Author);
    }

    private static MitigationTaskRequest Update(MitigationTask task, MitigationTaskStatus? status = null,
        string? evidence = null, string? criterion = null) => new()
    {
        Id = task.Id, MitigationId = task.MitigationId, Title = task.Title, OwnerId = task.OwnerId, Status = status,
        CompletionEvidence = evidence, AcceptanceCriterion = criterion
    };

    /// <summary>AT1 — completing a task without evidence is refused, and the task stays as it was.</summary>
    [Fact]
    public async Task TestAT1_CompletingWithoutEvidenceIsRefused()
    {
        var task = await OpenTaskAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Tasks.UpdateAsync(Update(task, MitigationTaskStatus.Completed), Author));

        Assert.Equal(nameof(MitigationTaskRequest.CompletionEvidence), ex.ParameterName);
        var stored = Read(ctx => ctx.MitigationTasks.Single(t => t.Id == task.Id));
        Assert.Equal((MitigationTaskStatus.Open, (DateTime?)null), (stored.Status, stored.CompletedAt));
    }

    /// <summary>AT2 — completing with evidence records it, with who and when stamped by the server.</summary>
    [Fact]
    public async Task TestAT2_CompletingWithEvidenceStampsWhoAndWhen()
    {
        var task = await OpenTaskAsync();

        var done = await Tasks.UpdateAsync(Update(task, MitigationTaskStatus.Completed, "  CA policy 12 enforced  "), Author);

        Assert.Equal(MitigationTaskStatus.Completed, done.Status);
        Assert.Equal("CA policy 12 enforced", done.CompletionEvidence);
        Assert.Equal(Author, done.CompletionEvidenceById);
        Assert.NotNull(done.CompletionEvidenceAt);
        Assert.NotNull(done.CompletedAt);
    }

    /// <summary>AT3 — on update, null leaves the new fields as they are and blank clears the criterion (S47 D12).</summary>
    [Fact]
    public async Task TestAT3_NullLeavesTheFieldsAndBlankClears()
    {
        var task = await OpenTaskAsync(criterion: "MFA on for every admin account");

        var untouched = await Tasks.UpdateAsync(Update(task), Author);
        Assert.Equal("MFA on for every admin account", untouched.AcceptanceCriterion);

        var cleared = await Tasks.UpdateAsync(Update(task, criterion: "   "), Author);
        Assert.Null(cleared.AcceptanceCriterion);
    }

    /// <summary>AT4 — the evidence of a completed task cannot be removed; reopening first allows it.</summary>
    [Fact]
    public async Task TestAT4_TheEvidenceOfACompletedTaskCannotBeRemoved()
    {
        var task = await OpenTaskAsync();
        await Tasks.UpdateAsync(Update(task, MitigationTaskStatus.Completed, "Ticket 4411"), Author);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Tasks.UpdateAsync(Update(task, evidence: ""), Author));
        Assert.Equal(nameof(MitigationTaskRequest.CompletionEvidence), ex.ParameterName);
        Assert.Equal("Ticket 4411", Read(ctx => ctx.MitigationTasks.Single(t => t.Id == task.Id).CompletionEvidence));

        var reopened = await Tasks.UpdateAsync(Update(task, MitigationTaskStatus.InProgress, ""), Author);
        Assert.Null(reopened.CompletionEvidence);
        Assert.Null(reopened.CompletionEvidenceById);
    }

    /// <summary>AT5 — a task created already completed needs evidence too.</summary>
    [Fact]
    public async Task TestAT5_CreatingACompletedTaskNeedsEvidence()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() => Tasks.CreateAsync(new MitigationTaskRequest
            { MitigationId = 10, Title = "Backfilled", Status = MitigationTaskStatus.Completed }, Author));

        var created = await Tasks.CreateAsync(new MitigationTaskRequest
        {
            MitigationId = 10, Title = "Backfilled", Status = MitigationTaskStatus.Completed,
            CompletionEvidence = "Change 900"
        }, Author);

        Assert.Equal((Author, "Change 900"), (created.CompletionEvidenceById!.Value, created.CompletionEvidence));
        Assert.NotNull(created.CompletedAt);
    }

    /// <summary>AT6 — the new texts are bounded.</summary>
    [Fact]
    public async Task TestAT6_TheTextsAreBounded()
    {
        var task = await OpenTaskAsync();
        var tooLong = new string('x', TreatmentEconomicsLimits.MaxTaskTextLength + 1);

        var criterion = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Tasks.UpdateAsync(Update(task, criterion: tooLong), Author));
        Assert.Equal(nameof(MitigationTaskRequest.AcceptanceCriterion), criterion.ParameterName);

        var evidence = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Tasks.UpdateAsync(Update(task, MitigationTaskStatus.Completed, tooLong), Author));
        Assert.Equal(nameof(MitigationTaskRequest.CompletionEvidence), evidence.ParameterName);
    }

    /// <summary>AT7 — the mitigation's action plan says what Phase 5 still needs from each line.</summary>
    [Fact]
    public async Task TestAT7_TheActionPlanListsWhatIsMissing()
    {
        await OpenTaskAsync();

        var line = Assert.Single((await Svc.GetMitigationAsync(10)).ActionPlan);

        Assert.Equal([ActionPlanElement.DueDate, ActionPlanElement.AcceptanceCriterion], line.Missing);
    }

    // --- QM1: the residual mean ------------------------------------------------------------------

    /// <summary>
    /// QM1 — the Monte Carlo records the residual <em>mean</em> (Gate C's E[L] after): the mean of the residual run, which
    /// for an effectiveness applied to every loss is the inherent mean × (1 − e) — and not the residual median. Without a
    /// residual run it stays null.
    /// </summary>
    [Fact]
    public async Task TestQM1_TheResidualMeanIsRecorded()
    {
        AddRisk(5, UnitA);
        AddRisk(6, UnitA);
        AddMitigation(15, 5, percent: 50);
        var input = new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 0.05, LossEventFrequencyMostLikely = 0.2, LossEventFrequencyMax = 1,
            LossMagnitudeMin = 10_000, LossMagnitudeMostLikely = 50_000, LossMagnitudeMax = 400_000, Seed = 7
        };

        var quant = GetService<IQuantitativeRiskService>();
        var treated = await quant.ComputeAndSaveAsync(5, input);

        Assert.NotNull(treated.ResidualMean);
        Assert.Equal(treated.InherentMean * 0.5, treated.ResidualMean!.Value, treated.InherentMean * 1e-9);
        Assert.Equal(treated.ResidualMean, Read(ctx => ctx.RiskScorings.Single(s => s.Id == 5).QuantResidualAleMean));
        Assert.NotEqual(treated.ResidualP50, treated.ResidualMean);
        Assert.Equal(treated.ResidualMean, (await quant.GetAsync(5))!.ResidualMean);

        var untreated = await quant.ComputeAndSaveAsync(6, input);
        Assert.Null(untreated.ResidualMean);
        Assert.Null(Read(ctx => ctx.RiskScorings.Single(s => s.Id == 6).QuantResidualAleMean));
    }
}
