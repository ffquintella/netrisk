using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.AiGovernance;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.12 (S53 §7) — <see cref="AiGovernanceRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every body and query string it sends and every status branch runs for real. A refusal (a write outside the model's
/// scope, a retired model, a typed override rate, a text carrying a personal value) keeps the server's sentence.
/// </summary>
[TestSubject(typeof(AiGovernanceRestService))]
public class AiGovernanceRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IAiGovernanceService _service;

    public AiGovernanceRestServiceTest()
    {
        _service = ResolveWith<IAiGovernanceService>(_backend);
    }

    private static AiModelDto Model(int id = 7) => new()
    {
        Id = id, Name = "Admissions triage", Purpose = "Ranks applications.", Kind = AiModelKind.Classification,
        Source = AiModelSource.Vendor, ThirdPartyId = 3, ThirdPartyHidden = true, Version = "2.1",
        Status = AiModelStatus.Production, RiskTier = AiModelRiskTier.High, HumanOversight = AiHumanOversight.EveryOutput,
        MaxEvaluationAgeDays = 180,
        Data = [new AiModelDataLinkDto { Id = 1, EntityId = 50, Usage = AiModelDataUsage.Training, Catalogued = true,
            PersonalData = PersonalDataCategory.SensitivePersonal }],
        Evaluation = new AiModelEvaluationDto
        {
            State = AiModelEvaluationState.NotEvaluated, Version = "2.1", RequiredMetrics = [AiModelMetric.Drift],
            Metrics = [new AiModelMetricStateDto { Metric = AiModelMetric.Drift, Required = true, State = AiMetricState.NotEvaluated,
                LastEvaluatedVersion = "2.0" }]
        },
        HiddenRiskCount = 1,
        Findings = [new AiModelFindingDto { Code = AiModelFindingCode.NotEvaluated, Message = "Not evaluated." }]
    };

    private static AiModelReadingDto Reading(int id = 5) => new()
    {
        Id = id, ModelId = 7, Metric = AiModelMetric.HumanOverrideRate, Value = 0.25m, ModelVersion = "2.1", OverrideCount = 3,
        SampleSize = 12, MeasuredAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static AiModelOverrideDto Override(int id = 8) => new()
    {
        Id = id, ModelId = 7, ModelVersion = "2.1", ModelOutput = "Reject", HumanDecision = "Admit",
        Reason = "The transcript was misread.", RecordedById = 4
    };

    private static RiskAiModelsDto RiskModels(int riskId = 11) => new()
    {
        RiskId = riskId, HiddenModelCount = 2,
        Models = [new RiskAiModelDto { ModelId = 7, Name = "Admissions triage", Version = "2.1",
            EvaluationState = AiModelEvaluationState.Incomplete, Note = "Bias." }]
    };

    private bool BodyHas(string fragment) => _backend.LastRequest.Body.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private bool QueryHas(string fragment) => _backend.LastRequest.Query.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // --- reads ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheListSendsItsFiltersOnlyWhenAsked()
    {
        _backend.OnGet("/AiModels", new[]
        {
            new AiModelSummaryDto
            {
                Id = 7, Name = "Admissions triage", Status = AiModelStatus.Production,
                EvaluationState = AiModelEvaluationState.NotEvaluated, LinkedRiskCount = 3,
                FindingCodes = [AiModelFindingCode.NotEvaluated]
            }
        });

        var all = await _service.GetModelsAsync();
        Assert.Equal((7, AiModelEvaluationState.NotEvaluated, 3), (Assert.Single(all).Id, all[0].EvaluationState, all[0].LinkedRiskCount));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));

        await _service.GetModelsAsync(AiModelStatus.Pilot, includeRetired: true, withFindingsOnly: true);
        Assert.True(QueryHas("status=2"));
        Assert.True(QueryHas("includeRetired=true"));
        Assert.True(QueryHas("withFindings=true"));
    }

    [Fact]
    public async Task TestTheModelReadsHitTheirRoutes()
    {
        _backend.OnGet("/AiModels/7", Model());
        _backend.OnGet("/AiModels/7/History", new[] { new AuditLog { Id = 1, EntityType = "AiModel", EntityId = 7, Field = "", Actor = "user:1" } });

        var model = await _service.GetModelAsync(7);
        Assert.Equal((AiModelEvaluationState.NotEvaluated, "2.0", true),
            (model.Evaluation.State, model.Evaluation.Metrics.Single().LastEvaluatedVersion, model.ThirdPartyHidden));
        Assert.Null(model.Evaluation.Metrics.Single().Value);
        Assert.Equal((PersonalDataCategory.SensitivePersonal, AiModelDataUsage.Training),
            (model.Data.Single().PersonalData!.Value, model.Data[0].Usage));

        Assert.Equal(7, Assert.Single(await _service.GetHistoryAsync(7, 25)).EntityId);
        Assert.True(QueryHas("limit=25"));
        await _service.GetHistoryAsync(7);
        Assert.True(QueryHas("limit=500"));
    }

    [Fact]
    public async Task TestTheReadingAndOverrideListsSendTheirFilters()
    {
        _backend.OnGet("/AiModels/7/Readings", new[] { Reading() });
        _backend.OnGet("/AiModels/7/Overrides", new[] { Override() });

        var readings = await _service.GetReadingsAsync(7);
        Assert.Equal((0.25m, 3), (Assert.Single(readings).Value, readings[0].OverrideCount!.Value));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));
        await _service.GetReadingsAsync(7, AiModelMetric.Drift, includeVoided: true);
        Assert.True(QueryHas("metric=5"));
        Assert.True(QueryHas("includeVoided=true"));

        Assert.Equal(4, Assert.Single(await _service.GetOverridesAsync(7)).RecordedById);
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));
        await _service.GetOverridesAsync(7, includeVoided: true);
        Assert.True(QueryHas("includeVoided=true"));
    }

    [Fact]
    public async Task TestTheRiskViewHitsItsRoute()
    {
        _backend.OnGet("/AiModels/Risks/11", RiskModels());

        var view = await _service.GetRiskModelsAsync(11);
        Assert.Equal((11, 2, AiModelEvaluationState.Incomplete), (view.RiskId, view.HiddenModelCount, view.Models.Single().EvaluationState));
    }

    // --- writes -----------------------------------------------------------------------------

    [Fact]
    public async Task TestTheInventoryWritesSendTheirBodies()
    {
        _backend.OnPost("/AiModels", Model());
        _backend.OnPut("/AiModels/7", Model());
        _backend.OnPost("/AiModels/7/Retire", Model());
        _backend.OnPut("/AiModels/7/Data", Model());

        await _service.CreateModelAsync(new AiModelRequest { Name = "Admissions triage", Version = "2.1", MaxEvaluationAgeDays = 180 });
        Assert.True(_backend.Sent(Method.Post, "/AiModels"));
        Assert.True(BodyHas("Admissions triage"));

        await _service.UpdateModelAsync(7, new AiModelRequest { Name = "Admissions triage", Version = "3.0" });
        Assert.True(_backend.Sent(Method.Put, "/AiModels/7"));
        Assert.True(BodyHas("3.0"));

        await _service.RetireModelAsync(7, new AiGovernanceReasonRequest { Reason = "Replaced by version 3." });
        Assert.True(_backend.Sent(Method.Post, "/AiModels/7/Retire"));
        Assert.True(BodyHas("Replaced by version 3."));

        await _service.SetDataAsync(7, new AiModelDataRequest { Data = [new AiModelDataLinkRequest { EntityId = 50, Usage = AiModelDataUsage.Input }] });
        Assert.True(_backend.Sent(Method.Put, "/AiModels/7/Data"));
        Assert.True(BodyHas("50"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateModelAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateModelAsync(7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RetireModelAsync(7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetDataAsync(7, null!));
    }

    [Fact]
    public async Task TestTheReadingAndOverrideWritesSendTheirBodies()
    {
        _backend.OnPost("/AiModels/7/Readings", Reading());
        _backend.OnPost("/AiModels/7/Readings/5/Void", Reading());
        _backend.OnPost("/AiModels/7/Overrides", Override());
        _backend.OnPost("/AiModels/7/Overrides/8/Void", Override());

        await _service.RecordReadingAsync(7, new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.83m, Method = "Holdout 2026-Q3" });
        Assert.True(_backend.Sent(Method.Post, "/AiModels/7/Readings"));
        Assert.True(BodyHas("Holdout 2026-Q3"));

        await _service.VoidReadingAsync(7, 5, new AiGovernanceReasonRequest { Reason = "Measured on the training set." });
        Assert.True(_backend.Sent(Method.Post, "/AiModels/7/Readings/5/Void"));
        Assert.True(BodyHas("training set"));

        await _service.RecordOverrideAsync(7, new AiModelOverrideRequest
            { ModelOutput = "Reject", HumanDecision = "Admit", Reason = "The transcript was misread." });
        Assert.True(_backend.Sent(Method.Post, "/AiModels/7/Overrides"));
        Assert.True(BodyHas("transcript was misread"));

        await _service.VoidOverrideAsync(7, 8, new AiGovernanceReasonRequest { Reason = "Recorded against the wrong model." });
        Assert.True(_backend.Sent(Method.Post, "/AiModels/7/Overrides/8/Void"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RecordReadingAsync(7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VoidReadingAsync(7, 5, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RecordOverrideAsync(7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VoidOverrideAsync(7, 8, null!));
    }

    [Fact]
    public async Task TestTheRiskLinksSendTheirNote()
    {
        _backend.OnPut("/AiModels/7/Risks/11", RiskModels());
        _backend.OnStatus(Method.Delete, "/AiModels/7/Risks/11", HttpStatusCode.NoContent);

        var linked = await _service.LinkRiskAsync(7, 11, new AiModelRiskLinkRequest { Note = "Bias against transfer students" });
        Assert.Equal(11, linked.RiskId);
        Assert.True(_backend.Sent(Method.Put, "/AiModels/7/Risks/11"));
        Assert.True(BodyHas("Bias against transfer students"));

        // No request at all is a link without a note, not a null body.
        await _service.LinkRiskAsync(7, 11);
        Assert.True(_backend.Sent(Method.Put, "/AiModels/7/Risks/11"));

        await _service.UnlinkRiskAsync(7, 11);
        Assert.True(_backend.Sent(Method.Delete, "/AiModels/7/Risks/11"));
    }

    // --- errors -----------------------------------------------------------------------------

    private static (string Name, Method Method, string Path, Func<IAiGovernanceService, Task> Call)[] Calls() =>
    [
        ("GetModels", Method.Get, "/AiModels", s => s.GetModelsAsync()),
        ("GetModel", Method.Get, "/AiModels/7", s => s.GetModelAsync(7)),
        ("GetHistory", Method.Get, "/AiModels/7/History", s => s.GetHistoryAsync(7)),
        ("CreateModel", Method.Post, "/AiModels", s => s.CreateModelAsync(new AiModelRequest())),
        ("UpdateModel", Method.Put, "/AiModels/7", s => s.UpdateModelAsync(7, new AiModelRequest())),
        ("RetireModel", Method.Post, "/AiModels/7/Retire", s => s.RetireModelAsync(7, new AiGovernanceReasonRequest())),
        ("SetData", Method.Put, "/AiModels/7/Data", s => s.SetDataAsync(7, new AiModelDataRequest())),
        ("GetReadings", Method.Get, "/AiModels/7/Readings", s => s.GetReadingsAsync(7)),
        ("RecordReading", Method.Post, "/AiModels/7/Readings", s => s.RecordReadingAsync(7, new AiModelReadingRequest())),
        ("VoidReading", Method.Post, "/AiModels/7/Readings/5/Void", s => s.VoidReadingAsync(7, 5, new AiGovernanceReasonRequest())),
        ("GetOverrides", Method.Get, "/AiModels/7/Overrides", s => s.GetOverridesAsync(7)),
        ("RecordOverride", Method.Post, "/AiModels/7/Overrides", s => s.RecordOverrideAsync(7, new AiModelOverrideRequest())),
        ("VoidOverride", Method.Post, "/AiModels/7/Overrides/8/Void", s => s.VoidOverrideAsync(7, 8, new AiGovernanceReasonRequest())),
        ("GetRiskModels", Method.Get, "/AiModels/Risks/11", s => s.GetRiskModelsAsync(11)),
        ("LinkRisk", Method.Put, "/AiModels/7/Risks/11", s => s.LinkRiskAsync(7, 11)),
        ("UnlinkRisk", Method.Delete, "/AiModels/7/Risks/11", s => s.UnlinkRiskAsync(7, 11))
    ];

    [Fact]
    public void TestEveryClientMethodIsCoveredByTheRefusalChecks()
    {
        var methods = typeof(IAiGovernanceService).GetMethods().Select(m => m.Name).Distinct().Count();
        Assert.Equal(16, methods);
        Assert.Equal(methods, Calls().Length);
    }

    [Fact]
    public async Task TestA404IsNotFoundOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.NotFound);

        foreach (var (_, _, _, call) in Calls())
            await Assert.ThrowsAsync<DataNotFoundException>(() => call(_service));
    }

    /// <summary>A retired model, a duplicate name, a typed override rate and a refused scope keep the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "This model was retired: the inventory keeps it as evidence, frozen.")]
    [InlineData(HttpStatusCode.Conflict, "A model with this name is already registered.")]
    [InlineData(HttpStatusCode.BadRequest, "The human override rate is computed from the overrides recorded in the period.")]
    [InlineData(HttpStatusCode.Forbidden, "The unit of the model is outside your scope.")]
    public async Task TestARefusalKeepsTheServersSentenceOnEveryMethod(HttpStatusCode status, string sentence)
    {
        foreach (var (_, method, path, _) in Calls())
            _backend.On(method, path, new { error = "refused", message = sentence }, status);

        foreach (var (name, _, _, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains(sentence), name);
        }
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailureOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.InternalServerError);

        foreach (var (name, _, path, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains($"Error calling {path}"), name);
        }
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsAFailureNotANull()
    {
        _backend.OnStatus(Method.Get, "/AiModels/7", HttpStatusCode.OK);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetModelAsync(7));
        Assert.Contains("empty body", thrown.Message);
    }

    [Fact]
    public async Task TestAnUnreachableServerIsACommunicationFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/AiModels");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetModelsAsync());
    }
}
