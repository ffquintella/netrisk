using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Exceptions;
using Model.Monitoring;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IMonitoringService"/> for <c>MonitoringControllerTest</c> (Stage 9.8). Its ids drive every
/// branch of the controller's error mapping without a per-test double: 400 an invalid parameter, 404 missing (or out of
/// scope), 409 an event that already exists, 422 a retired KRI, 403 an entity-scope violation (re-thrown to the
/// middleware), 500 anything else. A list is driven by its filter: <c>limit</c> for the events, <c>riskId</c> for the
/// triggers; a declared event by its <c>IncidentId</c>.
/// </summary>
public static class MockedMonitoringService
{
    public const int Known = 10;
    public const int Invalid = 400;
    public const int Scope = 403;
    public const int Missing = 404;
    public const int Conflict = 409;
    public const int Retired = 422;
    public const int Broken = 500;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("Reason", "A reason is required.");
            case Scope: throw new DAL.Exceptions.EntityScopeViolationException(nameof(DAL.Entities.Kri), 99, "100");
            case Missing: throw new DataNotFoundException("kris", id.ToString());
            case Conflict:
                throw new DataAlreadyExistsException("netrisk", "reassessment_events", id.ToString(),
                    "The incident already has its event.");
            case Retired: throw new RuleBrokenException("The KRI is retired.", "kri_retired");
            case Broken: throw new InvalidOperationException("boom");
        }
    }

    public static KriDto Kri(int id) => new()
    {
        Id = id, Name = "Hours of unavailability", Category = KriCategory.Unavailability, Source = "Monitoring export",
        Unit = "hours", Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m, WarningThreshold = 3m,
        ToleranceRationale = "Phase 0 decision.", MaxReadingAgeDays = 31, CreatedAt = When, LinkedRisks = 1,
        Status = new KriStatusDto { State = KriState.WithinTolerance, EvaluatedAt = When, Explanation = "Within tolerance." }
    };

    public static KriDetailDto Detail(int id) => new()
    {
        Id = id, Name = "Hours of unavailability", Category = KriCategory.Unavailability, Source = "Monitoring export",
        Unit = "hours", Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 5m, WarningThreshold = 3m,
        ToleranceRationale = "Phase 0 decision.", MaxReadingAgeDays = 31, CreatedAt = When, LinkedRisks = 1,
        Status = new KriStatusDto { State = KriState.WithinTolerance, EvaluatedAt = When, Explanation = "Within tolerance." },
        Readings = [new KriReadingDto { Id = 1, KriId = id, Value = 2m, ObservedAt = When, CreatedAt = When }],
        Risks = [new KriLinkedRiskDto { RiskId = 4, Subject = "Ransomware", Status = "New", LinkedAt = When }]
    };

    public static ReassessmentTriggerDto Trigger(int riskId) => new()
    {
        Id = 1, EventId = Known, TriggerType = ReassessmentTriggerType.NewRegulation, Origin = ReassessmentEventOrigin.Declared,
        EventTitle = "New regulation", RiskId = riskId, RiskSubject = "Ransomware", RaisedAt = When,
        State = ReassessmentTriggerState.Pending
    };

    public static ReassessmentEventDto Event(int id, ReassessmentTriggerType type = ReassessmentTriggerType.NewRegulation) => new()
    {
        Id = id, TriggerType = type, Origin = ReassessmentEventOrigin.Declared, Title = "New regulation",
        OccurredAt = When, CreatedAt = When, Triggers = [Trigger(4)]
    };

    public static IMonitoringService Create()
    {
        var service = Substitute.For<IMonitoringService>();

        service.GetKrisAsync(Arg.Any<bool>()).Returns(call =>
            new List<KriDto> { Kri(Known) });
        service.GetKriAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return Detail(call.Arg<int>()); });
        service.CreateKriAsync(Arg.Any<KriRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.Arg<KriRequest>();
            if (request.Name is null) throw new InvalidParameterException("Name", "A name is required.");
            if (request.EntityId is { } entityId) Gate(entityId);
            return Kri(Known);
        });
        service.UpdateKriAsync(Arg.Any<int>(), Arg.Any<KriRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Kri(call.ArgAt<int>(0)); });
        service.RetireKriAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Kri(call.ArgAt<int>(0)); });
        service.RecordReadingAsync(Arg.Any<int>(), Arg.Any<KriReadingRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Detail(call.ArgAt<int>(0)); });
        service.VoidReadingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<KriReadingVoidRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); Gate(call.ArgAt<int>(1)); return Detail(call.ArgAt<int>(0)); });
        service.LinkRiskAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); Gate(call.ArgAt<int>(1)); return Detail(call.ArgAt<int>(0)); });
        service.UnlinkRiskAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); Gate(call.ArgAt<int>(1)); return Task.CompletedTask; });

        service.GetEventsAsync(Arg.Any<ReassessmentTriggerType?>(), Arg.Any<int?>()).Returns(call =>
        {
            if (call.ArgAt<int?>(1) is { } limit) Gate(limit);
            return new List<ReassessmentEventDto> { Event(Known, call.ArgAt<ReassessmentTriggerType?>(0) ?? ReassessmentTriggerType.NewRegulation) };
        });
        service.DeclareEventAsync(Arg.Any<ReassessmentEventRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.Arg<ReassessmentEventRequest>();
            if (request.Type is null) throw new InvalidParameterException("Type", "The trigger type is required.");
            if (request.IncidentId is { } incident) Gate(incident);
            return Event(Known, request.Type.Value);
        });
        service.AddEventRisksAsync(Arg.Any<int>(), Arg.Any<ReassessmentRisksRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                Gate(call.ArgAt<int>(0));
                if (call.ArgAt<ReassessmentRisksRequest>(1).RiskIds is not { Count: > 0 })
                    throw new InvalidParameterException("RiskIds", "At least one risk is required.");
                return Event(call.ArgAt<int>(0));
            });
        service.GetTriggersAsync(Arg.Any<int?>(), Arg.Any<bool>()).Returns(call =>
        {
            if (call.ArgAt<int?>(0) is { } riskId) Gate(riskId);
            return new List<ReassessmentTriggerDto> { Trigger(call.ArgAt<int?>(0) ?? 4) };
        });

        return service;
    }
}
