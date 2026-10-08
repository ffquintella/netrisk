using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.Monitoring;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.8 (S49 §6, §7) — <see cref="MonitoringController"/>: each of its fourteen actions' success shape, and the
/// mapping of every domain exception onto the status code the other controllers use for it. A cross-entity write is the
/// one exception that is not answered here: it is re-thrown for <c>EntityScopeViolationMiddleware</c> to make a 403.
/// </summary>
[TestSubject(typeof(MonitoringController))]
public class MonitoringControllerTest : BaseControllerTest
{
    private readonly MonitoringController _controller;

    private const int Known = MockedMonitoringService.Known;
    private const int Invalid = MockedMonitoringService.Invalid;
    private const int Scope = MockedMonitoringService.Scope;
    private const int Missing = MockedMonitoringService.Missing;
    private const int Conflict = MockedMonitoringService.Conflict;
    private const int Retired = MockedMonitoringService.Retired;
    private const int Broken = MockedMonitoringService.Broken;

    public MonitoringControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<MonitoringController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static void AssertServerError(ActionResult? result) =>
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result).StatusCode);

    private static readonly KriRequest Definition = new()
    {
        Name = "Hours of unavailability", Category = KriCategory.Unavailability, Source = "Monitoring export",
        Unit = "hours", Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m, WarningThreshold = 3m,
        ToleranceRationale = "Phase 0 decision.", MaxReadingAgeDays = 31, OwnerId = 2, EntityId = 3
    };

    private static readonly KriReadingRequest Reading = new() { Value = 2m, Note = "Monthly export." };
    private static readonly KriReadingVoidRequest Voiding = new() { Reason = "Wrong source." };
    private static readonly ReassessmentRisksRequest Risks = new() { RiskIds = [3, 4] };

    private static ReassessmentEventRequest Declared(int? incidentId = null) => new()
    {
        Type = ReassessmentTriggerType.SignificantIncidentOrNearMiss, Title = "Outage", IncidentId = incidentId, RiskIds = [4]
    };

    // --- success ------------------------------------------------------------------------------

    [Fact]
    public async Task TestReadsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _controller.GetKris()).Result);
        Assert.Equal(Known, Assert.Single(Assert.IsType<List<KriDto>>(list.Value)).Id);

        var detail = Assert.IsType<OkObjectResult>((await _controller.GetKri(Known)).Result);
        var kri = Assert.IsType<KriDetailDto>(detail.Value);
        Assert.Equal((Known, KriState.WithinTolerance), (kri.Id, kri.Status.State));
        Assert.Single(kri.Readings);
        Assert.Single(kri.Risks);

        var events = Assert.IsType<OkObjectResult>((await _controller.GetEvents(ReassessmentTriggerType.NewAiModel, 20)).Result);
        var first = Assert.Single(Assert.IsType<List<ReassessmentEventDto>>(events.Value));
        Assert.Equal(ReassessmentTriggerType.NewAiModel, first.TriggerType);

        var triggers = Assert.IsType<OkObjectResult>((await _controller.GetTriggers(4, true)).Result);
        Assert.Equal(4, Assert.Single(Assert.IsType<List<ReassessmentTriggerDto>>(triggers.Value)).RiskId);

        var metrics = Assert.IsType<OkObjectResult>((await _controller.GetMetrics()).Result);
        var panel = Assert.IsType<MethodologyMetricsDto>(metrics.Value);
        Assert.Equal(Enumerable.Range(1, 10).Select(n => $"M{n}"), panel.Metrics.Select(m => m.Code));
    }

    [Fact]
    public async Task TestKriWritesAnswerTheirDtos()
    {
        var created = Assert.IsType<OkObjectResult>((await _controller.CreateKri(Definition)).Result);
        Assert.Equal(Known, Assert.IsType<KriDto>(created.Value).Id);

        var updated = Assert.IsType<OkObjectResult>((await _controller.UpdateKri(Known, Definition)).Result);
        Assert.Equal(Known, Assert.IsType<KriDto>(updated.Value).Id);

        var retired = Assert.IsType<OkObjectResult>((await _controller.RetireKri(Known)).Result);
        Assert.Equal(Known, Assert.IsType<KriDto>(retired.Value).Id);

        var recorded = Assert.IsType<OkObjectResult>((await _controller.RecordReading(Known, Reading)).Result);
        Assert.Equal(Known, Assert.IsType<KriDetailDto>(recorded.Value).Id);

        var voided = Assert.IsType<OkObjectResult>((await _controller.VoidReading(Known, 1, Voiding)).Result);
        Assert.Equal(Known, Assert.IsType<KriDetailDto>(voided.Value).Id);

        var linked = Assert.IsType<OkObjectResult>((await _controller.LinkRisk(Known, 4)).Result);
        Assert.Equal(4, Assert.Single(Assert.IsType<KriDetailDto>(linked.Value).Risks).RiskId);

        Assert.IsType<NoContentResult>(await _controller.UnlinkRisk(Known, 4));
    }

    [Fact]
    public async Task TestEventWritesAnswerTheirDtos()
    {
        var declared = Assert.IsType<OkObjectResult>((await _controller.DeclareEvent(Declared(77))).Result);
        var dto = Assert.IsType<ReassessmentEventDto>(declared.Value);
        Assert.Equal((Known, ReassessmentTriggerType.SignificantIncidentOrNearMiss), (dto.Id, dto.TriggerType));

        var added = Assert.IsType<OkObjectResult>((await _controller.AddEventRisks(Known, Risks)).Result);
        Assert.Equal(Known, Assert.IsType<ReassessmentEventDto>(added.Value).Id);
    }

    // --- 400 ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        var create = Assert.IsType<BadRequestObjectResult>((await _controller.CreateKri(null)).Result);
        Assert.Equal(("invalid_parameter", "Name"), (Property(create.Value, "error"), Property(create.Value, "ParameterName")));

        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.UpdateKri(Invalid, Definition)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.RecordReading(Invalid, Reading)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.VoidReading(Invalid, 1, Voiding)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.GetKri(Invalid)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.RetireKri(Invalid)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.LinkRisk(Known, Invalid)).Result).Value, "ParameterName"));
        Assert.IsType<BadRequestObjectResult>(await _controller.UnlinkRisk(Invalid, 4));

        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.GetEvents(null, Invalid)).Result).Value, "ParameterName"));
        Assert.Equal("Type", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.DeclareEvent(null)).Result).Value, "ParameterName"));
        Assert.Equal("RiskIds", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.AddEventRisks(Known, null)).Result).Value, "ParameterName"));
        Assert.Equal("Reason", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.GetTriggers(Invalid)).Result).Value, "ParameterName"));
    }

    // --- 404 ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestNotFoundIsA404OnEveryActionThatTakesAnId()
    {
        Assert.IsType<NotFoundResult>((await _controller.GetKri(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.UpdateKri(Missing, Definition)).Result);
        Assert.IsType<NotFoundResult>((await _controller.RetireKri(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.RecordReading(Missing, Reading)).Result);
        Assert.IsType<NotFoundResult>((await _controller.VoidReading(Missing, 1, Voiding)).Result);
        Assert.IsType<NotFoundResult>((await _controller.VoidReading(Known, Missing, Voiding)).Result);
        Assert.IsType<NotFoundResult>((await _controller.LinkRisk(Missing, 4)).Result);
        Assert.IsType<NotFoundResult>((await _controller.LinkRisk(Known, Missing)).Result);
        Assert.IsType<NotFoundResult>(await _controller.UnlinkRisk(Missing, 4));
        Assert.IsType<NotFoundResult>(await _controller.UnlinkRisk(Known, Missing));
        Assert.IsType<NotFoundResult>((await _controller.AddEventRisks(Missing, Risks)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetTriggers(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.DeclareEvent(Declared(Missing))).Result);
    }

    // --- 409 ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestASecondEventOfTheSameIncidentIsA409()
    {
        var conflict = Assert.IsType<ConflictObjectResult>((await _controller.DeclareEvent(Declared(Conflict))).Result);

        Assert.Equal("already_exists", Property(conflict.Value, "error"));
        Assert.Equal(Conflict.ToString(), Property(conflict.Value, "Identification"));
        Assert.False(string.IsNullOrWhiteSpace(Property(conflict.Value, "Message")));
    }

    // --- 422 ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        var results = new List<UnprocessableEntityObjectResult>
        {
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.GetKri(Retired)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.UpdateKri(Retired, Definition)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.RetireKri(Retired)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.RecordReading(Retired, Reading)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.VoidReading(Retired, 1, Voiding)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.LinkRisk(Retired, 4)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.AddEventRisks(Retired, Risks)).Result),
            Assert.IsType<UnprocessableEntityObjectResult>((await _controller.DeclareEvent(Declared(Retired))).Result),
            Assert.IsType<UnprocessableEntityObjectResult>(await _controller.UnlinkRisk(Retired, 4))
        };

        Assert.All(results, r =>
        {
            Assert.Equal("kri_retired", Property(r.Value, "error"));
            Assert.False(string.IsNullOrWhiteSpace(Property(r.Value, "Message")));
        });
    }

    // --- 403 (middleware) ---------------------------------------------------------------------

    /// <summary>A write outside the caller's entities is not swallowed into a 500: the middleware answers 403.</summary>
    [Fact]
    public async Task TestAScopeViolationPropagatesToTheMiddleware()
    {
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            _controller.CreateKri(new KriRequest { Name = "Hours", EntityId = Scope }));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.UpdateKri(Scope, Definition));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.RetireKri(Scope));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.RecordReading(Scope, Reading));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.VoidReading(Scope, 1, Voiding));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.LinkRisk(Scope, 4));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.UnlinkRisk(Scope, 4));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.GetKri(Scope));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.GetEvents(null, Scope));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.GetTriggers(Scope));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.AddEventRisks(Scope, Risks));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _controller.DeclareEvent(Declared(Scope)));
    }

    // --- 500 ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        AssertServerError((await _controller.GetKri(Broken)).Result);
        AssertServerError((await _controller.UpdateKri(Broken, Definition)).Result);
        AssertServerError((await _controller.RetireKri(Broken)).Result);
        AssertServerError((await _controller.RecordReading(Broken, Reading)).Result);
        AssertServerError((await _controller.VoidReading(Broken, 1, Voiding)).Result);
        AssertServerError((await _controller.LinkRisk(Broken, 4)).Result);
        AssertServerError(await _controller.UnlinkRisk(Broken, 4));
        AssertServerError((await _controller.GetEvents(null, Broken)).Result);
        AssertServerError((await _controller.DeclareEvent(Declared(Broken))).Result);
        AssertServerError((await _controller.AddEventRisks(Broken, Risks)).Result);
        AssertServerError((await _controller.GetTriggers(Broken)).Result);
    }

    /// <summary>The actions no id can drive: a service that throws something unexpected is a bare 500.</summary>
    [Fact]
    public async Task TestTheListCreateAndMetricsFailuresAreA500()
    {
        var monitoring = Substitute.For<IMonitoringService>();
        monitoring.GetKrisAsync(Arg.Any<bool>()).Returns<List<KriDto>>(_ => throw new InvalidOperationException("boom"));
        monitoring.CreateKriAsync(Arg.Any<KriRequest>(), Arg.Any<int>())
            .Returns<KriDto>(_ => throw new InvalidOperationException("boom"));
        var metrics = Substitute.For<IMethodologyMetricsService>();
        metrics.GetAsync().Returns<MethodologyMetricsDto>(_ => throw new InvalidOperationException("boom"));
        var controller = ResolveController<MonitoringController>(s =>
        {
            s.AddSingleton(monitoring);
            s.AddSingleton(metrics);
        });

        AssertServerError((await controller.GetKris()).Result);
        AssertServerError((await controller.CreateKri(Definition)).Result);
        AssertServerError((await controller.GetMetrics()).Result);

        var broken = Substitute.For<IMethodologyMetricsService>();
        broken.GetAsync().Returns<MethodologyMetricsDto>(
            _ => throw new RuleBrokenException("Not computable.", "metrics_unavailable"));
        var other = ResolveController<MonitoringController>(s => s.AddSingleton(broken));
        Assert.Equal("metrics_unavailable", Property(Assert.IsType<UnprocessableEntityObjectResult>(
            (await other.GetMetrics()).Result).Value, "error"));
    }

    // --- arguments ----------------------------------------------------------------------------

    /// <summary>The acting user's id and every argument reach the service; a controller passing a fixed value would be invisible above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedMonitoringService.Create();
        var controller = ResolveController<MonitoringController>(s => s.AddSingleton(recording));

        await controller.GetKris();
        await controller.GetKris(true);
        await controller.GetKri(Known);
        await controller.CreateKri(Definition);
        await controller.UpdateKri(Known, Definition);
        await controller.RetireKri(Known);
        await controller.RecordReading(Known, Reading);
        await controller.VoidReading(Known, 7, Voiding);
        await controller.LinkRisk(Known, 4);
        await controller.UnlinkRisk(Known, 4);
        await controller.GetEvents(null, null);
        await controller.GetEvents(ReassessmentTriggerType.NewRegulation, 25);
        await controller.DeclareEvent(Declared(77));
        await controller.AddEventRisks(Known, Risks);
        await controller.GetTriggers(null);
        await controller.GetTriggers(4, true);

        await recording.Received(1).GetKrisAsync(false);
        await recording.Received(1).GetKrisAsync(true);
        await recording.Received(1).GetKriAsync(Known);
        await recording.Received(1).CreateKriAsync(Arg.Is<KriRequest>(r => r.Name == "Hours of unavailability"
            && r.Category == KriCategory.Unavailability && r.Direction == KriDirection.HigherIsWorse
            && r.ToleranceThreshold == 5m && r.WarningThreshold == 3m && r.OwnerId == 2 && r.EntityId == 3), 1);
        await recording.Received(1).UpdateKriAsync(Known, Arg.Is<KriRequest>(r => r.ToleranceThreshold == 5m), 1);
        await recording.Received(1).RetireKriAsync(Known, 1);
        await recording.Received(1).RecordReadingAsync(Known,
            Arg.Is<KriReadingRequest>(r => r.Value == 2m && r.Note == "Monthly export."), 1);
        await recording.Received(1).VoidReadingAsync(Known, 7,
            Arg.Is<KriReadingVoidRequest>(r => r.Reason == "Wrong source."), 1);
        await recording.Received(1).LinkRiskAsync(Known, 4, 1);
        await recording.Received(1).UnlinkRiskAsync(Known, 4, 1);
        await recording.Received(1).GetEventsAsync(null, null);
        await recording.Received(1).GetEventsAsync(ReassessmentTriggerType.NewRegulation, 25);
        await recording.Received(1).DeclareEventAsync(Arg.Is<ReassessmentEventRequest>(r =>
            r.Type == ReassessmentTriggerType.SignificantIncidentOrNearMiss && r.Title == "Outage" && r.IncidentId == 77
            && r.RiskIds!.Single() == 4), 1);
        await recording.Received(1).AddEventRisksAsync(Known,
            Arg.Is<ReassessmentRisksRequest>(r => r.RiskIds!.SequenceEqual(new[] { 3, 4 })), 1);
        await recording.Received(1).GetTriggersAsync(null, false);
        await recording.Received(1).GetTriggersAsync(4, true);
    }

    /// <summary>A missing body is replaced by an empty request, so the service names the missing field instead of throwing on null.</summary>
    [Fact]
    public async Task TestAMissingBodyReachesTheServiceAsAnEmptyRequest()
    {
        var recording = MockedMonitoringService.Create();
        var controller = ResolveController<MonitoringController>(s => s.AddSingleton(recording));

        await controller.CreateKri(null);
        await controller.UpdateKri(Known, null);
        await controller.RecordReading(Known, null);
        await controller.VoidReading(Known, 7, null);
        await controller.DeclareEvent(null);
        await controller.AddEventRisks(Known, null);

        await recording.Received(1).CreateKriAsync(
            Arg.Is<KriRequest>(r => r != null && r.Name == null && r.ToleranceThreshold == null), 1);
        await recording.Received(1).UpdateKriAsync(Known, Arg.Is<KriRequest>(r => r != null && r.Name == null), 1);
        await recording.Received(1).RecordReadingAsync(Known,
            Arg.Is<KriReadingRequest>(r => r != null && r.Value == null), 1);
        await recording.Received(1).VoidReadingAsync(Known, 7,
            Arg.Is<KriReadingVoidRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).DeclareEventAsync(
            Arg.Is<ReassessmentEventRequest>(r => r != null && r.Type == null), 1);
        await recording.Received(1).AddEventRisksAsync(Known,
            Arg.Is<ReassessmentRisksRequest>(r => r != null && r.RiskIds == null), 1);
    }
}
