using System;
using System.Linq;
using Model.Monitoring;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>A deterministic <see cref="IMethodologyMetricsService"/> for <c>MonitoringControllerTest</c> (Stage 9.8): the ten metrics M1..M10.</summary>
public static class MockedMethodologyMetricsService
{
    public static MethodologyMetricsDto Metrics() => new()
    {
        ComputedAt = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
        Metrics = Enum.GetValues<MethodologyMetric>().Select(metric => new MethodologyMetricDto
        {
            Code = $"M{(int)metric}", Metric = metric, Name = metric.ToString(), Availability = MetricAvailability.Available,
            Value = 1d, Detail = "Computed.", Stage = "9.8"
        }).ToList(),
        Kris = new KriHealthDto { Active = 3, WithinTolerance = 2, Breached = 1 },
        Reassessment = new ReassessmentHealthDto { EventsLast90Days = 2, Pending = 1, Answered = 1 }
    };

    public static IMethodologyMetricsService Create()
    {
        var service = Substitute.For<IMethodologyMetricsService>();
        service.GetAsync().Returns(_ => Metrics());
        return service;
    }
}
