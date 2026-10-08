using System;
using DAL.Enums;
using Model.AiGovernance;

namespace GUIClient.Tools.Track9;

/// <summary>Typed request builders and explicit evaluation-state presentation for Stage 9.12.</summary>
public static class Track9AiGovernanceForms
{
    public static AiModelRequest BuildRequest(AiModelDto source) => new()
    {
        Name = source.Name,
        Purpose = source.Purpose,
        Kind = source.Kind,
        Source = source.Source,
        ThirdPartyId = source.ThirdPartyId,
        Version = source.Version,
        VersionSince = source.VersionSince,
        Status = source.Status,
        RiskTier = source.RiskTier,
        HumanOversight = source.HumanOversight,
        OwnerId = source.OwnerId,
        EntityId = source.EntityId,
        MaxEvaluationAgeDays = source.MaxEvaluationAgeDays,
        Notes = source.Notes
    };

    public static AiModelReadingRequest BuildReading(AiModelMetric metric, decimal? value, DateTime? measuredAt,
        DateTime? periodStart, DateTime? periodEnd, int? sampleSize, string? method, string? evidenceReference) => new()
    {
        Metric = metric,
        Value = metric == AiModelMetric.HumanOverrideRate ? null : value,
        MeasuredAt = metric == AiModelMetric.HumanOverrideRate ? null : measuredAt,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        SampleSize = sampleSize,
        Method = method,
        EvidenceReference = evidenceReference
    };

    public static string MetricStateKey(AiModelMetricStateDto state) => state.State switch
    {
        AiMetricState.NotEvaluated => "Track9AiMetricNotEvaluated",
        AiMetricState.Evaluated => "Track9AiMetricEvaluated",
        AiMetricState.Stale => "Track9AiMetricStale",
        _ => "Track9AiMetricNotEvaluated"
    };

    public static string? PreviousVersion(AiModelMetricStateDto state) =>
        state.State == AiMetricState.NotEvaluated ? state.LastEvaluatedVersion : null;
}
