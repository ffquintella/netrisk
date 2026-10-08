using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Monitoring;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.8 (S49 §7) — <see cref="MonitoringRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every body and query string it sends and every status branch runs for real. A refusal (a retired KRI, an incident that
/// already has its event) keeps the server's sentence.
/// </summary>
[TestSubject(typeof(MonitoringRestService))]
public class MonitoringRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IMonitoringService _service;

    public MonitoringRestServiceTest()
    {
        _service = ResolveWith<IMonitoringService>(_backend);
    }

    private static KriDto Kri(int id = 4) => new()
    {
        Id = id, Name = "Hours of unavailability", Category = KriCategory.Unavailability, Unit = "hours",
        Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m, WarningThreshold = 3m, LinkedRisks = 2,
        Status = new KriStatusDto { State = KriState.Breached, LastReadingBreached = true, Explanation = "Beyond tolerance." }
    };

    private static KriDetailDto Detail(int id = 4) => new()
    {
        Id = id, Name = "Hours of unavailability", Category = KriCategory.Unavailability, Unit = "hours",
        Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m,
        Status = new KriStatusDto { State = KriState.Warning, Explanation = "Past the warning." },
        Readings = [new KriReadingDto { Id = 7, KriId = id, Value = 4m, BeyondTolerance = false }],
        Risks = [new KriLinkedRiskDto { RiskId = 3, Subject = "Ransomware", Status = "New" }]
    };

    private static ReassessmentEventDto Event(int id = 9) => new()
    {
        Id = id, TriggerType = ReassessmentTriggerType.NewRegulation, Origin = ReassessmentEventOrigin.Declared,
        Title = "New regulation", SkippedClosedRiskIds = [8], AlreadyTriggeredRiskIds = [6],
        Triggers = [new ReassessmentTriggerDto { Id = 1, EventId = id, RiskId = 3, State = ReassessmentTriggerState.Pending }]
    };

    private static readonly KriRequest Definition = new()
    {
        Name = "Hours of unavailability", Category = KriCategory.Unavailability, Source = "Monitoring export",
        Unit = "hours", Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m, WarningThreshold = 3m,
        ToleranceRationale = "Phase 0 decision.", MaxReadingAgeDays = 31, OwnerId = 2, EntityId = 3
    };

    // --- KRIs ---------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheKrisAreListedWithTheRetiredOnlyWhenAsked()
    {
        _backend.OnGet("/Monitoring/Kris", new[] { Kri() });

        var active = await _service.GetKrisAsync();
        Assert.Equal((4, KriState.Breached), (active.Single().Id, active.Single().Status.State));
        Assert.True(_backend.Sent(Method.Get, "/Monitoring/Kris"));
        Assert.DoesNotContain("includeRetired", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetKrisAsync(false);
        Assert.DoesNotContain("includeRetired", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetKrisAsync(true);
        Assert.Contains("includeRetired=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestAKriIsReadWithItsReadingsAndRisks()
    {
        _backend.OnGet("/Monitoring/Kris/4", Detail());

        var kri = await _service.GetKriAsync(4);

        Assert.Equal(KriState.Warning, kri.Status.State);
        Assert.Equal(7, Assert.Single(kri.Readings).Id);
        Assert.Equal(3, Assert.Single(kri.Risks).RiskId);
        Assert.True(_backend.Sent(Method.Get, "/Monitoring/Kris/4"));
    }

    [Fact]
    public async Task TestAKriIsDefinedAndChangedWithItsBody()
    {
        _backend.OnPost("/Monitoring/Kris", Kri(5));
        _backend.OnPut("/Monitoring/Kris/5", Kri(5));

        var created = await _service.CreateKriAsync(Definition);

        Assert.Equal(5, created.Id);
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Kris"));
        Assert.Contains("\"toleranceThreshold\":5", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"warningThreshold\":3", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"toleranceRationale\":\"Phase 0 decision.\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"maxReadingAgeDays\":31", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"entityId\":3", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        var updated = await _service.UpdateKriAsync(5, Definition);

        Assert.Equal(5, updated.Id);
        Assert.True(_backend.Sent(Method.Put, "/Monitoring/Kris/5"));
        Assert.Contains("\"name\":\"Hours of unavailability\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestAKriIsRetiredWithoutABody()
    {
        _backend.OnPost("/Monitoring/Kris/4/Retire", Kri());

        var retired = await _service.RetireKriAsync(4);

        Assert.Equal(4, retired.Id);
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Kris/4/Retire"));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Body));
    }

    // --- readings -----------------------------------------------------------------------------

    [Fact]
    public async Task TestAReadingIsRecordedAndVoidedWithItsBody()
    {
        _backend.OnPost("/Monitoring/Kris/4/Readings", Detail());
        _backend.OnPost("/Monitoring/Kris/4/Readings/7/Void", Detail());

        var recorded = await _service.RecordReadingAsync(4, new KriReadingRequest
            { Value = 6.5m, ObservedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), Note = "Monthly export." });

        Assert.Equal(4, recorded.Id);
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Kris/4/Readings"));
        Assert.Contains("\"value\":6.5", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"note\":\"Monthly export.\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"observedAt\":\"2026-10-01", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        var voided = await _service.VoidReadingAsync(4, 7, new KriReadingVoidRequest { Reason = "Wrong source." });

        Assert.Equal(4, voided.Id);
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Kris/4/Readings/7/Void"));
        Assert.Contains("\"reason\":\"Wrong source.\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    // --- links --------------------------------------------------------------------------------

    [Fact]
    public async Task TestARiskIsLinkedAndUnlinked()
    {
        _backend.OnPut("/Monitoring/Kris/4/Risks/3", Detail());
        _backend.OnStatus(Method.Delete, "/Monitoring/Kris/4/Risks/3", HttpStatusCode.NoContent);

        var linked = await _service.LinkRiskAsync(4, 3);
        Assert.Equal(3, Assert.Single(linked.Risks).RiskId);
        Assert.True(_backend.Sent(Method.Put, "/Monitoring/Kris/4/Risks/3"));

        await _service.UnlinkRiskAsync(4, 3);
        Assert.True(_backend.Sent(Method.Delete, "/Monitoring/Kris/4/Risks/3"));
    }

    // --- reassessment events and triggers -----------------------------------------------------

    [Fact]
    public async Task TestTheEventsAreListedWithTheirFiltersOnlyWhenGiven()
    {
        _backend.OnGet("/Monitoring/Reassessment/Events", new[] { Event() });

        var all = await _service.GetEventsAsync();
        Assert.Equal((9, ReassessmentTriggerType.NewRegulation), (all.Single().Id, all.Single().TriggerType));
        Assert.Equal(8, Assert.Single(all.Single().SkippedClosedRiskIds));
        Assert.DoesNotContain("type", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetEventsAsync(ReassessmentTriggerType.NewAiModel);
        Assert.Contains("type=5", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetEventsAsync(limit: 25);
        Assert.Contains("limit=25", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetEventsAsync(ReassessmentTriggerType.NewDataOrKriBreach, 10);
        Assert.Contains("type=6", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit=10", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestAnEventIsDeclaredAndAppliedToMoreRisksWithItsBody()
    {
        _backend.OnPost("/Monitoring/Reassessment/Events", Event());
        _backend.OnPost("/Monitoring/Reassessment/Events/9/Risks", Event());

        var declared = await _service.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.SignificantIncidentOrNearMiss, Title = "Outage", IncidentId = 77, RiskIds = [3, 4]
        });

        Assert.Equal(9, declared.Id);
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Reassessment/Events"));
        Assert.Contains("\"type\":3", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"incidentId\":77", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"riskIds\":[3,4]", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        var added = await _service.AddEventRisksAsync(9, new ReassessmentRisksRequest { RiskIds = [3, 4] });

        Assert.Equal(6, Assert.Single(added.AlreadyTriggeredRiskIds));
        Assert.True(_backend.Sent(Method.Post, "/Monitoring/Reassessment/Events/9/Risks"));
        Assert.Contains("\"riskIds\":[3,4]", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTheTriggersAreListedWithTheirFiltersOnlyWhenGiven()
    {
        _backend.OnGet("/Monitoring/Reassessment/Triggers", new[]
        {
            new ReassessmentTriggerDto { Id = 1, EventId = 9, RiskId = 3, State = ReassessmentTriggerState.Pending }
        });

        var all = await _service.GetTriggersAsync();
        Assert.Equal((3, ReassessmentTriggerState.Pending), (all.Single().RiskId, all.Single().State));
        Assert.DoesNotContain("riskId", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pendingOnly", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetTriggersAsync(pendingOnly: false);
        Assert.DoesNotContain("pendingOnly", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetTriggersAsync(3);
        Assert.Contains("riskId=3", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pendingOnly", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetTriggersAsync(pendingOnly: true);
        Assert.Contains("pendingOnly=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("riskId", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetTriggersAsync(3, true);
        Assert.Contains("riskId=3", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pendingOnly=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    // --- metrics ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheMetricsPanelIsRead()
    {
        _backend.OnGet("/Monitoring/Metrics", new MethodologyMetricsDto
        {
            Metrics =
            [
                new MethodologyMetricDto
                {
                    Code = "M6", Metric = MethodologyMetric.KevRemediation, Availability = MetricAvailability.Partial,
                    Value = 12.5, Detail = "Some KEV items.", Stage = "9.4"
                }
            ],
            Kris = new KriHealthDto { Active = 3, Breached = 1 },
            Reassessment = new ReassessmentHealthDto { Pending = 2, MeanDaysToAnswer = 4.5 }
        });

        var panel = await _service.GetMetricsAsync();

        var metric = Assert.Single(panel.Metrics);
        Assert.Equal(("M6", MethodologyMetric.KevRemediation, MetricAvailability.Partial, 12.5),
            (metric.Code, metric.Metric, metric.Availability, metric.Value));
        Assert.Equal((3, 1), (panel.Kris.Active, panel.Kris.Breached));
        Assert.Equal((2, 4.5), (panel.Reassessment.Pending, panel.Reassessment.MeanDaysToAnswer));
        Assert.True(_backend.Sent(Method.Get, "/Monitoring/Metrics"));
    }

    // --- refusals -----------------------------------------------------------------------------

    [Fact]
    public async Task TestANullRequestIsRefusedBeforeAnyCall()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateKriAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateKriAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RecordReadingAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VoidReadingAsync(4, 7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DeclareEventAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AddEventRisksAsync(9, null!));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/Monitoring/Kris/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Put, "/Monitoring/Kris/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Post, "/Monitoring/Kris/99/Retire", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Post, "/Monitoring/Kris/99/Readings", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Post, "/Monitoring/Kris/4/Readings/99/Void", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Put, "/Monitoring/Kris/4/Risks/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Delete, "/Monitoring/Kris/4/Risks/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Post, "/Monitoring/Reassessment/Events/99/Risks", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Post, "/Monitoring/Reassessment/Events", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Get, "/Monitoring/Reassessment/Triggers", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetKriAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.UpdateKriAsync(99, Definition));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.RetireKriAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.RecordReadingAsync(99, new KriReadingRequest()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.VoidReadingAsync(4, 99, new KriReadingVoidRequest()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.LinkRiskAsync(4, 99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.UnlinkRiskAsync(4, 99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.AddEventRisksAsync(99, new ReassessmentRisksRequest()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeclareEventAsync(new ReassessmentEventRequest()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetTriggersAsync(99));
    }

    /// <summary>A retired KRI, an incident that already has its event, a validation error and a scope violation reach the person with the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "The KRI is retired: it takes no reading.")]
    [InlineData(HttpStatusCode.Conflict, "The incident already has its reassessment event.")]
    [InlineData(HttpStatusCode.BadRequest, "A reason is required.")]
    [InlineData(HttpStatusCode.Forbidden, "The KRI belongs to an entity you do not manage.")]
    public async Task TestARefusalKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.OnPost("/Monitoring/Kris/4/Readings", new { error = "refused", message = sentence }, status);
        _backend.OnPost("/Monitoring/Reassessment/Events", new { error = "refused", message = sentence }, status);

        var reading = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.RecordReadingAsync(4, new KriReadingRequest { Value = 1m }));
        Assert.Contains(sentence, reading.Message);

        var declared = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.DeclareEventAsync(new ReassessmentEventRequest
                { Type = ReassessmentTriggerType.SignificantIncidentOrNearMiss, IncidentId = 77 }));
        Assert.Contains(sentence, declared.Message);
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailure()
    {
        _backend.OnStatus(Method.Get, "/Monitoring/Metrics", HttpStatusCode.InternalServerError);
        _backend.OnStatus(Method.Get, "/Monitoring/Kris", HttpStatusCode.InternalServerError);
        _backend.OnStatus(Method.Post, "/Monitoring/Kris", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetMetricsAsync());
        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetKrisAsync());
        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.CreateKriAsync(Definition));
    }
}
