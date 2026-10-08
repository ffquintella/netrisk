using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.AiGovernance;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.12 (S53 §6) — <see cref="AiModelsController"/>: each action's success shape, that the acting user and every
/// argument reach the service, and the mapping of every domain exception through <c>DecisionCycleErrors</c> onto the status
/// the other controllers use; a scope violation is re-thrown for <c>EntityScopeViolationMiddleware</c>. HI2 pins the generic
/// trail's refusal of the five AI governance types.
/// </summary>
[TestSubject(typeof(AiModelsController))]
public class AiModelsControllerTest : BaseControllerTest
{
    private const int Known = DecisionCycleIds.Known;

    private readonly AiModelsController _controller;

    public AiModelsControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<AiModelsController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly AiModelRequest ModelBody = new()
    {
        Name = "Admissions triage", Purpose = "Ranks applications.", Kind = AiModelKind.Classification,
        Source = AiModelSource.Vendor, ThirdPartyId = 3, Version = "2.1", Status = AiModelStatus.Pilot,
        RiskTier = AiModelRiskTier.High, HumanOversight = AiHumanOversight.EveryOutput, MaxEvaluationAgeDays = 180
    };
    private static readonly AiGovernanceReasonRequest ReasonBody = new() { Reason = "Replaced by version 3 of the vendor." };
    private static readonly AiModelDataRequest DataBody = new()
        { Data = [new AiModelDataLinkRequest { EntityId = 50, Usage = AiModelDataUsage.Training }] };
    private static readonly AiModelReadingRequest ReadingBody = new()
        { Metric = AiModelMetric.Recall, Value = 0.8m, MeasuredAt = DecisionCycleIds.When, Method = "Holdout 2026-Q3" };
    private static readonly AiModelOverrideRequest OverrideBody = new()
    {
        OccurredAt = DecisionCycleIds.When, ModelOutput = "Reject", HumanDecision = "Admit",
        Reason = "The transcript was misread."
    };
    private static readonly AiModelRiskLinkRequest LinkBody = new() { Note = "Bias against transfer students." };

    /// <summary>Every action an id can drive to an error, called with that id in each position it can take.</summary>
    private IEnumerable<(string Name, Func<int, Task<ActionResult?>> Call)> IdDrivenActions()
    {
        yield return ("GetModel", async id => (await _controller.GetModel(id)).Result);
        yield return ("GetHistory", async id => (await _controller.GetHistory(id)).Result);
        yield return ("CreateModel", async id => (await _controller.CreateModel(new AiModelRequest { Name = id.ToString() })).Result);
        yield return ("UpdateModel", async id => (await _controller.UpdateModel(id, ModelBody)).Result);
        yield return ("RetireModel", async id => (await _controller.RetireModel(id, ReasonBody)).Result);
        yield return ("SetData", async id => (await _controller.SetData(id, DataBody)).Result);
        yield return ("GetReadings", async id => (await _controller.GetReadings(id)).Result);
        yield return ("RecordReading", async id => (await _controller.RecordReading(id, ReadingBody)).Result);
        yield return ("VoidReading", async id => (await _controller.VoidReading(id, 5, ReasonBody)).Result);
        yield return ("VoidReading#", async id => (await _controller.VoidReading(Known, id, ReasonBody)).Result);
        yield return ("GetOverrides", async id => (await _controller.GetOverrides(id)).Result);
        yield return ("RecordOverride", async id => (await _controller.RecordOverride(id, OverrideBody)).Result);
        yield return ("VoidOverride", async id => (await _controller.VoidOverride(id, 8, ReasonBody)).Result);
        yield return ("VoidOverride#", async id => (await _controller.VoidOverride(Known, id, ReasonBody)).Result);
        yield return ("GetRiskModels", async id => (await _controller.GetRiskModels(id)).Result);
        yield return ("LinkRisk", async id => (await _controller.LinkRisk(id, 11, LinkBody)).Result);
        yield return ("LinkRisk#", async id => (await _controller.LinkRisk(Known, id, LinkBody)).Result);
        yield return ("UnlinkRisk", async id => await _controller.UnlinkRisk(id, 11));
        yield return ("UnlinkRisk#", async id => await _controller.UnlinkRisk(Known, id));
    }

