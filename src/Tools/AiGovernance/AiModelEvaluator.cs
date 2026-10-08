using DAL.Enums;
using Model.AiGovernance;

namespace Tools.AiGovernance;

/// <summary>One metric reading, as the evaluation reads it.</summary>
public sealed record AiReadingFacts(int Id, AiModelMetric Metric, decimal Value, string ModelVersion, DateTime MeasuredAt,
    bool Voided);

/// <summary>
/// The evaluation of a model's current version (Stage 9.12, S53 §4.3–§4.4, T214, T215) — which metrics its declared risk
/// tier and oversight require, the state of each metric, and the state of the whole.
///
/// Three rules shape it. <b>Not evaluated is explicit</b>: a metric with no live reading for the current version is
/// <see cref="AiMetricState.NotEvaluated"/> with no value — never zero, never a pass — and a model whose required metrics
/// all lack one is <see cref="AiModelEvaluationState.NotEvaluated"/>; the required set is never empty, because drift is
/// required of every model, so no model is "evaluated" by having nothing asked of it (T215). <b>An evaluation is of a
/// version</b> (S53 D3): a reading of an earlier version, or of this version measured before it was in use, says nothing
/// about the current one. <b>Old is not current</b>: a reading older than the model's maximum evaluation age is
/// <see cref="AiMetricState.Stale"/>, checked before anything else is read from it, as a KRI's (S49 D2). Computed on read;
/// pure.
/// </summary>
public static class AiModelEvaluator
{
    /// <summary>The six metrics, in their stored order.</summary>
    public static IReadOnlyList<AiModelMetric> AllMetrics { get; } =
    [
        AiModelMetric.Accuracy, AiModelMetric.Precision, AiModelMetric.Recall, AiModelMetric.Calibration,
        AiModelMetric.Drift, AiModelMetric.HumanOverrideRate
    ];

    /// <summary>
    /// What a model must report, proportional to its declared risk (MIGR-TI/IA Phase 6: "tests, metrics and explainability
    /// proportional to the risk"; S53 §4.3, D6) — an <em>interpretation</em> for the organization's AI governance to confirm
    /// (S53 §11):
    /// <list type="bullet">
    /// <item>drift — every model: a model in use drifts whatever its tier;</item>
    /// <item>accuracy — limited and high;</item>
    /// <item>precision, recall and calibration — high;</item>
    /// <item>the human override rate — whenever people review outputs, because it is the measure of that oversight.</item>
    /// </list>
    /// An undeclared tier is read as high and undeclared oversight as reviewed: absent is never lenient (S53 D5).
    /// </summary>
    public static List<AiModelMetric> RequiredMetrics(AiModelRiskTier? tier, AiHumanOversight? oversight)
    {
        var effective = tier ?? AiModelRiskTier.High;

        var required = new List<AiModelMetric>();
        if (effective >= AiModelRiskTier.Limited) required.Add(AiModelMetric.Accuracy);
        if (effective == AiModelRiskTier.High)
            required.AddRange([AiModelMetric.Precision, AiModelMetric.Recall, AiModelMetric.Calibration]);
        required.Add(AiModelMetric.Drift);
        if (oversight != AiHumanOversight.NoReview) required.Add(AiModelMetric.HumanOverrideRate);

        return required;
    }

    /// <summary>Whether the value is in range for the metric: a fraction in [0, 1], or a drift statistic in [0, 1000].</summary>
    public static bool IsInRange(AiModelMetric metric, decimal value) =>
        value >= 0 && value <= (metric == AiModelMetric.Drift ? AiGovernanceLimits.MaxDriftValue : 1m);

    /// <param name="versionSince">
    /// Since when the current version is in use: a reading of it measured earlier — a version that went A → B → A — is of
    /// an earlier deployment and does not count. Null sets no lower bound.
    /// </param>
    public static AiModelEvaluationDto Evaluate(string currentVersion, DateTime? versionSince, AiModelRiskTier? tier,
        AiHumanOversight? oversight, int maxEvaluationAgeDays, IEnumerable<AiReadingFacts> readings, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(readings);

        var required = RequiredMetrics(tier, oversight);
        var live = readings.Where(r => !r.Voided).ToList();

        var metrics = AllMetrics.Select(metric =>
        {
            var ofMetric = live.Where(r => r.Metric == metric)
                .OrderByDescending(r => r.MeasuredAt).ThenByDescending(r => r.Id)
                .ToList();
            var current = ofMetric.FirstOrDefault(r =>
                string.Equals(r.ModelVersion, currentVersion, StringComparison.Ordinal) &&
                (versionSince is not { } since || r.MeasuredAt >= since));

            var state = new AiModelMetricStateDto { Metric = metric, Required = required.Contains(metric) };

            if (current is null)
            {
                state.State = AiMetricState.NotEvaluated;
                state.LastEvaluatedVersion = ofMetric.FirstOrDefault()?.ModelVersion;
                return state;
            }

            state.ReadingId = current.Id;
            state.Value = current.Value;
            state.MeasuredAt = current.MeasuredAt;
            state.AgeDays = System.Math.Max(0, (int)System.Math.Floor((now - current.MeasuredAt).TotalDays));
            state.State = now - current.MeasuredAt > TimeSpan.FromDays(maxEvaluationAgeDays)
                ? AiMetricState.Stale
                : AiMetricState.Evaluated;
            return state;
        }).ToList();

        return new AiModelEvaluationDto
        {
            Version = currentVersion,
            VersionSince = versionSince,
            RequiredMetrics = required,
            Metrics = metrics,
            State = Overall(metrics.Where(m => m.Required).Select(m => m.State).ToList())
        };
    }

    /// <summary>The whole from the required metrics' states. An empty list — which <see cref="RequiredMetrics"/> never
    /// returns — is not evaluated, never evaluated by omission.</summary>
    public static AiModelEvaluationState Overall(IReadOnlyCollection<AiMetricState> required)
    {
        if (required.Count == 0 || required.All(s => s == AiMetricState.NotEvaluated)) return AiModelEvaluationState.NotEvaluated;
        if (required.Any(s => s == AiMetricState.NotEvaluated)) return AiModelEvaluationState.Incomplete;
        if (required.Any(s => s == AiMetricState.Stale)) return AiModelEvaluationState.Stale;
        return AiModelEvaluationState.Evaluated;
    }

    /// <summary>The methodology's name of a metric, for messages.</summary>
    public static string Label(AiModelMetric metric) => metric switch
    {
        AiModelMetric.Accuracy => "accuracy",
        AiModelMetric.Precision => "precision",
        AiModelMetric.Recall => "recall",
        AiModelMetric.Calibration => "calibration",
        AiModelMetric.Drift => "drift",
        AiModelMetric.HumanOverrideRate => "human override rate",
        _ => metric.ToString()
    };
}
