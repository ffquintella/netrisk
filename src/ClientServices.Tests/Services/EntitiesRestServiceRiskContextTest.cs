using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using Model.Exceptions;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// M55, T264 (S40 §8.5): the three risk-context calls of <see cref="EntitiesRestService"/> over
/// <see cref="StubRestBackend"/> — route, verb, query and body; the API's camelCase records read back; every refusal
/// kept with its status, stable <c>error</c> code and the server's sentence (401 also discards the token).
/// </summary>
[TestSubject(typeof(EntitiesRestService))]
public class EntitiesRestServiceRiskContextTest : BaseServiceTest
{
    private static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web);

    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IMemoryCacheService _cache = Substitute.For<IMemoryCacheService>();
    private readonly IEntitiesService _service;

    public EntitiesRestServiceRiskContextTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_cache);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IEntitiesService>();
    }

    /// <summary>Serialized as the API does (MVC web defaults), not with the stub's PascalCase default.</summary>
    private void Answer(Method method, string path, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _backend.On(method, path, JsonSerializer.Serialize(body, Api), status);

    private static EntityRiskContextBatchRequest Batch() =>
        new("Calibrated with the business owner", [new EntityRiskContextChange(12, 5, null, null),
            new EntityRiskContextChange(30, null, true, 51)]);

    [Fact]
    public async Task TestTheRowsAreReadWithTheRequestedTypes()
    {
        var at = new DateTime(2026, 9, 12, 10, 41, 0, DateTimeKind.Utc);
        Answer(Method.Get, "/Entities/RiskContext",
            new[] { new EntityRiskContextRowDto(30, "APP-03", "application", 10, true, 5, true, true, true, 51, at, "msouza") });

        var rows = await _service.GetRiskContextAsync(["application", " ", "businessProcess"]);

        var row = Assert.Single(rows);
        Assert.Equal((30, "APP-03", (int?)5, (bool?)true, (int?)51, (DateTime?)at, "msouza"),
            (row.EntityId, row.Name, row.Criticality, row.InternetFacing, row.SecurityClassificationId, row.LastChangedAtUtc,
                row.LastChangedBy));
        Assert.Equal(("GET", "/Entities/RiskContext", "?types=application,businessProcess"),
            (_backend.LastRequest.Method, _backend.LastRequest.Path, Uri.UnescapeDataString(_backend.LastRequest.Query)));
    }

    [Fact]
    public async Task TestWithoutTypesNoQueryIsSent()
    {
        Answer(Method.Get, "/Entities/RiskContext", Array.Empty<EntityRiskContextRowDto>());

        Assert.Empty(await _service.GetRiskContextAsync());
        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheBatchIsPutAsJsonAndClearsTheEntityCache()
    {
        Answer(Method.Put, "/Entities/RiskContext", new EntityRiskContextBatchResultDto(2, "abc"));

        var result = await _service.SaveRiskContextAsync(Batch());

        Assert.Equal(new EntityRiskContextBatchResultDto(2, "abc"), result);
        Assert.Equal("PUT", _backend.LastRequest.Method);
        var sent = JsonSerializer.Deserialize<EntityRiskContextBatchRequest>(_backend.LastRequest.Body, Api)!;
        Assert.Equal(Batch().Justification, sent.Justification);
        Assert.Equal(Batch().Changes, sent.Changes);
        _cache.Received(1).Remove<System.Collections.Generic.List<DAL.Entities.Entity>>("*");
    }

    [Fact]
    public async Task TestABatchThatChangedNothingKeepsTheCache()
    {
        Answer(Method.Put, "/Entities/RiskContext", new EntityRiskContextBatchResultDto(0, "abc"));

        await _service.SaveRiskContextAsync(Batch());

        _cache.DidNotReceive().Remove<System.Collections.Generic.List<DAL.Entities.Entity>>(Arg.Any<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "field_not_supported_for_type")]
    [InlineData(HttpStatusCode.Forbidden, "risk_context_requires_global_scope")]
    [InlineData(HttpStatusCode.NotFound, "entity_not_found")]
    public async Task TestABatchRefusalKeepsItsStatusCodeAndSentence(HttpStatusCode status, string code)
    {
        Answer(Method.Put, "/Entities/RiskContext",
            new { error = code, message = "Entity 40 is refused.", details = new { entityId = 40 } }, status);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.SaveRiskContextAsync(Batch()));

        Assert.Equal((status, code, "Entity 40 is refused.", "PUT"), (refusal.StatusCode, refusal.ErrorCode, refusal.Message, refusal.Method));
        Assert.Single(_backend.Requests);
        _cache.DidNotReceive().Remove<System.Collections.Generic.List<DAL.Entities.Entity>>(Arg.Any<string>());
    }

    [Fact]
    public async Task TestTheHistoryIsReadWithItsLimit()
    {
        var at = new DateTime(2026, 9, 12, 10, 41, 0, DateTimeKind.Utc);
        Answer(Method.Get, "/Entities/12/History",
            new[] { new EntityHistoryEntryDto(at, "Update", "criticality", "4", "5", "msouza", "b1", "Reitoria") });

        var history = await _service.GetHistoryAsync(12, 50);

        Assert.Equal(new EntityHistoryEntryDto(at, "Update", "criticality", "4", "5", "msouza", "b1", "Reitoria"),
            Assert.Single(history));
        Assert.Equal("GET /Entities/12/History?limit=50", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestTheHistoryDefaultLimitIs200()
    {
        Answer(Method.Get, "/Entities/12/History", Array.Empty<EntityHistoryEntryDto>());

        await _service.GetHistoryAsync(12);

        Assert.Equal("?limit=200", _backend.LastRequest.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "limit")]
    [InlineData(HttpStatusCode.Forbidden, "entity_history_requires_permission")]
    [InlineData(HttpStatusCode.NotFound, "entity_not_found")]
    public async Task TestAHistoryRefusalKeepsItsStatusCodeAndSentence(HttpStatusCode status, string code)
    {
        Answer(Method.Get, "/Entities/12/History", new { error = code, message = "No." }, status);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetHistoryAsync(12));

        Assert.Equal((status, code, "No."), (refusal.StatusCode, refusal.ErrorCode, refusal.Message));
    }

    [Fact]
    public async Task TestUnauthorizedDiscardsTheToken()
    {
        _backend.OnStatus(Method.Get, "/Entities/RiskContext", HttpStatusCode.Unauthorized);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRiskContextAsync());

        Assert.Equal((HttpStatusCode.Unauthorized, (string?)null), (refusal.StatusCode, refusal.ErrorCode));
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestARefusalThatIsNotJsonKeepsAGenericSentenceAndNoCode()
    {
        _backend.OnContent(Method.Get, "/Entities/12/History", "<html>proxy</html>", "text/html", HttpStatusCode.Forbidden);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetHistoryAsync(12));

        Assert.Equal(((string?)null, "Error calling /Entities/12/History"), (refusal.ErrorCode, refusal.Message));
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestATransportFailureIsACommunicationError()
    {
        _backend.OnTransportFailure(Method.Put, "/Entities/RiskContext", new HttpRequestException("down"));

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.SaveRiskContextAsync(Batch()));
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsRefusedRatherThanReadAsNull()
    {
        _backend.OnStatus(Method.Get, "/Entities/RiskContext", HttpStatusCode.OK);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRiskContextAsync());

        Assert.Equal(HttpStatusCode.OK, refusal.StatusCode);
    }
}
