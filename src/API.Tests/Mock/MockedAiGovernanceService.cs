using System.Collections.Generic;
using DAL.Entities;
using DAL.Enums;
using Model.AiGovernance;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IAiGovernanceService"/> for <c>AiModelsControllerTest</c> (S53 §6): the model, reading,
/// override or risk id — every id the route has — drives the branch through <see cref="DecisionCycleIds.Gate"/>, so every
/// action answers each domain exception the way the Stage 9.9–9.11 controllers do. The creation has no id in the route: it
/// is driven by the number in the request's name.
/// </summary>
public static class MockedAiGovernanceService
{
    public static AiModelDto Model(int id) => new()
    {
        Id = id, Name = "Admissions triage", Purpose = "Ranks applications for a human reviewer.",
        Kind = AiModelKind.Classification, Source = AiModelSource.Vendor, ThirdPartyId = 3, ThirdPartyName = "Vendor",
        Version = "2.1", Status = AiModelStatus.Production, RiskTier = AiModelRiskTier.High,
        HumanOversight = AiHumanOversight.EveryOutput, MaxEvaluationAgeDays = 180,
        Data = [new AiModelDataLinkDto { Id = 1, EntityId = 50, Name = "Applications", Usage = AiModelDataUsage.Input }],
        Evaluation = new AiModelEvaluationDto
        {
            State = AiModelEvaluationState.NotEvaluated, Version = "2.1",
            RequiredMetrics = [AiModelMetric.Accuracy, AiModelMetric.Drift],
            Metrics = [new AiModelMetricStateDto { Metric = AiModelMetric.Drift, Required = true, State = AiMetricState.NotEvaluated }]
        },
        Findings = [new AiModelFindingDto { Code = AiModelFindingCode.NotEvaluated, Message = "Not evaluated." }]
    };

    public static AiModelReadingDto Reading(int modelId, int id) => new()
    {
        Id = id, ModelId = modelId, Metric = AiModelMetric.Accuracy, Value = 0.91m, ModelVersion = "2.1",
        MeasuredAt = DecisionCycleIds.When
    };

    public static AiModelOverrideDto Override(int modelId, int id) => new()
    {
        Id = id, ModelId = modelId, ModelVersion = "2.1", OccurredAt = DecisionCycleIds.When, ModelOutput = "Reject",
        HumanDecision = "Admit", Reason = "The transcript was misread.", RecordedById = 1
    };

    public static RiskAiModelsDto RiskModels(int riskId) => new()
    {
        RiskId = riskId,
        Models =
        [
            new RiskAiModelDto
            {
                ModelId = DecisionCycleIds.Known, Name = "Admissions triage", Version = "2.1",
                Status = AiModelStatus.Production, EvaluationState = AiModelEvaluationState.NotEvaluated, Note = "Bias."
            }
        ]
    };

    private static List<AuditLog> History(int id) =>
        [new AuditLog { Id = 1, EntityType = nameof(AiModel), EntityId = id, Field = string.Empty, Actor = "user:1" }];

    public static IAiGovernanceService Create()
    {
        var service = Substitute.For<IAiGovernanceService>();

        service.GetModelsAsync(Arg.Any<AiModelStatus?>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns(_ =>
            new List<AiModelSummaryDto>
            {
                new()
                {
                    Id = DecisionCycleIds.Known, Name = "Admissions triage", Version = "2.1", Status = AiModelStatus.Production,
                    EvaluationState = AiModelEvaluationState.NotEvaluated, FindingCodes = [AiModelFindingCode.NotEvaluated]
                }
            });
        service.GetModelAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Model(call.Arg<int>());
        });
        service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return History(call.ArgAt<int>(0));
        });
        service.CreateAsync(Arg.Any<AiModelRequest>(), Arg.Any<int>()).Returns(call =>
        {
            if (int.TryParse(call.Arg<AiModelRequest>().Name, out var id)) DecisionCycleIds.Gate(id);
            return Model(DecisionCycleIds.Known);
        });
        service.UpdateAsync(Arg.Any<int>(), Arg.Any<AiModelRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Model(call.ArgAt<int>(0));
        });
        service.RetireAsync(Arg.Any<int>(), Arg.Any<AiGovernanceReasonRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var model = Model(call.ArgAt<int>(0));
            model.Status = AiModelStatus.Retired;
            return model;
        });
        service.SetDataAsync(Arg.Any<int>(), Arg.Any<AiModelDataRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Model(call.ArgAt<int>(0));
        });
        service.GetReadingsAsync(Arg.Any<int>(), Arg.Any<AiModelMetric?>(), Arg.Any<bool>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return new List<AiModelReadingDto> { Reading(call.ArgAt<int>(0), 5) };
        });
        service.RecordReadingAsync(Arg.Any<int>(), Arg.Any<AiModelReadingRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Reading(call.ArgAt<int>(0), 6);
        });
        service.VoidReadingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AiGovernanceReasonRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                var reading = Reading(call.ArgAt<int>(0), call.ArgAt<int>(1));
                reading.VoidedAt = DecisionCycleIds.When;
                return reading;
            });
        service.GetOverridesAsync(Arg.Any<int>(), Arg.Any<bool>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return new List<AiModelOverrideDto> { Override(call.ArgAt<int>(0), 7) };
        });
        service.RecordOverrideAsync(Arg.Any<int>(), Arg.Any<AiModelOverrideRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Override(call.ArgAt<int>(0), 8);
        });
        service.VoidOverrideAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AiGovernanceReasonRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                var record = Override(call.ArgAt<int>(0), call.ArgAt<int>(1));
                record.VoidedAt = DecisionCycleIds.When;
                return record;
            });
        service.GetRiskModelsAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return RiskModels(call.Arg<int>());
        });
        service.LinkRiskAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AiModelRiskLinkRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                return RiskModels(call.ArgAt<int>(1));
            });
        service.UnlinkRiskAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return System.Threading.Tasks.Task.CompletedTask;
        });
        service.GetMetricsSummaryAsync().Returns(_ => new AiModelMetricsSummaryDto());

        return service;
    }
}
