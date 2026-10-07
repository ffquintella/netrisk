using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using Model.RiskFlags;
using Model.TailRisk;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Tools.Risks;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.7 (S48 §8) — <see cref="TailRiskService"/>, the tail rows of <see cref="QuantitativeRiskService"/>, Gate B on
/// the tail in <see cref="RiskWorkflowService"/>/<see cref="RiskAcceptancesService"/> and the flag 8 derivation of
/// <see cref="RiskFlagsService"/>, against the real model, scope filters and audit interceptor on the EF in-memory
/// provider: guards V1–V8, tail Q1–Q5, Gate B GB1–GB9, flag 8 F1–F3, portfolio P1–P7, correlations C1–C4 and the
/// appetite's tolerances L1–L3.
///
/// The organisation is the Stage 9.1 one (<see cref="RiskChainTestBase"/>): units 100 and 200, user 7. Every Monte Carlo
/// run is seeded, so each statistical assertion is deterministic.
/// </summary>
[TestSubject(typeof(TailRiskService))]
public class TailRiskServiceInMemoryTest : RiskChainTestBase
{
    private const int Cro = 1;
    private const int Owner = 2;
    private const int GlobalAppetite = 1;
    private const int UnitAAppetite = 2;

    private static readonly CalibratedRange RareFrequency = new(0.01, 0.03, 0.06);
    private static readonly CalibratedRange Catastrophic = new(1_000_000, 4_000_000, 10_000_000);
    private static readonly CalibratedRange FrequentFrequency = new(0.5, 2, 5);
    private static readonly CalibratedRange Moderate = new(10_000, 50_000, 500_000);

    private ITailRiskService Svc => GetService<ITailRiskService>();
    private IQuantitativeRiskService Quant => GetService<IQuantitativeRiskService>();
    private IRiskAcceptancesService Acceptances => GetService<IRiskAcceptancesService>();
    private IRiskFlagsService Flags => GetService<IRiskFlagsService>();
    private IRiskWorkflowService Workflow => GetService<IRiskWorkflowService>();

