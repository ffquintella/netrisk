using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.RiskIndex;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// M55 (S40 §8.4, §8.9) — <see cref="RiskIndexConfigRestService"/> over <see cref="StubRestBackend"/>: every route and verb,
/// the bodies as the API reads them, the draft's <c>If-Match</c>, the 204/404 answers read as "none", and every refusal
/// kept with its status, stable <c>error</c> code and the server's sentence (401 also discards the token).
/// </summary>
[TestSubject(typeof(RiskIndexConfigRestService))]
public class RiskIndexConfigRestServiceTest : BaseServiceTest
{
    private static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web);

    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IRiskIndexConfigService _service;

    public RiskIndexConfigRestServiceTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IRiskIndexConfigService>();
    }

    private void Answer(Method method, string path, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _backend.On(method, path, JsonSerializer.Serialize(body, Api), status);

    private void Refuse(Method method, string path, HttpStatusCode status, string code, string message) =>
        _backend.On(method, path, JsonSerializer.Serialize(new { error = code, message }, Api), status);

    private static RiskIndexProfileDto Profile(int id = 4, RiskIndexProfileStatus status = RiskIndexProfileStatus.Draft,
        int revision = 3) =>
        new(id, id, "Calibração T4/26", "balanced", RiskIndexMode.Interim, status, new RiskIndexParameters(), null, null, null,
            false, null, "alima", new DateTime(2026, 10, 5, 14, 2, 0, DateTimeKind.Utc), null, null, null, null, null, null,
            null, null, null, revision, false);

    private static JsonElement BodyOf(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement;

    [Fact]
    public async Task TestTheSchemaAndPresetsAreRead()
    {
        Answer(Method.Get, "/RiskIndex/Profiles/Schema", RiskIndexProfileRules.Schema());
        Answer(Method.Get, "/RiskIndex/Profiles/Presets", RiskIndexPresets.List());

        var schema = await _service.GetSchemaAsync();
        var presets = await _service.GetPresetsAsync();

        Assert.Contains(schema.Items, i => i.Key == "omega.REG" && i.Group == "weights");
        Assert.Contains(schema.Items, i => i.Key == "omega.TER" && i.Locked);
        Assert.Equal(RiskIndexPresets.All.Count, presets.Count);
        Assert.False(presets.Single(p => p.Key == RiskIndexPresets.PureMean).Activatable);
        Assert.Equal(0.45, presets.Single(p => p.Key == RiskIndexPresets.RegisterCentred).Parameters.Omega.Reg);
    }

    [Fact]
    public async Task TestNoActiveProfileIsNull()
    {
        _backend.OnStatus(Method.Get, "/RiskIndex/Profiles/Active", HttpStatusCode.NoContent);

        Assert.Null(await _service.GetActiveAsync());
    }

    [Fact]
    public async Task TestTheActiveProfileIsReadWithItsParameters()
    {
        Answer(Method.Get, "/RiskIndex/Profiles/Active", Profile(3, RiskIndexProfileStatus.Active));

        var active = await _service.GetActiveAsync();

        Assert.NotNull(active);
        Assert.Equal(RiskIndexProfileStatus.Active, active!.Status);
        Assert.Equal(0.30, active.Parameters.Omega.Reg);
        Assert.Equal(3, active.Parameters.Bands.Count);
    }

    [Fact]
    public async Task TestADraftIsCreatedWithItsOrigin()
    {
        Answer(Method.Post, "/RiskIndex/Profiles", Profile(), HttpStatusCode.Created);

        var created = await _service.CreateDraftAsync(new RiskIndexProfileCreateRequest("Calibração T4/26", 3, null));

        Assert.Equal(4, created.Id);
        Assert.Equal("POST /RiskIndex/Profiles", _backend.LastRequest.ToString());
        var body = BodyOf(_backend.LastRequest);
        Assert.Equal("Calibração T4/26", body.GetProperty("name").GetString());
        Assert.Equal(3, body.GetProperty("fromProfileId").GetInt32());
    }

    [Fact]
    public async Task TestADraftIsSavedUnderIfMatchWithTheParametersUnderTheirOwnKeys()
    {
        Answer(Method.Put, "/RiskIndex/Profiles/4", Profile(revision: 4));
        var parameters = new RiskIndexParameters { Tau = 0.85, Omega = new RiskIndexCategoryWeights { Reg = 0.32, Exp = 0.28 } };

        var saved = await _service.UpdateDraftAsync(4, 3, new RiskIndexProfileUpdateRequest("v4", RiskIndexMode.Interim, parameters));

        Assert.Equal(4, saved.Revision);
        Assert.Equal("PUT /RiskIndex/Profiles/4", _backend.LastRequest.ToString());
        Assert.Equal("\"3\"", _backend.LastRequest.Headers["If-Match"]);
        var sent = BodyOf(_backend.LastRequest).GetProperty("parameters");
        Assert.Equal(0.85, sent.GetProperty("tau").GetDouble());
        Assert.Equal(0.32, sent.GetProperty("omega").GetProperty("REG").GetDouble());
        Assert.True(sent.TryGetProperty("faixas", out _));
    }

    [Fact]
    public async Task TestAStaleDraftKeepsTheCodeAndTheSentence()
    {
        Refuse(Method.Put, "/RiskIndex/Profiles/4", HttpStatusCode.Conflict, "stale_draft", "The draft was changed by ana.");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.UpdateDraftAsync(4, 1, new RiskIndexProfileUpdateRequest("v4", RiskIndexMode.Interim, new RiskIndexParameters())));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("stale_draft", ex.ErrorCode);
        Assert.Equal("The draft was changed by ana.", ex.Message);
    }

    [Fact]
    public async Task TestADraftIsDeleted()
    {
        _backend.OnStatus(Method.Delete, "/RiskIndex/Profiles/4", HttpStatusCode.NoContent);

        await _service.DeleteDraftAsync(4);

        Assert.True(_backend.Sent(Method.Delete, "/RiskIndex/Profiles/4"));
    }

    [Fact]
    public async Task TestValidationPreviewAndSubmitHitTheirRoutes()
    {
        Answer(Method.Post, "/RiskIndex/Profiles/4/Validate",
            RiskIndexProfileRules.Validate(RiskIndexMode.Interim, new RiskIndexParameters(), RiskIndexMode.Interim,
                new RiskIndexParameters(), null));
        Answer(Method.Post, "/RiskIndex/Profiles/4/Preview",
            new RiskIndexPreviewStatusDto(9, RiskIndexPreviewState.Queued, 0, "h", null, null), HttpStatusCode.Accepted);
        Answer(Method.Get, "/RiskIndex/Profiles/4/Preview",
            new RiskIndexPreviewDto(new RiskIndexPreviewStatusDto(9, RiskIndexPreviewState.Completed, 100, "h", DateTime.UtcNow, null),
                [], 0.97, 34, [new RiskIndexTornadoRowDto("tau", -8.0, 11.9)], [], 0.51, 0.53, [], [], false));
        Answer(Method.Post, "/RiskIndex/Profiles/4/Submit", Profile(status: RiskIndexProfileStatus.PendingApproval));

        var validation = await _service.ValidateAsync(4);
        var queued = await _service.RequestPreviewAsync(4);
        var preview = await _service.GetPreviewAsync(4);
        var submitted = await _service.SubmitAsync(4, new RiskIndexSubmitRequest("Calibração trimestral do comitê"));

        Assert.True(validation.IsValid);
        Assert.Equal(RiskIndexPreviewState.Queued, queued.State);
        Assert.Equal(0.97, preview.KendallTau);
        Assert.Equal("tau", preview.Tornado.Single().ParameterKey);
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, submitted.Status);
        Assert.Equal("Calibração trimestral do comitê", BodyOf(_backend.LastRequest).GetProperty("justification").GetString());
    }

    [Fact]
    public async Task TestEligibilityApproveRejectAndRetireSendTheirBodies()
    {
        Answer(Method.Get, "/RiskIndex/Profiles/4/ApprovalEligibility",
            new RiskIndexApprovalEligibilityDto(
                [new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodAuthorCannotApprove, true, "ok")], "role:7", true,
                [RiskIndexRebaselineTrigger.PresetChange]));
        Answer(Method.Post, "/RiskIndex/Profiles/4/Approve", Profile(status: RiskIndexProfileStatus.Active));
        Answer(Method.Post, "/RiskIndex/Profiles/5/Reject", Profile(5, RiskIndexProfileStatus.Rejected));
        Answer(Method.Post, "/RiskIndex/Profiles/3/Retire", Profile(3, RiskIndexProfileStatus.Retired));

        var eligibility = await _service.GetApprovalEligibilityAsync(4);
        Assert.Equal("role:7", eligibility.GrantSource);
        Assert.True(eligibility.RequiresBoardRef);
        Assert.Equal(RiskIndexRebaselineTrigger.PresetChange, eligibility.RebaselineTriggers.Single());

        await _service.ApproveAsync(4, new RiskIndexApproveRequest("Ata CRTI 2026-10", "Conselho 2026/3", true, null));
        var approve = BodyOf(_backend.LastRequest);
        Assert.Equal("Ata CRTI 2026-10", approve.GetProperty("committeeDecisionRef").GetString());
        Assert.Equal("Conselho 2026/3", approve.GetProperty("boardApprovalRef").GetString());
        Assert.True(approve.GetProperty("confirmRebaseline").GetBoolean());

        await _service.RejectAsync(5, new RiskIndexRejectRequest("Pesos fora do acordado"));
        Assert.Equal("Pesos fora do acordado", BodyOf(_backend.LastRequest).GetProperty("reason").GetString());

        await _service.RetireAsync(3, new RiskIndexRetireRequest("Defeito no parâmetro tau", "Ata CRTI 2026-11"));
        var retire = BodyOf(_backend.LastRequest);
        Assert.Equal("Defeito no parâmetro tau", retire.GetProperty("justification").GetString());
        Assert.Equal("Ata CRTI 2026-11", retire.GetProperty("committeeDecisionRef").GetString());
    }

    [Fact]
    public async Task TestAnApprovalRefusalKeepsTheSegregationCode()
    {
        Refuse(Method.Post, "/RiskIndex/Profiles/4/Approve", HttpStatusCode.Forbidden, "sod_author_cannot_approve",
            "Whoever created or edited the draft cannot approve it.");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.ApproveAsync(4, new RiskIndexApproveRequest("Ata", null, false, null)));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal("sod_author_cannot_approve", ex.ErrorCode);
    }

    [Fact]
    public async Task TestEvidenceIsLinkedByFileIdAndListed()
    {
        _backend.OnStatus(Method.Post, "/RiskIndex/Profiles/4/Attachments", HttpStatusCode.NoContent);
        Answer(Method.Get, "/RiskIndex/Profiles/4/Attachments",
            new[] { new Model.DTO.FileListing { Name = "ata.pdf", UniqueName = "u1", OwnerId = 7 } });

        await _service.AttachEvidenceAsync(4, 81);
        Assert.Equal(81, BodyOf(_backend.LastRequest).GetProperty("fileId").GetInt32());

        var files = await _service.GetEvidenceAsync(4);
        Assert.Equal("ata.pdf", files.Single().Name);
    }

    [Fact]
    public async Task TestAMissingBridgeIsNullButAnotherRefusalThrows()
    {
        Refuse(Method.Get, "/RiskIndex/Profiles/3/Bridge", HttpStatusCode.NotFound, "bridge_not_found", "No bridge.");
        Refuse(Method.Get, "/RiskIndex/Profiles/2/Bridge", HttpStatusCode.Forbidden, "profile_values_require_global_scope",
            "Requires global scope.");

        Assert.Null(await _service.GetBridgeAsync(3));
        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetBridgeAsync(2));
        Assert.Equal("profile_values_require_global_scope", ex.ErrorCode);
    }

    [Fact]
    public async Task TestHistoryPassesItsLimitAndPublicationItsState()
    {
        Answer(Method.Get, "/RiskIndex/Profiles/4/History", Array.Empty<object>());
        Answer(Method.Put, "/RiskIndex/Publication",
            new RiskIndexPublicationDto(RiskIndexPublicationState.Published, DateTime.UtcNow, "cr"));

        var history = await _service.GetHistoryAsync(4, 50);
        Assert.Empty(history);
        Assert.Contains("limit=50", _backend.LastRequest.Query);

        var publication = await _service.SetPublicationAsync(RiskIndexPublicationState.Published);
        Assert.Equal(RiskIndexPublicationState.Published, publication.State);
        Assert.Equal(2, BodyOf(_backend.LastRequest).GetProperty("state").GetInt32());
    }

    [Fact]
    public async Task TestUnauthorizedDiscardsTheTokenAndAnUncodedNotFoundIsNotFound()
    {
        _backend.OnStatus(Method.Post, "/RiskIndex/Profiles/4/Validate", HttpStatusCode.Unauthorized);
        _backend.OnStatus(Method.Delete, "/RiskIndex/Profiles/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.ValidateAsync(4));
        _authentication.Received(1).DiscardAuthenticationToken();
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeleteDraftAsync(99));
    }
}
