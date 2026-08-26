using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestones 8.2 (inherent vs. residual) and 8.7.2 (FAIR-lite quantitative scoring).
///
/// The distinction these guard is the one the milestone is about: a null residual means nobody has
/// assessed the treatment, and a residual equal to the inherent score means the treatment achieves
/// nothing. Collapsing the two would make "is this control working" unanswerable from the data.
/// </summary>
[TestSubject(typeof(MitigationPercentResidualStrategy))]
public class ResidualAndQuantitativeInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskCalculationService _calculation;
    private readonly IQuantitativeRiskService _quantitative;

    public ResidualAndQuantitativeInMemoryTest()
    {
        _calculation = GetService<IRiskCalculationService>();
        _quantitative = GetService<IQuantitativeRiskService>();
    }

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Mitigation NewMitigation(int id, int riskId, int percent) => new()
    {
        Id = id, RiskId = riskId, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
        MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = percent,
        CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
        SecurityRecommendations = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningDate = new DateOnly(2026, 6, 1)
    };

    // --- 8.2.1 the residual pass ------------------------------------------------------------------

    [Fact]
    public async Task TestARiskWithNoTreatmentGetsANullResidualNotAnEqualOne()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 8f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        await _calculation.CalculateResidualRiskAsync();

        await using var db = OpenContext();
        Assert.Null(db.RiskScorings.Single(s => s.Id == 1).ResidualRisk);
    }

    [Fact]
    public async Task TestResidualIsTheInherentScoreReducedByTheTreatmentPercentage()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 8f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(NewMitigation(1, 1, percent: 25));
        });

        var changed = await _calculation.CalculateResidualRiskAsync();

        Assert.Equal(1, changed);

        await using var db = OpenContext();
        Assert.Equal(6f, db.RiskScorings.Single(s => s.Id == 1).ResidualRisk!.Value, 3);
    }

    /// <summary>
    /// The arithmetic that makes a residual number believable. Three 40% controls do not remove 120%
    /// of a risk — they compose as independent reducers, so the score approaches zero and never
    /// reaches it. A treated risk is not an absent one.
    /// </summary>
    [Fact]
    public async Task TestControlsComposeAsIndependentReducersRatherThanAddingUp()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 10f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(NewMitigation(1, 1, percent: 0));
            ctx.MitigationToControls.Add(new MitigationToControl
                { MitigationId = 1, ControlId = 1, ValidationMitigationPercent = 40 });
            ctx.MitigationToControls.Add(new MitigationToControl
                { MitigationId = 1, ControlId = 2, ValidationMitigationPercent = 40 });
            ctx.MitigationToControls.Add(new MitigationToControl
                { MitigationId = 1, ControlId = 3, ValidationMitigationPercent = 40 });
        });

        await _calculation.CalculateResidualRiskAsync();

        await using var db = OpenContext();
        var residual = db.RiskScorings.Single(s => s.Id == 1).ResidualRisk!.Value;

        // 10 × 0.6³ = 2.16, not 10 × (1 − 1.2) = −2.
        Assert.Equal(2.16f, residual, 2);
        Assert.True(residual > 0);
    }

    [Fact]
    public async Task TestATreatmentClaimingOverAHundredPercentStillLeavesANonNegativeResidual()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 9f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(NewMitigation(1, 1, percent: 250));
        });

        await _calculation.CalculateResidualRiskAsync();

        await using var db = OpenContext();
        Assert.Equal(0f, db.RiskScorings.Single(s => s.Id == 1).ResidualRisk!.Value, 3);
    }

    [Fact]
    public async Task TestAHistoryRowIsWrittenOnlyWhenTheResidualActuallyMoves()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 8f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(NewMitigation(1, 1, percent: 50));
        });

        await _calculation.CalculateResidualRiskAsync();
        await _calculation.CalculateResidualRiskAsync();

        await using var db = OpenContext();

        // One row, not two: the job runs on a schedule and an identical row every run would bury
        // the changes that matter.
        var history = db.RiskScoringHistories.Where(h => h.RiskId == 1).ToList();
        Assert.Single(history);
        Assert.Equal(4f, history[0].ResidualRisk!.Value, 3);
    }

    [Fact]
    public void TestTheStrategyIsSelectedByName()
    {
        var strategy = new MitigationPercentResidualStrategy();
        Assert.Equal(MitigationPercentResidualStrategy.StrategyName, strategy.Name);
    }

    // --- 8.2.2 both scores side by side -----------------------------------------------------------

    [Fact]
    public async Task TestScorePairsReportTheDelta()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 1, ScoringMethod = 1, CalculatedRisk = 8f, ResidualRisk = 3f,
                ClassicImpact = 3, ClassicLikelihood = 3
            });
        });

        var pairs = await GetService<IRisksService>().GetScorePairsAsync();

        var pair = Assert.Single(pairs);
        Assert.Equal(8f, pair.Inherent, 3);
        Assert.Equal(3f, pair.Residual!.Value, 3);
        Assert.Equal(5f, pair.Delta!.Value, 3);
    }

    [Fact]
    public void TestTheDeltaIsNullWhenTheResidualIsUnassessed()
    {
        var pair = new RiskScorePair { RiskId = 1, Inherent = 8f, Residual = null };
        Assert.Null(pair.Delta);
    }

    // --- 8.2.2 the cadence setting ----------------------------------------------------------------

    /// <summary>
    /// The setting the spec says "hints at the concept" was in fact deleted in db_version 29 and never
    /// did anything while it existed. Version 80 re-creates it and <c>GetRiskReviewLevelAsync</c> now
    /// reads it.
    ///
    /// Asserted on the score selection rather than end-to-end because both <c>risk_levels</c> and
    /// <c>review_levels</c> are keyless entities that the EF in-memory provider refuses to track. The
    /// full path over real lookup tables is covered by
    /// <c>DAL.IntegrationTests.Track8CadenceBasisTests</c>.
    /// </summary>
    [Theory]
    [InlineData("InherentRisk", 9.5f)]
    [InlineData("ResidualRisk", 1.0f)]
    [InlineData(null, 9.5f)]
    [InlineData("something-else", 9.5f)]
    public void TestTheCadenceSettingSelectsWhichScoreDrivesTheReviewInterval(string? setting,
        float expected)
    {
        var scoring = new RiskScoring
        {
            Id = 1, ScoringMethod = 1, CalculatedRisk = 9.5f, ResidualRisk = 1.0f,
            ClassicImpact = 3, ClassicLikelihood = 3
        };

        Assert.Equal(expected, MgmtReviewsService.SelectCadenceScore(scoring, setting), 3);
    }

    /// <summary>
    /// Residual only when it has actually been computed. Falling back to inherent is the safe
    /// direction: a null residual would resolve to the lowest band and hand an untreated risk the
    /// longest review interval in the table.
    /// </summary>
    [Fact]
    public void TestAnUnassessedResidualFallsBackToInherentEvenWhenResidualIsSelected()
    {
        var scoring = new RiskScoring
        {
            Id = 1, ScoringMethod = 1, CalculatedRisk = 9.5f, ResidualRisk = null,
            ClassicImpact = 3, ClassicLikelihood = 3
        };

        Assert.Equal(9.5f,
            MgmtReviewsService.SelectCadenceScore(scoring, MgmtReviewsService.CadenceBasisResidual), 3);
    }

    // --- 8.7.2 quantitative ------------------------------------------------------------------------

    [Fact]
    public async Task TestQuantitativeScoringProducesOrderedPercentilesAndACurve()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        var result = await _quantitative.ComputeAndSaveAsync(1, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 1, LossEventFrequencyMostLikely = 2, LossEventFrequencyMax = 4,
            LossMagnitudeMin = 10_000, LossMagnitudeMostLikely = 50_000, LossMagnitudeMax = 500_000,
            Iterations = 2000, Seed = 42
        });

        Assert.True(result.InherentP10 <= result.InherentP50,
            $"P10 {result.InherentP10} > P50 {result.InherentP50}");
        Assert.True(result.InherentP50 <= result.InherentP90,
            $"P50 {result.InherentP50} > P90 {result.InherentP90}");
        Assert.NotEmpty(result.LossExceedanceCurve);
        Assert.Equal(42, result.Seed);

        await using var db = OpenContext();
        var scoring = db.RiskScorings.Single(s => s.Id == 1);

        Assert.Equal(QuantitativeRiskService.QuantitativeScoringMethod, scoring.ScoringMethod);
        Assert.NotNull(scoring.QuantComputedAt);
        Assert.NotNull(scoring.QuantLossExceedanceCurve);

        // The bridge back to the rest of the product: a quantitatively scored risk still carries a
        // 0-10 score, so lists, heatmaps and appetite rules keep working.
        Assert.True(scoring.CalculatedRisk is > 0 and <= 10,
            $"mapped score was {scoring.CalculatedRisk}");
    }

    /// <summary>
    /// The reason the mapped score comes from the mean rather than the median.
    ///
    /// A risk that happens once a decade and costs ten million has a median year of zero — most years
    /// nothing happens — so a P50-derived score would call it harmless. That is exactly the class of
    /// risk a quantitative method exists to surface, so the mapping uses the mean annualized loss.
    /// </summary>
    [Fact]
    public async Task TestALowFrequencyHighImpactRiskDoesNotScoreZero()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        var result = await _quantitative.ComputeAndSaveAsync(1, new QuantitativeRiskInput
        {
            // Roughly once a decade.
            LossEventFrequencyMin = 0.02, LossEventFrequencyMostLikely = 0.1, LossEventFrequencyMax = 0.3,
            LossMagnitudeMin = 2_000_000, LossMagnitudeMostLikely = 8_000_000,
            LossMagnitudeMax = 20_000_000,
            Iterations = 5000, Seed = 3
        });

        // Most years really are loss-free, so the median is zero and that is the honest number...
        Assert.Equal(0, result.InherentP50, 6);

        // ...but the risk is not harmless, and the mapped score says so.
        Assert.True(result.MappedScore > 4,
            $"a once-a-decade eight-million-pound risk mapped to {result.MappedScore}");
    }

    /// <summary>Same seed, same answer — an auditor has to be able to recompute a stated number.</summary>
    [Fact]
    public async Task TestTheSimulationIsReproducibleForAGivenSeed()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        var input = new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 0.2, LossEventFrequencyMostLikely = 1, LossEventFrequencyMax = 3,
            LossMagnitudeMin = 5_000, LossMagnitudeMostLikely = 25_000, LossMagnitudeMax = 200_000,
            Iterations = 2000, Seed = 7
        };

        var first = await _quantitative.ComputeAndSaveAsync(1, input);
        var second = await _quantitative.ComputeAndSaveAsync(1, input);

        Assert.Equal(first.InherentP50, second.InherentP50, 6);
        Assert.Equal(first.InherentP90, second.InherentP90, 6);
    }

    [Fact]
    public async Task TestAMitigationProducesALowerResidualLossExposure()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(NewMitigation(1, 1, percent: 60));
        });

        var result = await _quantitative.ComputeAndSaveAsync(1, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 1, LossEventFrequencyMostLikely = 2, LossEventFrequencyMax = 4,
            LossMagnitudeMin = 10_000, LossMagnitudeMostLikely = 100_000, LossMagnitudeMax = 400_000,
            Iterations = 3000, Seed = 11
        });

        Assert.NotNull(result.ResidualP50);
        Assert.True(result.ResidualP50 < result.InherentP50);
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(3, 1, 2)]
    [InlineData(1, 2, 1)]
    public async Task TestAnUnorderedRangeIsRefused(double min, double likely, double max)
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _quantitative.ComputeAndSaveAsync(1, new QuantitativeRiskInput
            {
                LossEventFrequencyMin = min,
                LossEventFrequencyMostLikely = likely,
                LossEventFrequencyMax = max,
                LossMagnitudeMin = 1000, LossMagnitudeMostLikely = 2000, LossMagnitudeMax = 3000,
                Iterations = 1000
            }));
    }

    [Fact]
    public async Task TestGetReturnsNullForARiskNeverScoredQuantitatively()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

        Assert.Null(await _quantitative.GetAsync(1));
    }

    // --- band mapping ------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5_000, 2)]
    [InlineData(10_000, 4)]
    [InlineData(100_000, 7)]
    [InlineData(1_000_000, 7)]
    public void TestMonetaryLossMapsOntoTheExistingZeroToTenScale(double loss, double atLeast)
    {
        var score = QuantitativeRiskService.MapToScore(loss,
            QuantitativeRiskService.DefaultBandThresholds);

        Assert.True(score >= atLeast, $"{loss:N0} mapped to {score}, expected at least {atLeast}");
        Assert.True(score <= 10);
    }

    /// <summary>The mapping is monotonic — a worse loss must never sort lower.</summary>
    [Fact]
    public void TestTheBandMappingIsMonotonic()
    {
        double[] losses = [0, 1_000, 9_999, 10_000, 50_000, 100_000, 500_000, 1_000_000, 10_000_000,
            100_000_000];

        var scores = losses
            .Select(l => QuantitativeRiskService.MapToScore(l, QuantitativeRiskService.DefaultBandThresholds))
            .ToList();

        for (var i = 1; i < scores.Count; i++)
            Assert.True(scores[i] >= scores[i - 1],
                $"{losses[i]:N0} scored {scores[i]} but {losses[i - 1]:N0} scored {scores[i - 1]}");
    }
}