    public TailRiskServiceInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(NewUser(Cro, "cro", admin: true));
            ctx.Users.Add(NewUser(Owner, "owner"));
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
        });
    }

    // --- seeding ---------------------------------------------------------------------------------

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@example.test"
    };

    /// <summary>A risk owned by <see cref="Owner"/>, so <see cref="Cro"/> may accept it.</summary>
    private void OwnedRisk(int id, int? unit = UnitA)
    {
        AddRisk(id, unit);
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == id);
            risk.Owner = Owner;
            risk.Manager = Owner;
            risk.SubmittedBy = Owner;
        });
    }

    private void AddMitigation(int id, int riskId, int percent) =>
        SeedUnscoped(ctx => ctx.Mitigations.Add(new Mitigation
        {
            Id = id, RiskId = riskId, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
            MitigationOwner = Author, SubmittedBy = Author, MitigationPercent = percent, CurrentSolution = "",
            SecurityRequirements = "", SecurityRecommendations = "", SubmissionDate = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow, PlanningDate = new DateOnly(2026, 6, 1)
        }));

    private Task<QuantitativeRiskResult> Quantify(int riskId, CalibratedRange frequency, CalibratedRange magnitude,
        int iterations = 2_000, int seed = 7) =>
        Quant.ComputeAndSaveAsync(riskId, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = frequency.Min, LossEventFrequencyMostLikely = frequency.MostLikely,
            LossEventFrequencyMax = frequency.Max, LossMagnitudeMin = magnitude.Min,
            LossMagnitudeMostLikely = magnitude.MostLikely, LossMagnitudeMax = magnitude.Max,
            Iterations = iterations, Seed = seed
        });

    /// <summary>The organisation-wide appetite (ceiling 10 unless said) and, optionally, its tail tolerances.</summary>
    private void Appetite(double ceiling = 10, decimal? maxCvar = null, decimal? maxP95 = null,
        decimal? maxPortfolioCvar = null) =>
        SeedUnscoped(ctx =>
        {
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = GlobalAppetite, EntityId = null, MaxAcceptableResidual = ceiling, DualApprovalThreshold = ceiling,
                CreatedAt = DateTime.UtcNow
            });

            if (maxCvar is null && maxP95 is null && maxPortfolioCvar is null) return;

            ctx.RiskAppetiteTailLimits.Add(new RiskAppetiteTailLimit
            {
                AppetiteId = GlobalAppetite, MaxScenarioCvar95 = maxCvar, MaxScenarioP95 = maxP95,
                MaxPortfolioCvar95 = maxPortfolioCvar, Rationale = "Board minute 2026/07.", CreatedAt = DateTime.UtcNow
            });
        });

    private static RiskAcceptanceRequest Acceptance() => new()
    {
        Name = "Exception", BusinessJustification = "Insured and monitored.", ExpiresAt = DateTime.UtcNow.AddDays(90)
    };

    private static LossComponentRequest Component(LossComponent component, double min, double mode, double max,
        string? basis = null) => new() { Component = component, Min = min, MostLikely = mode, Max = max, Basis = basis };

    private static LossComponentsRequest Components(params LossComponentRequest[] items) => new() { Components = items.ToList() };

    private static RiskCorrelationRequest Pair(int a, int b, decimal coefficient, string rationale = "Same identity provider.") =>
        new() { RiskAId = a, RiskBId = b, Coefficient = coefficient, Rationale = rationale };

    private List<RiskTailStatistics> TailRows(int riskId) =>
        Read(ctx => ctx.RiskTailStatistics.Where(t => t.RiskId == riskId).OrderBy(t => t.Run).ToList());

    private List<AuditLog> Audit(string type) =>
        Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());

    // --- V1–V8: guards ---------------------------------------------------------------------------

    /// <summary>V1 — every invalid component request is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("null-list", "Components")]
    [InlineData("empty", "Components")]
    [InlineData("eight", "Components")]
    [InlineData("undefined", "Components[0].Component")]
    [InlineData("duplicate", "Components[1].Component")]
    [InlineData("unordered", "Components[0].MostLikely")]
    [InlineData("negative", "Components[0].MostLikely")]
    [InlineData("too-large", "Components[0].MostLikely")]
    [InlineData("missing-max", "Components[0].MostLikely")]
    [InlineData("infinite", "Components[0].MostLikely")]
    [InlineData("fine-without-basis", "Components[0].Basis")]
    [InlineData("long-basis", "Components[0].Basis")]
    public async Task TestV1_AnInvalidComponentRequestIsRefusedNamingTheField(string scenario, string field)
    {
        AddRisk(1, UnitA);

        var request = scenario switch
        {
            "null-list" => new LossComponentsRequest(),
            "empty" => Components(),
            "eight" => Components(Enumerable.Range(1, 8)
                .Select(i => Component((LossComponent)((i % 7) + 1), 0, 1, 2)).ToArray()),
            "undefined" => Components(Component((LossComponent)9, 0, 1, 2)),
            "duplicate" => Components(Component(LossComponent.Response, 0, 1, 2), Component(LossComponent.Response, 0, 1, 2)),
            "unordered" => Components(Component(LossComponent.Response, 5, 1, 2)),
            "negative" => Components(Component(LossComponent.Response, -1, 1, 2)),
            "too-large" => Components(Component(LossComponent.Response, 0, 1, 2e12)),
            "missing-max" => Components(new LossComponentRequest { Component = LossComponent.Response, Min = 0, MostLikely = 1 }),
            "infinite" => Components(Component(LossComponent.Response, 0, 1, double.PositiveInfinity)),
            "fine-without-basis" => Components(Component(LossComponent.Fine, 0, 1, 2, "   ")),
            _ => Components(Component(LossComponent.Response, 0, 1, 2, new string('x', 1001)))
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveLossComponentsAsync(1, request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, Read(ctx => ctx.RiskLossComponents.Count()));
    }

    /// <summary>V2 — a missing risk, and one outside the caller's scope, are not found for reading and writing.</summary>
    [Fact]
    public async Task TestV2_AMissingOrOutOfScopeRiskIsNotFound()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var valid = Components(Component(LossComponent.Response, 0, 1, 2));

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRiskAsync(999));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveLossComponentsAsync(999, valid, Author));

        ScopeTo(UnitB);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRiskAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveLossComponentsAsync(1, valid, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteLossComponentsAsync(1, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetCorrelationsAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveCorrelationAsync(Pair(1, 2, 0.5m), Author));

        Assert.Equal(0, Read(ctx => ctx.RiskLossComponents.Count() + ctx.RiskCorrelations.Count()));
    }

    /// <summary>V3 — an invalid correlation request is refused naming the field; 0.9996 rounds to 1.000 and is valid.</summary>
    [Theory]
    [InlineData("no-a", "RiskAId")]
    [InlineData("no-b", "RiskBId")]
    [InlineData("same", "RiskBId")]
    [InlineData("no-coefficient", "Coefficient")]
    [InlineData("above-one", "Coefficient")]
    [InlineData("negative", "Coefficient")]
    [InlineData("no-rationale", "Rationale")]
    [InlineData("long-rationale", "Rationale")]
    public async Task TestV3_AnInvalidCorrelationIsRefusedNamingTheField(string scenario, string field)
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);

        var request = scenario switch
        {
            "no-a" => new RiskCorrelationRequest { RiskBId = 2, Coefficient = 0.5m, Rationale = "x" },
            "no-b" => new RiskCorrelationRequest { RiskAId = 1, Coefficient = 0.5m, Rationale = "x" },
            "same" => Pair(1, 1, 0.5m),
            "no-coefficient" => new RiskCorrelationRequest { RiskAId = 1, RiskBId = 2, Rationale = "x" },
            "above-one" => Pair(1, 2, 1.2m),
            "negative" => Pair(1, 2, -0.1m),
            "no-rationale" => Pair(1, 2, 0.5m, " "),
            _ => Pair(1, 2, 0.5m, new string('x', 2001))
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveCorrelationAsync(request, Author));
        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, Read(ctx => ctx.RiskCorrelations.Count()));

        Assert.Equal(1.000m, (await Svc.SaveCorrelationAsync(Pair(1, 2, 0.9996m), Author)).Coefficient);
    }

    /// <summary>
    /// V4 — a correlation that would make its group's matrix not positive semidefinite is refused with 422 and nothing
    /// written. The check is organisation-wide: a scoped caller is refused because of a pair it cannot see, and the
    /// message names no risk.
    /// </summary>
    [Fact]
    public async Task TestV4_ANonPositiveSemidefiniteMatrixIsRefusedOrganisationWide()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitB);

        await Svc.SaveCorrelationAsync(Pair(1, 3, 0.9m), Author);

        // 1~3 at 0.9 and 1~2 at 0.9 force 2~3 to be strongly positive; undeclared, it is 0 — not a valid matrix.
        ScopeTo(UnitA);
        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.SaveCorrelationAsync(Pair(1, 2, 0.9m), Author));

        Assert.Equal(TailRiskService.NotPositiveSemidefiniteRule, ex.RuleName);
        Assert.DoesNotContain("3", ex.Message);
        Assert.Equal(1, Read(ctx => ctx.RiskCorrelations.Count()));

        // A compatible coefficient is accepted (1 − 0.9² − 0.3² > 0 with the implied 0 between 2 and 3).
        Assert.Equal(0.3m, (await Svc.SaveCorrelationAsync(Pair(1, 2, 0.3m), Author)).Coefficient);
    }

    /// <summary>V5 — an invalid portfolio request is refused naming the field.</summary>
    [Theory]
    [InlineData("basis", "Basis")]
    [InlineData("too-many", "RiskIds")]
    [InlineData("unknown", "RiskIds")]
    [InlineData("out-of-scope", "RiskIds")]
    [InlineData("entity", "EntityId")]
    public async Task TestV5_AnInvalidPortfolioRequestIsRefused(string scenario, string field)
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);
        ScopeTo(UnitA);

        var request = scenario switch
        {
            "basis" => new PortfolioTailRequest { Basis = (PortfolioBasis)9 },
            "too-many" => new PortfolioTailRequest { RiskIds = Enumerable.Range(1, 501).ToList() },
            "unknown" => new PortfolioTailRequest { RiskIds = [1, 999] },
            "out-of-scope" => new PortfolioTailRequest { RiskIds = [1, 2] },
            _ => new PortfolioTailRequest { EntityId = 4242 }
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.AggregatePortfolioAsync(request));
        Assert.Equal(field, ex.ParameterName);
    }

    /// <summary>V6 — removing components from a risk that has none is not found.</summary>
    [Fact]
    public async Task TestV6_DeletingAbsentComponentsIsNotFound()
    {
        AddRisk(1, UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteLossComponentsAsync(1, Author));
    }

    /// <summary>V7 — more than 100 000 iterations is refused (there was no ceiling); below 1 000 is still raised to it.</summary>
    [Fact]
    public async Task TestV7_TheIterationCountHasACeiling()
    {
        AddRisk(1, UnitA);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Quantify(1, FrequentFrequency, Moderate, iterations: 100_001));
        Assert.Equal(nameof(QuantitativeRiskInput.Iterations), ex.ParameterName);
        Assert.Empty(TailRows(1));

        var floored = await Quantify(1, FrequentFrequency, Moderate, iterations: 10);
        Assert.Equal(1_000, floored.InherentTail!.Iterations);
    }

    /// <summary>V8 — with components declared, a magnitude that contradicts their envelope is refused; the envelope passes.</summary>
    [Fact]
    public async Task TestV8_AMagnitudeThatContradictsTheComponentsIsRefused()
    {
        AddRisk(1, UnitA);
        await Svc.SaveLossComponentsAsync(1, Components(
            Component(LossComponent.Response, 10_000, 20_000, 50_000),
            Component(LossComponent.Fine, 0, 100_000, 1_000_000, "LGPD art. 52")), Author);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Quantify(1, FrequentFrequency, Moderate));
        Assert.Equal(nameof(QuantitativeRiskInput.LossMagnitudeMostLikely), ex.ParameterName);
        Assert.Contains("declared by component", ex.Message);

        var envelope = await Quantify(1, FrequentFrequency, new CalibratedRange(10_000, 120_000, 1_050_000));
        Assert.Equal(MagnitudeSource.Components, envelope.InherentTail!.MagnitudeSource);
    }

    // --- Q1–Q5: the stored tail ------------------------------------------------------------------

    /// <summary>
    /// Q1 — a computation stores both runs with their intervals, equal to the pure computation for the same seed, and
    /// returns them on the quantitative result.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheComputationStoresBothRunsWithTheirIntervals()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1, percent: 40);

        var result = await Quantify(1, FrequentFrequency, Moderate);

        var rows = TailRows(1);
        Assert.Equal([TailRun.Inherent, TailRun.Residual], rows.Select(r => r.Run));

        var pure = MonteCarloRiskSimulator.Run(FrequentFrequency, Moderate, 2_000, 7).Tail;
        var inherent = rows[0];
        Assert.Equal(pure.P95, inherent.P95);
        Assert.Equal(pure.P95CiLow, inherent.P95CiLow);
        Assert.Equal(pure.P95CiHigh, inherent.P95CiHigh);
        Assert.Equal(pure.Cvar95, inherent.Cvar95);
        Assert.Equal(pure.Cvar95CiLow, inherent.Cvar95CiLow);
        Assert.Equal(pure.Cvar95CiHigh, inherent.Cvar95CiHigh);
        Assert.Equal(pure.ExpectedLoss, inherent.ExpectedLoss, 1e-9 * pure.ExpectedLoss);
        Assert.Equal(0.95m, inherent.ConfidenceLevel);
        Assert.Equal((FrequentFrequency.Min, FrequentFrequency.MostLikely, FrequentFrequency.Max),
            (inherent.LefMin, inherent.LefMostLikely, inherent.LefMax));
        Assert.Equal(MagnitudeSource.SingleRange, inherent.MagnitudeSource);

        var residual = rows[1];
        Assert.Equal(0.4, residual.MitigationEffectiveness, 12);
        Assert.Equal(inherent.Cvar95 * 0.6, residual.Cvar95, 1e-6 * inherent.Cvar95);

        Assert.Equal(inherent.P95, result.InherentTail!.P95);
        Assert.Equal(residual.Cvar95, result.ResidualTail!.Cvar95);
        Assert.Equal(inherent.Cvar95, (await Quant.GetAsync(1))!.InherentTail!.Cvar95);
    }

    /// <summary>
    /// Q2 (T187) — end to end: a scenario that loses in about 3 % of years is stored with P90 and P95 zero and a CVaR95
    /// far from zero, and the risk's tail view says so.
    /// </summary>
    [Fact]
    public async Task TestQ2_TheStoredCvarOfALowFrequencyScenarioIsNotZero()
    {
        AddRisk(1, UnitA);

        var result = await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000, seed: 20260826);

        Assert.Equal(0, result.InherentP90);
        var tail = (await Svc.GetRiskAsync(1)).Inherent!;
        Assert.Equal(0, tail.P95);
        Assert.True(tail.Cvar95 > 1_000_000, $"CVaR95 {tail.Cvar95:N0}");
        Assert.InRange(tail.ProbabilityOfLoss, 0.02, 0.045);
        Assert.True(tail.ConditionalLoss > 4_000_000);
    }

    /// <summary>
    /// Q3 — declaring components recomputes the analysis with them: the contributions add up to the run's E[L] and
    /// CVaR95, the single range becomes their envelope, a second declaration updates rows in place, and deleting them
    /// goes back to a single range. On a risk with no analysis the components are only stored.
    /// </summary>
    [Fact]
    public async Task TestQ3_DeclaringComponentsRecomputesWithThem()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await Quantify(1, FrequentFrequency, Moderate);

        var tail = await Svc.SaveLossComponentsAsync(1, Components(
            Component(LossComponent.Response, 5_000, 10_000, 40_000),
            Component(LossComponent.Productivity, 0, 20_000, 100_000),
            Component(LossComponent.Fine, 0, 0, 2_000_000, "LGPD art. 52, II")), Author);

        Assert.True(tail.Recomputed);
        var run = tail.Inherent!;
        Assert.Equal(MagnitudeSource.Components, run.MagnitudeSource);
        Assert.Equal([LossComponent.Response, LossComponent.Productivity, LossComponent.Fine],
            run.Components.Select(c => c.Component));
        Assert.Equal(run.ExpectedLoss, run.Components.Sum(c => c.ExpectedLoss), 1e-6 * run.ExpectedLoss);
        Assert.Equal(run.Cvar95, run.Components.Sum(c => c.Cvar95), 1e-6 * run.Cvar95);
        Assert.Equal(3, tail.DeclaredComponents.Count);

        var scoring = Read(ctx => ctx.RiskScorings.Single(s => s.Id == 1));
        Assert.Equal((5_000d, 30_000d, 2_140_000d), (scoring.QuantLossMin!.Value, scoring.QuantLossMostLikely!.Value,
            scoring.QuantLossMax!.Value));

        var responseId = Read(ctx => ctx.RiskLossComponents.Single(c => c.Component == LossComponent.Response).Id);
        var second = await Svc.SaveLossComponentsAsync(1, Components(
            Component(LossComponent.Response, 5_000, 15_000, 40_000)), Author);
        Assert.Equal(responseId, Read(ctx => ctx.RiskLossComponents.Single().Id));
        Assert.Single(second.Inherent!.Components);

        var removed = await Svc.DeleteLossComponentsAsync(1, Author);
        Assert.True(removed.Recomputed);
        Assert.Equal(MagnitudeSource.SingleRange, removed.Inherent!.MagnitudeSource);
        Assert.Empty(removed.Inherent.Components);
        Assert.Empty(removed.DeclaredComponents);

        var unquantified = await Svc.SaveLossComponentsAsync(2, Components(
            Component(LossComponent.Reputation, 0, 1, 2)), Author);
        Assert.False(unquantified.Recomputed);
        Assert.Null(unquantified.Inherent);
        Assert.Single(unquantified.DeclaredComponents);
    }

    /// <summary>
    /// Q4 (S48 D1) — <c>PUT /Risks/{id}/Scoring</c> copies the whole scoring payload and blanks every quant_* column a
    /// client does not send (the S47 R3 defect, unchanged here). The tail statistics live elsewhere, so they survive,
    /// and Gate B keeps reading them.
    /// </summary>
    [Fact]
    public async Task TestQ4_APayloadCopyOfTheScoringDoesNotEraseTheTail()
    {
        AddRisk(1, UnitA);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);
        var before = TailRows(1).Single();

        Risks.SaveRiskScoring(new RiskScoring
            { Id = 1, ScoringMethod = 1, CalculatedRisk = 2, ClassicImpact = 1, ClassicLikelihood = 1 });

        var after = TailRows(1).Single();
        Assert.Equal(before.Cvar95, after.Cvar95);
        Assert.Equal(before.LefMostLikely, after.LefMostLikely);
        Assert.Equal(before.Cvar95, (await Svc.GetRiskAsync(1)).Inherent!.Cvar95);
        Assert.Equal(before.Cvar95, (await Workflow.EvaluateAppetiteAsync(1)).Tail.Cvar95 ?? -1);
    }

    /// <summary>Q5 — a recomputation replaces the rows, and removes the residual one when there is no residual run any more.</summary>
    [Fact]
    public async Task TestQ5_RecomputingReplacesTheRowsAndDropsAVanishedResidual()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1, percent: 50);

        await Quantify(1, FrequentFrequency, Moderate);
        await Quant.RecomputeAsync(1);
        Assert.Equal(2, TailRows(1).Count);

        SeedUnscoped(ctx => ctx.Mitigations.Single(m => m.Id == 10).MitigationPercent = 0);
        await Quant.RecomputeAsync(1);

        Assert.Equal([TailRun.Inherent], TailRows(1).Select(r => r.Run));
        Assert.Null((await Quant.GetAsync(1))!.ResidualTail);
        Assert.Null(await Quant.RecomputeAsync(999));
    }

    // --- GB1–GB9: Gate B on the tail -------------------------------------------------------------

    /// <summary>GB1 — the tail exceeds the CVaR tolerance: the appetite says so and the acceptance is refused, nothing written.</summary>
    [Fact]
    public async Task TestGB1_ATailAboveToleranceRefusesTheAcceptance()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 1_000_000);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.False(appetite.ExceedsCeiling);
        Assert.Equal(TailAppetiteState.ExceedsTolerance, appetite.Tail.State);
        Assert.Equal(TailRun.Inherent, appetite.Tail.Run);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.TailToleranceRule, ex.RuleName);
        Assert.Contains("CVaR95", ex.Message);
        Assert.Equal(0, Read(ctx => ctx.RiskAcceptances.Count()));
    }

    /// <summary>GB2 — the same tail within a higher tolerance: accepted.</summary>
    [Fact]
    public async Task TestGB2_ATailWithinToleranceIsAccepted()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 9_000_000);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        Assert.Equal(TailAppetiteState.WithinTolerance, (await Workflow.EvaluateAppetiteAsync(1)).Tail.State);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB3 — a tolerance and no analysis: not assessable, never "within", and the acceptance proceeds (S48 D12).</summary>
    [Fact]
    public async Task TestGB3_NoTailStatisticsIsNotAssessableAndDoesNotRefuse()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 1);

        var tail = (await Workflow.EvaluateAppetiteAsync(1)).Tail;
        Assert.Equal(TailAppetiteState.NotAssessable, tail.State);
        Assert.Equal([TailAppetiteNotAssessableReason.NoTailStatistics], tail.Reasons);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB4 — both the ordinal ceiling and the tail refuse: the refusal is the ceiling's, the order of Track 8 kept.</summary>
    [Fact]
    public async Task TestGB4_TheOrdinalCeilingIsStillCheckedFirst()
    {
        OwnedRisk(1);
        Appetite(ceiling: 1, maxCvar: 1_000_000);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.True(appetite.ExceedsCeiling);
        Assert.Equal(TailAppetiteState.ExceedsTolerance, appetite.Tail.State);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal("risk_appetite_ceiling", ex.RuleName);
    }

    /// <summary>GB5 — Gate A precedes Gate B on the tail too.</summary>
    [Fact]
    public async Task TestGB5_GateAPrecedesTheTail()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 1_000_000);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);
        await Flags.DeclareAsync(1, RiskFlagCode.HumanSafety,
            new RiskFlagDeclarationRequest { Reason = "The pump controller doses patients." }, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
    }

    /// <summary>GB6 — without tolerances nothing changes: not configured, accepted; and without any appetite, the same.</summary>
    [Fact]
    public async Task TestGB6_WithoutTolerancesTheTailIsNotGated()
    {
        OwnedRisk(1);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var none = await Workflow.EvaluateAppetiteAsync(1);
        Assert.False(none.AppetiteConfigured);
        Assert.Equal(TailAppetiteState.NotConfigured, none.Tail.State);

        Appetite();
        var configured = await Workflow.EvaluateAppetiteAsync(1);
        Assert.Equal(TailAppetiteState.NotConfigured, configured.Tail.State);
        Assert.Equal(GlobalAppetite, configured.Tail.AppetiteId);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB7 — the residual run is compared where it exists: a tolerance between the residual and inherent CVaR holds.</summary>
    [Fact]
    public async Task TestGB7_TheResidualTailIsComparedWhereItExists()
    {
        OwnedRisk(1);
        AddMitigation(10, 1, percent: 80);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var rows = TailRows(1);
        var between = (decimal)((rows[0].Cvar95 + rows[1].Cvar95) / 2);
        Appetite(maxCvar: between);

        var tail = (await Workflow.EvaluateAppetiteAsync(1)).Tail;
        Assert.Equal(TailRun.Residual, tail.Run);
        Assert.Equal(rows[1].Cvar95, tail.Cvar95);
        Assert.Equal(TailAppetiteState.WithinTolerance, tail.State);
    }

    /// <summary>GB8 — an entity's own appetite without tail tolerances governs: it does not inherit the global ones (S48 D8).</summary>
    [Fact]
    public async Task TestGB8_AnEntityAppetiteDoesNotInheritTheGlobalTolerances()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 1_000_000);
        SeedUnscoped(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = UnitAAppetite, EntityId = UnitA, MaxAcceptableResidual = 10, DualApprovalThreshold = 10,
            CreatedAt = DateTime.UtcNow
        }));
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var tail = (await Workflow.EvaluateAppetiteAsync(1)).Tail;
        Assert.Equal(TailAppetiteState.NotConfigured, tail.State);
        Assert.Equal(UnitAAppetite, tail.AppetiteId);
    }

    /// <summary>GB9 — a renewal is refused once the tolerance is lowered below the tail.</summary>
    [Fact]
    public async Task TestGB9_ARenewalIsRefusedByTheTail()
    {
        OwnedRisk(1);
        Appetite(maxCvar: 9_000_000);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);
        var accepted = await Acceptances.CreateAsync(1, Acceptance(), Cro);

        await Svc.SaveAppetiteLimitsAsync(GlobalAppetite,
            new RiskAppetiteTailLimitsRequest { MaxScenarioCvar95 = 1_000_000, Rationale = "Board minute 2026/09." }, Cro);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.RenewAsync(accepted.Id, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.TailToleranceRule, ex.RuleName);
    }

    // --- F1–F3: flag 8 ---------------------------------------------------------------------------

    /// <summary>F1 — a rare catastrophic scenario derives flag 8 on reconciliation, with the basis; it is not Gate A.</summary>
    [Fact]
    public async Task TestF1_ARareCatastrophicScenarioDerivesFlag8()
    {
        AddRisk(1, UnitA);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var view = await Svc.GetRiskAsync(1);
        Assert.True(view.TailFlag.Holds);
        Assert.False(view.TailFlag.Derived); // computing does not reconcile (S48 D10)

        var state = await Flags.RefreshAsync(1);
        var flag = state.Flags.Single(f => f.Code == RiskFlagCode.LowProbabilityCatastrophic);
        Assert.True(flag.Derived);
        Assert.StartsWith("inherent run (seed 7, 10000 iterations): loss in ", flag.DerivedBasis);
        Assert.False(state.GateA.Holds);
        Assert.True((await Svc.GetRiskAsync(1)).TailFlag.Derived);
    }

    /// <summary>F2 — the basis is lost (recomputed with a small magnitude): flag 8 reverts with a note and a system trail.</summary>
    [Fact]
    public async Task TestF2_Flag8RevertsWithATrailWhenItsBasisIsLost()
    {
        AddRisk(1, UnitA);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);
        await Flags.RefreshAsync(1);

        await Quantify(1, RareFrequency, new CalibratedRange(1_000, 5_000, 10_000), iterations: 10_000);
        var state = await Flags.RefreshAsync(1);

        var flag = state.Flags.Single(f => f.Code == RiskFlagCode.LowProbabilityCatastrophic);
        Assert.False(flag.IsSet);
        Assert.StartsWith("Derived basis lost: inherent run", flag.DerivedNote);

        var trail = Audit(nameof(RiskFlag)).Where(a => a.Action == AuditLogAction.Update).ToList();
        var derived = Assert.Single(trail, a => a.Field == nameof(RiskFlag.Derived));
        Assert.Equal(("true", "false"), (derived.OldValue, derived.NewValue));
        Assert.Equal("system", derived.Actor);
        Assert.Null(derived.UserId);
    }

    /// <summary>F3 — the thresholds come from the settings: a higher catastrophic loss stops the derivation.</summary>
    [Fact]
    public async Task TestF3_TheThresholdsAreReadFromTheSettings()
    {
        AddRisk(1, UnitA);
        SeedUnscoped(ctx => ctx.Settings.Add(new Setting
            { Name = TailRiskSettingKeys.TailFlagCatastrophicLoss, Value = "50000000" }));
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);

        var view = await Svc.GetRiskAsync(1);
        Assert.False(view.TailFlag.Holds);
        Assert.Equal(50_000_000, view.TailFlag.CatastrophicLoss);
        Assert.Equal(0.10, view.TailFlag.MaxAnnualProbability);

        var state = await Flags.RefreshAsync(1);
        Assert.False(state.Flags.Single(f => f.Code == RiskFlagCode.LowProbabilityCatastrophic).Derived);
    }

    /// <summary>F4 — the nightly pass derives flag 8 on every open risk with the basis, and skips a closed one.</summary>
    [Fact]
    public async Task TestF4_TheNightlyPassDerivesFlag8OnOpenRisks()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA, status: RiskWorkflowService.StatusClosed);
        AddRisk(3, UnitA);
        await Quantify(1, RareFrequency, Catastrophic, iterations: 10_000);
        await Quantify(2, RareFrequency, Catastrophic, iterations: 10_000);
        await Quantify(3, FrequentFrequency, Moderate);

        var summary = await Flags.RefreshAllAsync();

        Assert.Equal(1, summary.FlagsRaised);
        Assert.True(Read(ctx => ctx.RiskFlags.Single(f => f.RiskId == 1)).Derived);
        Assert.Equal(0, Read(ctx => ctx.RiskFlags.Count(f => f.RiskId == 2 || f.RiskId == 3)));
    }

    // --- P1–P7: the portfolio --------------------------------------------------------------------

    private async Task TwoFrequentRisksAsync()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await Quantify(1, FrequentFrequency, Moderate, seed: 11);
        await Quantify(2, FrequentFrequency, Moderate, seed: 22);
    }

    /// <summary>P1 (T187) — no correlation declared: E[L] is the sum, P95 is not, and the independence is said.</summary>
    [Fact]
    public async Task TestP1_AZeroCorrelationPortfolioIsNotTheSumOfP95s()
    {
        await TwoFrequentRisksAsync();

        var portfolio = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { RiskIds = [1, 2] });

        Assert.Equal(PortfolioDependence.AssumedIndependent, portfolio.Dependence);
        Assert.Equal(0, portfolio.DeclaredPairs);
        Assert.Equal(portfolio.SumOfExpectedLoss, portfolio.ExpectedLoss, 1e-9 * portfolio.SumOfExpectedLoss);
        Assert.True(portfolio.P95 < 0.9 * portfolio.SumOfP95, $"P95 {portfolio.P95:N0} vs Σ {portfolio.SumOfP95:N0}");
        Assert.True(portfolio.Cvar95 <= portfolio.SumOfCvar95);
        Assert.True(portfolio.Diversification > 0);
        Assert.Equal(portfolio.Cvar95, portfolio.Members.Sum(m => m.Cvar95Contribution), 1e-9 * portfolio.Cvar95);
        Assert.Equal(10_000, portfolio.Iterations);
        Assert.All(portfolio.Members, m => Assert.Equal(2_000, m.StoredIterations));
    }

    /// <summary>P2 — a declared correlation of 1 makes the scenarios comonotonic: the portfolio P95 is the sum.</summary>
    [Fact]
    public async Task TestP2_PerfectCorrelationMakesTheP95TheSum()
    {
        await TwoFrequentRisksAsync();
        await Svc.SaveCorrelationAsync(Pair(1, 2, 1m), Author);

        var portfolio = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { RiskIds = [1, 2] });

        Assert.Equal(PortfolioDependence.Declared, portfolio.Dependence);
        Assert.Equal(1, portfolio.DeclaredPairs);
        Assert.Equal(portfolio.SumOfP95, portfolio.P95, 1e-9 * portfolio.SumOfP95);
    }

    /// <summary>
    /// P3 — a risk without statistics is listed, never summed as zero; with a portfolio tolerance the quantified part
    /// within it is not assessable (incomplete coverage), and above it is a conclusive excess.
    /// </summary>
    [Fact]
    public async Task TestP3_AnUnquantifiedRiskIsListedAndCoverageGovernsGateB()
    {
        await TwoFrequentRisksAsync();
        AddRisk(3, UnitA);
        Appetite(maxPortfolioCvar: 1_000_000_000);

        var portfolio = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest());

        var excluded = Assert.Single(portfolio.NotQuantified);
        Assert.Equal((3, PortfolioExclusionReason.NotQuantified), (excluded.RiskId, excluded.Reason));
        Assert.Equal(2, portfolio.Members.Count);
        Assert.Equal(TailAppetiteState.NotAssessable, portfolio.Appetite.State);
        Assert.Equal([TailAppetiteNotAssessableReason.IncompleteCoverage], portfolio.Appetite.Reasons);
        Assert.Equal((2, 3), (portfolio.Appetite.QuantifiedRisks!.Value, portfolio.Appetite.TotalRisks!.Value));

        SeedUnscoped(ctx => ctx.RiskAppetiteTailLimits.Single().MaxPortfolioCvar95 = 1);
        var exceeded = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest());
        Assert.Equal(TailAppetiteState.ExceedsTolerance, exceeded.Appetite.State);
    }

    /// <summary>P4 — scope: the default portfolio of a scoped caller holds only the risks it can see.</summary>
    [Fact]
    public async Task TestP4_ThePortfolioIsScoped()
    {
        await TwoFrequentRisksAsync();
        AddRisk(5, UnitB);
        await Quantify(5, FrequentFrequency, Moderate);

        ScopeTo(UnitA);
        var portfolio = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest());

        Assert.Equal([1, 2], portfolio.Members.Select(m => m.RiskId));
        Assert.Empty(portfolio.NotQuantified);
    }

    /// <summary>P5 — a fixed seed reproduces the aggregate; another seed changes the tail, never Σ E[L].</summary>
    [Fact]
    public async Task TestP5_AFixedSeedReproducesTheAggregate()
    {
        await TwoFrequentRisksAsync();
        await Svc.SaveCorrelationAsync(Pair(1, 2, 0.4m), Author);

        var first = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { Seed = 5 });
        var again = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { Seed = 5, RiskIds = [2, 1] });
        var other = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { Seed = 6 });

        Assert.Equal((first.P95, first.Cvar95, first.Cvar95CiLow), (again.P95, again.Cvar95, again.Cvar95CiLow));
        Assert.NotEqual(first.P95, other.P95);
        Assert.Equal(first.SumOfExpectedLoss, other.SumOfExpectedLoss);
    }

    /// <summary>P6 — the inherent basis aggregates the inherent runs even where a residual exists.</summary>
    [Fact]
    public async Task TestP6_TheBasisSelectsTheRun()
    {
        AddRisk(1, UnitA);
        AddMitigation(10, 1, percent: 50);
        await Quantify(1, FrequentFrequency, Moderate);

        var residual = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest { RiskIds = [1] });
        var inherent = await Svc.AggregatePortfolioAsync(new PortfolioTailRequest
            { RiskIds = [1], Basis = PortfolioBasis.Inherent });

        Assert.Equal(TailRun.Residual, residual.Members.Single().Run);
        Assert.Equal(TailRun.Inherent, inherent.Members.Single().Run);
        Assert.Equal(inherent.ExpectedLoss * 0.5, residual.ExpectedLoss, 1e-6 * inherent.ExpectedLoss);
    }

    /// <summary>P7 — the aggregation writes nothing: no row, no audit entry.</summary>
    [Fact]
    public async Task TestP7_TheAggregationWritesNothing()
    {
        await TwoFrequentRisksAsync();
        var before = Read(ctx => (ctx.RiskTailStatistics.Count(), ctx.AuditLogs.Count(), ctx.RiskCorrelations.Count()));

        await Svc.AggregatePortfolioAsync(new PortfolioTailRequest());

        Assert.Equal(before, Read(ctx => (ctx.RiskTailStatistics.Count(), ctx.AuditLogs.Count(), ctx.RiskCorrelations.Count())));
    }

    // --- C1–C4: correlations ---------------------------------------------------------------------

    /// <summary>C1 — the pair is normalized (smaller id first) and a second declaration updates the same row.</summary>
    [Fact]
    public async Task TestC1_ThePairIsNormalizedAndUpdated()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);

        var first = await Svc.SaveCorrelationAsync(Pair(2, 1, 0.5m), Author);
        var second = await Svc.SaveCorrelationAsync(Pair(1, 2, 0.6m, "Same supplier, confirmed."), Author);

        Assert.Equal((1, 2), (first.RiskAId, first.RiskBId));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(0.6m, second.Coefficient);
        Assert.NotNull(second.UpdatedAt);
        Assert.Single(await Svc.GetCorrelationsAsync(2));
    }

    /// <summary>C2 — a pair with a risk outside the caller's scope is invisible, and cannot be deleted by that caller.</summary>
    [Fact]
    public async Task TestC2_APairWithAnInvisibleRiskIsInvisible()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitB);
        await Svc.SaveCorrelationAsync(Pair(1, 2, 0.2m), Author);
        var hidden = await Svc.SaveCorrelationAsync(Pair(1, 3, 0.2m), Author);

        ScopeTo(UnitA);
        Assert.Equal([(1, 2)], (await Svc.GetCorrelationsAsync(null)).Select(c => (c.RiskAId, c.RiskBId)));
        Assert.Single(await Svc.GetCorrelationsAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteCorrelationAsync(hidden.Id, Author));
        Assert.Equal(2, Read(ctx => ctx.RiskCorrelations.Count()));
    }

    /// <summary>C3 — deleting a correlation removes it; deleting it again is not found.</summary>
    [Fact]
    public async Task TestC3_DeletingACorrelation()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var pair = await Svc.SaveCorrelationAsync(Pair(1, 2, 0.2m), Author);

        await Svc.DeleteCorrelationAsync(pair.Id, Author);

        Assert.Empty(await Svc.GetCorrelationsAsync(null));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteCorrelationAsync(pair.Id, Author));
    }

    /// <summary>
    /// C4 — the declared inputs are audited with the person who declared them, and appear on the risk's trail and in
    /// the evidence pack's changes; the computed statistics are not audited.
    /// </summary>
    [Fact]
    public async Task TestC4_TheDeclaredInputsAreAuditedWithThePerson()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await Quantify(1, FrequentFrequency, Moderate);
        await Svc.SaveCorrelationAsync(Pair(1, 2, 0.3m), Author);
        await Svc.SaveLossComponentsAsync(1, Components(Component(LossComponent.Response, 0, 10_000, 50_000)), Author);

        Assert.All(Audit(nameof(RiskCorrelation)), a => Assert.Equal(Author, a.UserId));
        Assert.NotEmpty(Audit(nameof(RiskCorrelation)));
        Assert.All(Audit(nameof(RiskLossComponent)), a => Assert.Equal(Author, a.UserId));
        Assert.NotEmpty(Audit(nameof(RiskLossComponent)));
        Assert.Empty(Audit(nameof(RiskTailStatistics)));

        var trail = await GetService<IAuditTrailService>().GetForRiskAsync(1);
        Assert.Contains(trail, a => a.EntityType == nameof(RiskCorrelation));
        Assert.Contains(trail, a => a.EntityType == nameof(RiskLossComponent));

        var period = await GetService<IAuditTrailService>().GetForEntityPeriodAsync(UnitA, DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));
        Assert.Contains(period, a => a.EntityType == nameof(RiskCorrelation));
    }

    // --- L1–L3: the appetite's tolerances --------------------------------------------------------

    /// <summary>L1 — invalid tolerances are refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("negative", "MaxScenarioP95")]
    [InlineData("too-large", "MaxPortfolioCvar95")]
    [InlineData("none", "Limits")]
    [InlineData("no-rationale", "Rationale")]
    [InlineData("long-rationale", "Rationale")]
    public async Task TestL1_InvalidTolerancesAreRefused(string scenario, string field)
    {
        Appetite();

        var request = scenario switch
        {
            "negative" => new RiskAppetiteTailLimitsRequest { MaxScenarioP95 = -1, Rationale = "x" },
            "too-large" => new RiskAppetiteTailLimitsRequest { MaxPortfolioCvar95 = 2_000_000_000_000m, Rationale = "x" },
            "none" => new RiskAppetiteTailLimitsRequest { Rationale = "x" },
            "no-rationale" => new RiskAppetiteTailLimitsRequest { MaxScenarioCvar95 = 1 },
            _ => new RiskAppetiteTailLimitsRequest { MaxScenarioCvar95 = 1, Rationale = new string('x', 2001) }
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.SaveAppetiteLimitsAsync(GlobalAppetite, request, Cro));
        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, Read(ctx => ctx.RiskAppetiteTailLimits.Count()));
    }

    /// <summary>L2 — save, read, update in place, delete; reading or deleting absent limits is not found.</summary>
    [Fact]
    public async Task TestL2_SaveReadUpdateAndDeleteTheTolerances()
    {
        Appetite();

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetAppetiteLimitsAsync(GlobalAppetite));

        var saved = await Svc.SaveAppetiteLimitsAsync(GlobalAppetite, new RiskAppetiteTailLimitsRequest
            { MaxScenarioCvar95 = 2_000_000, MaxPortfolioP95 = 10_000_000, Rationale = " Board minute 2026/07. " }, Cro);
        Assert.Equal("Board minute 2026/07.", saved.Rationale);
        Assert.Equal(Cro, saved.UpdatedById);

        var raised = await Svc.SaveAppetiteLimitsAsync(GlobalAppetite, new RiskAppetiteTailLimitsRequest
            { MaxScenarioCvar95 = 3_000_000, Rationale = "Board minute 2026/09." }, Cro);
        Assert.Null(raised.MaxPortfolioP95);
        Assert.Equal(1, Read(ctx => ctx.RiskAppetiteTailLimits.Count()));
        Assert.Equal(3_000_000m, (await Svc.GetAppetiteLimitsAsync(GlobalAppetite)).MaxScenarioCvar95);
        Assert.Contains(Audit(nameof(RiskAppetiteTailLimit)), a => a.Field == nameof(RiskAppetiteTailLimit.MaxScenarioCvar95)
                                                                && a.UserId == Cro);

        await Svc.DeleteAppetiteLimitsAsync(GlobalAppetite, Cro);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetAppetiteLimitsAsync(GlobalAppetite));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteAppetiteLimitsAsync(GlobalAppetite, Cro));
    }

    /// <summary>L3 — an appetite that does not exist is not found for every operation.</summary>
    [Fact]
    public async Task TestL3_AMissingAppetiteIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetAppetiteLimitsAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveAppetiteLimitsAsync(99,
            new RiskAppetiteTailLimitsRequest { MaxScenarioCvar95 = 1, Rationale = "x" }, Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteAppetiteLimitsAsync(99, Cro));
    }
}