    // --- success ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheReadsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _controller.GetModels(AiModelStatus.Production, true, true)).Result);
        var summary = Assert.Single(Assert.IsType<List<AiModelSummaryDto>>(list.Value));
        Assert.Equal((Known, AiModelEvaluationState.NotEvaluated), (summary.Id, summary.EvaluationState));
        Assert.Equal(AiModelFindingCode.NotEvaluated, Assert.Single(summary.FindingCodes));

        var model = Assert.IsType<AiModelDto>(Assert.IsType<OkObjectResult>((await _controller.GetModel(7)).Result).Value);
        Assert.Equal((7, "2.1", AiModelEvaluationState.NotEvaluated), (model.Id, model.Version, model.Evaluation.State));
        Assert.Single(model.Data);
        Assert.Single(model.Findings);
        Assert.Null(Assert.Single(model.Evaluation.Metrics).Value);

        var history = Assert.IsType<OkObjectResult>((await _controller.GetHistory(7)).Result);
        Assert.Equal(nameof(AiModel), Assert.Single(Assert.IsType<List<AuditLog>>(history.Value)).EntityType);

        var readings = Assert.IsType<OkObjectResult>((await _controller.GetReadings(7, AiModelMetric.Accuracy, true)).Result);
        Assert.Equal(7, Assert.Single(Assert.IsType<List<AiModelReadingDto>>(readings.Value)).ModelId);

        var overrides = Assert.IsType<OkObjectResult>((await _controller.GetOverrides(7, true)).Result);
        Assert.Equal(1, Assert.Single(Assert.IsType<List<AiModelOverrideDto>>(overrides.Value)).RecordedById);

