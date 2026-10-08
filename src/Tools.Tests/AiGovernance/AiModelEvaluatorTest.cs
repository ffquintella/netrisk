using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.AiGovernance;
using Tools.AiGovernance;
using Xunit;

namespace Tools.Tests.AiGovernance;

/// <summary>
/// Stage 9.12 (S53 §4.3–§4.4, §8 EV1–EV8) — the evaluation of a model's current version. EV1 is the edge case T215 names: a
/// model with no recorded evaluation is not treated as evaluated — no state, no value, no pass by omission.
/// </summary>
[TestSubject(typeof(AiModelEvaluator))]
public class AiModelEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static AiReadingFacts Reading(int id, AiModelMetric metric, decimal value = 0.9m, string version = "2.0",
        int daysAgo = 1, bool voided = false) => new(id, metric, value, version, Now.AddDays(-daysAgo), voided);

    private static AiModelEvaluationDto Evaluate(params AiReadingFacts[] readings) =>
        AiModelEvaluator.Evaluate("2.0", null, AiModelRiskTier.Minimal, AiHumanOversight.NoReview, 90, readings, Now);

    /// <summary>
    /// EV1 (T215) — no reading at all: the model and each of the six metrics are not evaluated, with no value, no date and no
    /// reading — never zero, never a pass — whatever the tier and oversight.
    /// </summary>
    [Theory]
    [InlineData(AiModelRiskTier.Minimal, AiHumanOversight.NoReview)]
    [InlineData(AiModelRiskTier.High, AiHumanOversight.EveryOutput)]
    [InlineData(null, null)]
    public void TestEV1_NoRecordedEvaluationIsNotEvaluated(AiModelRiskTier? tier, AiHumanOversight? oversight)
    {
        var evaluation = AiModelEvaluator.Evaluate("2.0", null, tier, oversight, 90, [], Now);

        Assert.Equal(AiModelEvaluationState.NotEvaluated, evaluation.State);
        Assert.Equal(6, evaluation.Metrics.Count);
        Assert.All(evaluation.Metrics, m => Assert.True(m is
            { State: AiMetricState.NotEvaluated, Value: null, MeasuredAt: null, ReadingId: null, AgeDays: null }));
        Assert.NotEmpty(evaluation.RequiredMetrics);
    }

    /// <summary>EV2 — the overall state never reads evaluated from an empty requirement, the case RequiredMetrics never returns.</summary>
    [Fact]
    public void TestEV2_AnEmptyRequirementIsNotEvaluated()
    {
        Assert.Equal(AiModelEvaluationState.NotEvaluated, AiModelEvaluator.Overall([]));
        Assert.Equal(AiModelEvaluationState.NotEvaluated, AiModelEvaluator.Overall([AiMetricState.NotEvaluated]));
        Assert.Equal(AiModelEvaluationState.Incomplete, AiModelEvaluator.Overall([AiMetricState.Evaluated, AiMetricState.NotEvaluated]));
        Assert.Equal(AiModelEvaluationState.Incomplete, AiModelEvaluator.Overall([AiMetricState.Stale, AiMetricState.NotEvaluated]));
        Assert.Equal(AiModelEvaluationState.Stale, AiModelEvaluator.Overall([AiMetricState.Evaluated, AiMetricState.Stale]));
        Assert.Equal(AiModelEvaluationState.Evaluated, AiModelEvaluator.Overall([AiMetricState.Evaluated, AiMetricState.Evaluated]));
    }

    /// <summary>EV3 — the required metrics, proportional to the tier; undeclared tier is high, undeclared oversight is reviewed.</summary>
    [Fact]
    public void TestEV3_TheRequiredMetricsAreProportionalToTheRisk()
    {
        Assert.Equal([AiModelMetric.Drift], AiModelEvaluator.RequiredMetrics(AiModelRiskTier.Minimal, AiHumanOversight.NoReview));
        Assert.Equal([AiModelMetric.Accuracy, AiModelMetric.Drift],
            AiModelEvaluator.RequiredMetrics(AiModelRiskTier.Limited, AiHumanOversight.NoReview));
        var high = new[]
        {
            AiModelMetric.Accuracy, AiModelMetric.Precision, AiModelMetric.Recall, AiModelMetric.Calibration, AiModelMetric.Drift
        };
        Assert.Equal(high, AiModelEvaluator.RequiredMetrics(AiModelRiskTier.High, AiHumanOversight.NoReview));
        Assert.Equal(high, AiModelEvaluator.RequiredMetrics(null, AiHumanOversight.NoReview));
        Assert.Equal([.. high, AiModelMetric.HumanOverrideRate],
            AiModelEvaluator.RequiredMetrics(AiModelRiskTier.High, AiHumanOversight.Sampled));
        Assert.Equal([AiModelMetric.Drift, AiModelMetric.HumanOverrideRate],
            AiModelEvaluator.RequiredMetrics(AiModelRiskTier.Minimal, null));

        // Drift is required of every model: no combination leaves the requirement empty.
        foreach (var tier in new AiModelRiskTier?[] { null, AiModelRiskTier.Minimal, AiModelRiskTier.Limited, AiModelRiskTier.High })
        foreach (var oversight in new AiHumanOversight?[] { null, AiHumanOversight.EveryOutput, AiHumanOversight.Sampled, AiHumanOversight.NoReview })
            Assert.Contains(AiModelMetric.Drift, AiModelEvaluator.RequiredMetrics(tier, oversight));
    }

    /// <summary>EV4 — the latest live reading of the current version is the metric's value; a voided one is ignored.</summary>
    [Fact]
    public void TestEV4_TheLatestLiveReadingOfTheCurrentVersionCounts()
    {
        var evaluation = Evaluate(
            Reading(1, AiModelMetric.Drift, 0.10m, daysAgo: 10),
            Reading(2, AiModelMetric.Drift, 0.20m, daysAgo: 5),
            Reading(3, AiModelMetric.Drift, 0.30m, daysAgo: 1, voided: true));

        var drift = evaluation.Metrics.Single(m => m.Metric == AiModelMetric.Drift);
        Assert.Equal((AiMetricState.Evaluated, 0.20m, 2, 5), (drift.State, drift.Value!.Value, drift.ReadingId!.Value, drift.AgeDays!.Value));
        Assert.Equal(AiModelEvaluationState.Evaluated, evaluation.State);

        // Only voided readings: not evaluated.
        Assert.Equal(AiModelEvaluationState.NotEvaluated, Evaluate(Reading(4, AiModelMetric.Drift, voided: true)).State);
    }

    /// <summary>EV5 — a reading of an earlier version does not evaluate the current one; it names the last evaluated version.</summary>
    [Fact]
    public void TestEV5_AnEarlierVersionDoesNotEvaluateTheCurrentOne()
    {
        var evaluation = Evaluate(Reading(1, AiModelMetric.Drift, version: "1.9"));

        var drift = evaluation.Metrics.Single(m => m.Metric == AiModelMetric.Drift);
        Assert.Equal((AiMetricState.NotEvaluated, (decimal?)null, "1.9"), (drift.State, drift.Value, drift.LastEvaluatedVersion));
        Assert.Equal(AiModelEvaluationState.NotEvaluated, evaluation.State);

        // The comparison is exact: "2.0 " or "v2.0" is another version.
        Assert.Equal(AiModelEvaluationState.NotEvaluated, Evaluate(Reading(2, AiModelMetric.Drift, version: "v2.0")).State);
    }

    /// <summary>EV6 — staleness is checked before anything is read from a reading: past the maximum age it is stale; at it, evaluated.</summary>
    [Fact]
    public void TestEV6_AnOldReadingIsStaleNeverEvaluated()
    {
        Assert.Equal(AiModelEvaluationState.Evaluated, Evaluate(Reading(1, AiModelMetric.Drift, daysAgo: 90)).State);

        var stale = Evaluate(Reading(1, AiModelMetric.Drift, daysAgo: 91));
        Assert.Equal(AiModelEvaluationState.Stale, stale.State);
        Assert.Equal(AiMetricState.Stale, stale.Metrics.Single(m => m.Metric == AiModelMetric.Drift).State);
    }

    /// <summary>EV7 — a reading of a metric the tier does not require shows, but does not evaluate the model.</summary>
    [Fact]
    public void TestEV7_ANonRequiredMetricDoesNotEvaluateTheModel()
    {
        var evaluation = Evaluate(Reading(1, AiModelMetric.Accuracy, 0.99m));

        var accuracy = evaluation.Metrics.Single(m => m.Metric == AiModelMetric.Accuracy);
        Assert.Equal((false, AiMetricState.Evaluated), (accuracy.Required, accuracy.State));
        Assert.Equal(AiModelEvaluationState.NotEvaluated, evaluation.State);
    }

    /// <summary>
    /// EV9 — a version is evaluated only by readings measured while it is in use: a version that went 2.0 → 2.1 → 2.0 does not
    /// count the readings of its first deployment, and says so through the last evaluated version.
    /// </summary>
    [Fact]
    public void TestEV9_ReadingsBeforeTheVersionStartDoNotCount()
    {
        var firstDeployment = Reading(1, AiModelMetric.Drift, 0.1m, daysAgo: 30);
        var since = Now.AddDays(-10);

        var evaluation = AiModelEvaluator.Evaluate("2.0", since, AiModelRiskTier.Minimal, AiHumanOversight.NoReview, 90,
            [firstDeployment], Now);
        var drift = evaluation.Metrics.Single(m => m.Metric == AiModelMetric.Drift);
        Assert.Equal((AiModelEvaluationState.NotEvaluated, AiMetricState.NotEvaluated, (decimal?)null, "2.0", since),
            (evaluation.State, drift.State, drift.Value, drift.LastEvaluatedVersion, evaluation.VersionSince));

        var afterwards = AiModelEvaluator.Evaluate("2.0", since, AiModelRiskTier.Minimal, AiHumanOversight.NoReview, 90,
            [firstDeployment, Reading(2, AiModelMetric.Drift, 0.2m, daysAgo: 10)], Now);
        Assert.Equal((AiModelEvaluationState.Evaluated, 0.2m),
            (afterwards.State, afterwards.Metrics.Single(m => m.Metric == AiModelMetric.Drift).Value!.Value));
    }

    /// <summary>EV8 — the ranges: a fraction in [0, 1] for every metric but drift, a statistic in [0, 1000].</summary>
    [Fact]
    public void TestEV8_TheValueRanges()
    {
        foreach (var metric in AiModelEvaluator.AllMetrics.Where(m => m != AiModelMetric.Drift))
        {
            Assert.True(AiModelEvaluator.IsInRange(metric, 0m));
            Assert.True(AiModelEvaluator.IsInRange(metric, 1m));
            Assert.False(AiModelEvaluator.IsInRange(metric, 1.000001m));
            Assert.False(AiModelEvaluator.IsInRange(metric, -0.000001m));
        }

        Assert.True(AiModelEvaluator.IsInRange(AiModelMetric.Drift, 3.5m));
        Assert.True(AiModelEvaluator.IsInRange(AiModelMetric.Drift, 1000m));
        Assert.False(AiModelEvaluator.IsInRange(AiModelMetric.Drift, 1000.1m));
        Assert.False(AiModelEvaluator.IsInRange(AiModelMetric.Drift, -1m));
        Assert.Equal(Enum.GetValues<AiModelMetric>(), AiModelEvaluator.AllMetrics);
    }
}
