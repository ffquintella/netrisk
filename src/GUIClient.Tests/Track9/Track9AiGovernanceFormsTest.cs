using System;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.AiGovernance;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9AiGovernanceFormsTest
{
    [Fact]
    public void TestBuildRequestKeepsInventoryAndVersionState()
    {
        var source = new AiModelDto
        {
            Name = "Admissions model",
            Purpose = "rank applications",
            Kind = AiModelKind.Classification,
            Source = AiModelSource.Vendor,
            ThirdPartyId = 4,
            Version = "2.1",
            VersionSince = new DateTime(2026, 7, 1),
            Status = AiModelStatus.Pilot,
            RiskTier = AiModelRiskTier.High,
            HumanOversight = AiHumanOversight.EveryOutput,
            OwnerId = 5,
            EntityId = 6,
            MaxEvaluationAgeDays = 90,
            Notes = "governance notes"
        };

        var request = Track9AiGovernanceForms.BuildRequest(source);

        Assert.Equal("Admissions model", request.Name);
        Assert.Equal("rank applications", request.Purpose);
        Assert.Equal(AiModelKind.Classification, request.Kind);
        Assert.Equal(AiModelSource.Vendor, request.Source);
        Assert.Equal(4, request.ThirdPartyId);
        Assert.Equal("2.1", request.Version);
        Assert.Equal(new DateTime(2026, 7, 1), request.VersionSince);
        Assert.Equal(AiModelStatus.Pilot, request.Status);
        Assert.Equal(AiModelRiskTier.High, request.RiskTier);
        Assert.Equal(AiHumanOversight.EveryOutput, request.HumanOversight);
        Assert.Equal(5, request.OwnerId);
        Assert.Equal(6, request.EntityId);
        Assert.Equal(90, request.MaxEvaluationAgeDays);
        Assert.Equal("governance notes", request.Notes);
    }

    [Fact]
    public void TestHumanOverrideRateIsServerCalculated()
    {
        var request = Track9AiGovernanceForms.BuildReading(
            AiModelMetric.HumanOverrideRate,
            0.75m,
            new DateTime(2026, 9, 1),
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            400,
            "review log",
            "evidence-1");

        Assert.Null(request.Value);
        Assert.Null(request.MeasuredAt);
        Assert.Equal(400, request.SampleSize);
        Assert.Equal(new DateTime(2026, 8, 1), request.PeriodStart);
        Assert.Equal(new DateTime(2026, 8, 31), request.PeriodEnd);
    }

    [Fact]
    public void TestNotEvaluatedIsExplicitAndHasNoNumericValue()
    {
        var state = new AiModelMetricStateDto
        {
            Metric = AiModelMetric.Drift,
            Required = true,
            State = AiMetricState.NotEvaluated,
            Value = null,
            LastEvaluatedVersion = "1.0"
        };

        Assert.Equal("Track9AiMetricNotEvaluated", Track9AiGovernanceForms.MetricStateKey(state));
        Assert.Equal("1.0", Track9AiGovernanceForms.PreviousVersion(state));
    }
}