        var risk = Assert.IsType<RiskAiModelsDto>(Assert.IsType<OkObjectResult>((await _controller.GetRiskModels(11)).Result).Value);
        Assert.Equal((11, Known), (risk.RiskId, Assert.Single(risk.Models).ModelId));
    }

    [Fact]
    public async Task TestTheWritesAnswerWithTheirDtos()
    {
        Assert.Equal(Known, Assert.IsType<AiModelDto>(
            Assert.IsType<OkObjectResult>((await _controller.CreateModel(ModelBody)).Result).Value).Id);
        Assert.Equal(7, Assert.IsType<AiModelDto>(
            Assert.IsType<OkObjectResult>((await _controller.UpdateModel(7, ModelBody)).Result).Value).Id);
        Assert.Equal(AiModelStatus.Retired, Assert.IsType<AiModelDto>(
            Assert.IsType<OkObjectResult>((await _controller.RetireModel(7, ReasonBody)).Result).Value).Status);
        Assert.Single(Assert.IsType<AiModelDto>(
            Assert.IsType<OkObjectResult>((await _controller.SetData(7, DataBody)).Result).Value).Data);

        Assert.Equal(6, Assert.IsType<AiModelReadingDto>(
            Assert.IsType<OkObjectResult>((await _controller.RecordReading(7, ReadingBody)).Result).Value).Id);
        Assert.NotNull(Assert.IsType<AiModelReadingDto>(
            Assert.IsType<OkObjectResult>((await _controller.VoidReading(7, 6, ReasonBody)).Result).Value).VoidedAt);
        Assert.Equal(8, Assert.IsType<AiModelOverrideDto>(
            Assert.IsType<OkObjectResult>((await _controller.RecordOverride(7, OverrideBody)).Result).Value).Id);
        Assert.NotNull(Assert.IsType<AiModelOverrideDto>(
            Assert.IsType<OkObjectResult>((await _controller.VoidOverride(7, 8, ReasonBody)).Result).Value).VoidedAt);

        Assert.Equal(11, Assert.IsType<RiskAiModelsDto>(
            Assert.IsType<OkObjectResult>((await _controller.LinkRisk(7, 11, LinkBody)).Result).Value).RiskId);
        Assert.IsType<NoContentResult>(await _controller.UnlinkRisk(7, 11));
    }

    /// <summary>
    /// HI2 (regression, S53 §4.8) — the AI governance types are audited, and the generic reader, which cannot apply the
    /// inventory's read policy nor the caller's entity scope, refuses them and points at the model's history route (or the
    /// risk's trail for a model link), as it does for hosts, third parties and the catalogue. Without the refusal any reader
    /// of the generic trail would read another unit's model, its overrides and their reasons.
    /// </summary>
    [Theory]
    [InlineData("AiModel", "/AiModels/{modelId}/History")]
    [InlineData("AiModelDataLink", "/AiModels/{modelId}/History")]
    [InlineData("AiModelMetricReading", "/AiModels/{modelId}/History")]
    [InlineData("AiModelOverride", "/AiModels/{modelId}/History")]
    [InlineData("AiModelRisk", "/Risks/{riskId}/AuditTrail")]
    public async Task TestHI2_TheGenericTrailRefusesAiGovernanceTypes(string entityType, string route)
    {
        Assert.Contains(entityType, ServerServices.Governance.AuditTrailService.AuditedTypes);
        var trail = _serviceProvider.GetRequiredService<AuditTrailController>();

        var bad = Assert.IsType<BadRequestObjectResult>((await trail.GetForRecord(entityType, 1)).Result);

        Assert.Equal(("use_ai_model_history", route), (Property(bad.Value, "error"), Property(bad.Value, "route")));
    }

    // --- error mapping ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<BadRequestObjectResult>(await call(DecisionCycleIds.Invalid));
            Assert.True(("invalid_parameter", "Reason") == (Property(result.Value, "error"), Property(result.Value, "ParameterName")), name);
        }
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryAction()
    {
        foreach (var (name, call) in IdDrivenActions())
            Assert.True(await call(DecisionCycleIds.Missing) is NotFoundResult, name);
    }

    [Fact]
    public async Task TestAnExistingRecordIsA409()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ConflictObjectResult>(await call(DecisionCycleIds.Conflict));
            Assert.True("already_exists" == Property(result.Value, "error"), name);
        }
    }

    [Fact]
    public async Task TestAPermissionRefusalIsA403()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ObjectResult>(await call(DecisionCycleIds.Forbidden));
            Assert.True(StatusCodes.Status403Forbidden == result.StatusCode, name);
        }
    }

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<UnprocessableEntityObjectResult>(await call(DecisionCycleIds.Rule));
            Assert.True("risk_archive_not_live" == Property(result.Value, "error"), name);
        }
    }

    [Fact]
    public async Task TestAScopeViolationPropagatesToTheMiddleware()
    {
        foreach (var (_, call) in IdDrivenActions())
            await Assert.ThrowsAsync<EntityScopeViolationException>(() => call(DecisionCycleIds.Scope));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<StatusCodeResult>(await call(DecisionCycleIds.Broken));
            Assert.True(StatusCodes.Status500InternalServerError == result.StatusCode, name);
        }
    }

    /// <summary>The list no id drives maps a failure like any other.</summary>
    [Fact]
    public async Task TestTheListMapsAFailureToo()
    {
        var broken = Substitute.For<IAiGovernanceService>();
        broken.GetModelsAsync(Arg.Any<AiModelStatus?>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns<List<AiModelSummaryDto>>(_ => throw new Model.Exceptions.InvalidParameterException("status", "Bad status."));

        var controller = ResolveController<AiModelsController>(s => s.AddSingleton(broken));
        Assert.IsType<BadRequestObjectResult>((await controller.GetModels((AiModelStatus)9)).Result);

        var failing = Substitute.For<IAiGovernanceService>();
        failing.GetModelsAsync(Arg.Any<AiModelStatus?>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns<List<AiModelSummaryDto>>(_ => throw new InvalidOperationException("boom"));
        controller = ResolveController<AiModelsController>(s => s.AddSingleton(failing));
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await controller.GetModels()).Result).StatusCode);
    }

    // --- arguments ----------------------------------------------------------------------------------------------------

    /// <summary>The acting user's id and every argument reach the service; a controller passing a fixed value would pass above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedAiGovernanceService.Create();
        var controller = ResolveController<AiModelsController>(s => s.AddSingleton(recording));

        await controller.GetModels();
        await controller.GetModels(AiModelStatus.Pilot, true, true);
        await controller.GetModel(7);
        await controller.GetHistory(7, 25);
        await controller.CreateModel(ModelBody);
        await controller.UpdateModel(7, ModelBody);
        await controller.RetireModel(7, ReasonBody);
        await controller.SetData(7, DataBody);
        await controller.GetReadings(7);
        await controller.GetReadings(7, AiModelMetric.Drift, true);
        await controller.RecordReading(7, ReadingBody);
        await controller.VoidReading(7, 5, ReasonBody);
        await controller.GetOverrides(7);
        await controller.GetOverrides(7, true);
        await controller.RecordOverride(7, OverrideBody);
        await controller.VoidOverride(7, 8, ReasonBody);
        await controller.GetRiskModels(11);
        await controller.LinkRisk(7, 11, LinkBody);
        await controller.UnlinkRisk(7, 11);

        await recording.Received(1).GetModelsAsync(null, false, false);
        await recording.Received(1).GetModelsAsync(AiModelStatus.Pilot, true, true);
        await recording.Received(1).GetModelAsync(7);
        await recording.Received(1).GetHistoryAsync(7, 25);
        await recording.Received(1).CreateAsync(Arg.Is<AiModelRequest>(r =>
            r.Name == "Admissions triage" && r.ThirdPartyId == 3 && r.Version == "2.1" && r.MaxEvaluationAgeDays == 180 &&
            r.HumanOversight == AiHumanOversight.EveryOutput), 1);
        await recording.Received(1).UpdateAsync(7, Arg.Is<AiModelRequest>(r => r.RiskTier == AiModelRiskTier.High), 1);
        await recording.Received(1).RetireAsync(7, Arg.Is<AiGovernanceReasonRequest>(r => r.Reason == ReasonBody.Reason), 1);
        await recording.Received(1).SetDataAsync(7, Arg.Is<AiModelDataRequest>(r =>
            r.Data!.Single().EntityId == 50 && r.Data!.Single().Usage == AiModelDataUsage.Training), 1);
        await recording.Received(1).GetReadingsAsync(7, null, false);
        await recording.Received(1).GetReadingsAsync(7, AiModelMetric.Drift, true);
        await recording.Received(1).RecordReadingAsync(7, Arg.Is<AiModelReadingRequest>(r =>
            r.Metric == AiModelMetric.Recall && r.Value == 0.8m && r.Method == "Holdout 2026-Q3"), 1);
        await recording.Received(1).VoidReadingAsync(7, 5, Arg.Is<AiGovernanceReasonRequest>(r => r.Reason == ReasonBody.Reason), 1);
        await recording.Received(1).GetOverridesAsync(7, false);
        await recording.Received(1).GetOverridesAsync(7, true);
        await recording.Received(1).RecordOverrideAsync(7, Arg.Is<AiModelOverrideRequest>(r =>
            r.ModelOutput == "Reject" && r.HumanDecision == "Admit" && r.Reason == OverrideBody.Reason), 1);
        await recording.Received(1).VoidOverrideAsync(7, 8, Arg.Is<AiGovernanceReasonRequest>(r => r.Reason == ReasonBody.Reason), 1);
        await recording.Received(1).GetRiskModelsAsync(11);
        await recording.Received(1).LinkRiskAsync(7, 11, Arg.Is<AiModelRiskLinkRequest>(r => r.Note == LinkBody.Note), 1);
        await recording.Received(1).UnlinkRiskAsync(7, 11, 1);
    }

    /// <summary>A missing body reaches the service as an empty request, so the service names the missing field.</summary>
    [Fact]
    public async Task TestAMissingBodyReachesTheServiceAsAnEmptyRequest()
    {
        var recording = MockedAiGovernanceService.Create();
        var controller = ResolveController<AiModelsController>(s => s.AddSingleton(recording));

        await controller.CreateModel(null);
        await controller.UpdateModel(7, null);
        await controller.RetireModel(7, null);
        await controller.SetData(7, null);
        await controller.RecordReading(7, null);
        await controller.VoidReading(7, 5, null);
        await controller.RecordOverride(7, null);
        await controller.VoidOverride(7, 8, null);
        await controller.LinkRisk(7, 11, null);

        await recording.Received(1).CreateAsync(Arg.Is<AiModelRequest>(r => r != null && r.Name == null), 1);
        await recording.Received(1).UpdateAsync(7, Arg.Is<AiModelRequest>(r => r != null && r.Name == null), 1);
        await recording.Received(1).RetireAsync(7, Arg.Is<AiGovernanceReasonRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).SetDataAsync(7, Arg.Is<AiModelDataRequest>(r => r != null && r.Data == null), 1);
        await recording.Received(1).RecordReadingAsync(7, Arg.Is<AiModelReadingRequest>(r => r != null && r.Metric == null), 1);
        await recording.Received(1).VoidReadingAsync(7, 5, Arg.Is<AiGovernanceReasonRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).RecordOverrideAsync(7, Arg.Is<AiModelOverrideRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).VoidOverrideAsync(7, 8, Arg.Is<AiGovernanceReasonRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).LinkRiskAsync(7, 11, Arg.Is<AiModelRiskLinkRequest>(r => r != null && r.Note == null), 1);
    }
}
