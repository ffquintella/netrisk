using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using RestSharp;
using RestSharp.Authenticators;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers <see cref="VulnerabilitiesRestService"/> over the stub HTTP backend, so RestSharp's
/// serialization and status handling run for real.
///
/// RestSharp 114 semantics that decide which branch a status reaches (verified against the
/// shipped assembly):
/// <list type="bullet">
/// <item>a non-success status other than 404 makes every verb extension — typed <b>and</b>
/// untyped — throw <see cref="HttpRequestException"/>, so it lands in the service's
/// <c>catch</c> and becomes <see cref="RestComunicationException"/>;</item>
/// <item>404 is treated as a legitimate empty answer: nothing throws, the typed extensions hand
/// back <c>null</c> and the untyped ones a response whose <c>StatusCode</c> is NotFound;</item>
/// <item>a non-OK <i>success</i> status (204) therefore is the only way to reach the
/// <c>StatusCode != OK</c> guards in the methods that inspect the response themselves.</item>
/// </list>
/// </summary>
[TestSubject(typeof(VulnerabilitiesRestService))]
public class VulnerabilitiesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IVulnerabilitiesService _service;

    public VulnerabilitiesRestServiceTest()
    {
        _service = ResolveWith<IVulnerabilitiesService>(_backend);
    }

    private static Vulnerability Vuln(int id = 1, string title = "Open port") => new()
    {
        Id = id,
        Title = title,
        Severity = "High",
        Status = 1,
        Score = 7.5,
        DetectionCount = 3,
        FirstDetection = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static List<Vulnerability> TwoVulnerabilities() => [Vuln(1, "First"), Vuln(2, "Second")];

    private static List<RiskScoring> TwoScores() =>
    [
        new() { Id = 1, ScoringMethod = 1, CalculatedRisk = 6.5f, ClassicLikelihood = 3, ClassicImpact = 4 },
        new() { Id = 2, ScoringMethod = 1, CalculatedRisk = 2.5f, ClassicLikelihood = 1, ClassicImpact = 2 }
    ];

    // ---------------------------------------------------------------- GetAll

    [Fact]
    public void TestGetAllReturnsTheListedVulnerabilities()
    {
        _backend.OnGet("/Vulnerabilities", TwoVulnerabilities());

        var result = _service.GetAll();

        Assert.Equal(2, result.Count);
        Assert.Equal("First", result[0].Title);
        Assert.Equal("High", result[0].Severity);
        Assert.Equal("GET /Vulnerabilities", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAllThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetAll());
    }

    [Fact]
    public void TestGetAllWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetAll());
    }

    [Fact]
    public void TestGetAllWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Vulnerabilities");

        Assert.Throws<RestComunicationException>(() => _service.GetAll());
    }

    // ------------------------------------------------------------ GetFiltered

    [Fact]
    public void TestGetFilteredReturnsTheListAndTheTotalCount()
    {
        var backend = new HeaderStubBackend
        {
            Body = JsonSerializer.Serialize(TwoVulnerabilities()),
            TotalCount = "42"
        };
        var service = ResolveWithHeaders(backend);

        var result = service.GetFiltered(10, 2, "abc", out var totalRecords, out var validFilter);

        Assert.Equal(2, result.Count);
        Assert.Equal("Second", result[1].Title);
        Assert.Equal(42, totalRecords);
        Assert.True(validFilter);
        Assert.Equal("/Vulnerabilities/Filtered", backend.LastPath);
        Assert.Contains("pageSize=10", backend.LastQuery);
        Assert.Contains("page=2", backend.LastQuery);
        Assert.Contains("filters=abc", backend.LastQuery);
    }

    [Fact]
    public void TestGetFilteredOmitsTheFilterParameterWhenTheFilterIsEmpty()
    {
        var backend = new HeaderStubBackend { Body = "[]", TotalCount = "0" };
        var service = ResolveWithHeaders(backend);

        var result = service.GetFiltered(5, 1, "", out var totalRecords, out var validFilter);

        Assert.Empty(result);
        Assert.Equal(0, totalRecords);
        Assert.True(validFilter);
        Assert.DoesNotContain("filters=", backend.LastQuery);
    }

    [Fact]
    public void TestGetFilteredReportsABadFilterOnBadRequest()
    {
        var backend = new HeaderStubBackend { Status = HttpStatusCode.BadRequest, Body = "bad filter" };
        var service = ResolveWithHeaders(backend);

        var exception = Assert.Throws<BadFilterException>(
            () => service.GetFiltered(10, 1, "nope=1", out _, out _));

        Assert.Equal("nope=1", exception.Filter);
    }

    [Fact]
    public void TestGetFilteredReportsABadFilterOnConflict()
    {
        var backend = new HeaderStubBackend { Status = HttpStatusCode.Conflict, Body = "conflicting filter" };
        var service = ResolveWithHeaders(backend);

        var exception = Assert.Throws<BadFilterException>(
            () => service.GetFiltered(10, 1, "dup=1", out _, out _));

        Assert.Equal("dup=1", exception.Filter);
    }

    [Fact]
    public void TestGetFilteredThrowsOnAnUnexpectedSuccessStatus()
    {
        var backend = new HeaderStubBackend { Status = HttpStatusCode.NoContent, Body = "" };
        var service = ResolveWithHeaders(backend);

        Assert.Throws<InvalidHttpRequestException>(() => service.GetFiltered(10, 1, "", out _, out _));
    }

    [Fact]
    public void TestGetFilteredWrapsATransportFailure()
    {
        var backend = new HeaderStubBackend { FailTransport = true };
        var service = ResolveWithHeaders(backend);

        Assert.Throws<RestComunicationException>(() => service.GetFiltered(10, 1, "", out _, out _));
    }

    // ------------------------------------------------------- GetFilteredAsync

    [Fact]
    public async Task TestGetFilteredAsyncReturnsTheListTheCountAndTheFilterValidity()
    {
        var backend = new HeaderStubBackend
        {
            Body = JsonSerializer.Serialize(TwoVulnerabilities()),
            TotalCount = "7"
        };
        var service = ResolveWithHeaders(backend);

        var (vulnerabilities, totalRecords, validFilter) = await service.GetFilteredAsync(20, 3, "sev=high");

        Assert.Equal(2, vulnerabilities.Count);
        Assert.Equal(7, totalRecords);
        Assert.True(validFilter);
        Assert.Equal("/Vulnerabilities/Filtered", backend.LastPath);
        Assert.Contains("pageSize=20", backend.LastQuery);
        Assert.Contains("page=3", backend.LastQuery);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestGetFilteredAsyncForwardsTheIncludeFixRequestsFlag(bool includeFixRequests)
    {
        var backend = new HeaderStubBackend { Body = "[]", TotalCount = "0" };
        var service = ResolveWithHeaders(backend);

        await service.GetFilteredAsync(10, 1, "", includeFixRequests);

        Assert.Contains($"includeFixRequests={includeFixRequests}", backend.LastQuery,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestGetFilteredAsyncReportsABadFilterOnBadRequest()
    {
        var backend = new HeaderStubBackend { Status = HttpStatusCode.BadRequest, Body = "bad" };
        var service = ResolveWithHeaders(backend);

        var exception = await Assert.ThrowsAsync<BadFilterException>(
            () => service.GetFilteredAsync(10, 1, "broken=1"));

        Assert.Equal("broken=1", exception.Filter);
    }

    [Fact]
    public async Task TestGetFilteredAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        var backend = new HeaderStubBackend { Status = HttpStatusCode.NoContent, Body = "" };
        var service = ResolveWithHeaders(backend);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => service.GetFilteredAsync(10, 1, ""));
    }

    [Fact]
    public async Task TestGetFilteredAsyncWrapsATransportFailure()
    {
        var backend = new HeaderStubBackend { FailTransport = true };
        var service = ResolveWithHeaders(backend);

        await Assert.ThrowsAsync<RestComunicationException>(() => service.GetFilteredAsync(10, 1, ""));
    }

    // ---------------------------------------------------------------- GetOne

    [Fact]
    public void TestGetOneReturnsTheVulnerabilityAndAsksForItsDetails()
    {
        _backend.OnGet("/Vulnerabilities/11", Vuln(11, "Detailed"));

        var result = _service.GetOne(11);

        Assert.Equal(11, result.Id);
        Assert.Equal("Detailed", result.Title);
        Assert.Equal("/Vulnerabilities/11", _backend.LastRequest.Path);
        Assert.Contains("includeDetails=true", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetOneThrowsWhenTheVulnerabilityIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/404", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetOne(404));
    }

    [Fact]
    public void TestGetOneWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/12", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetOne(12));
    }

    [Fact]
    public async Task TestGetOneAsyncReturnsTheVulnerability()
    {
        _backend.OnGet("/Vulnerabilities/13", Vuln(13, "Async one"));

        var result = await _service.GetOneAsync(13);

        Assert.Equal(13, result.Id);
        Assert.Equal("Async one", result.Title);
        Assert.Contains("includeDetails=true", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetOneAsyncThrowsWhenTheVulnerabilityIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/14", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetOneAsync(14));
    }

    [Fact]
    public async Task TestGetOneAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/15", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetOneAsync(15));
    }

    // --------------------------------------------------------- GetRisksScores

    [Fact]
    public void TestGetRisksScoresReturnsTheScores()
    {
        _backend.OnGet("/Vulnerabilities/3/RisksScores", TwoScores());

        var result = _service.GetRisksScores(3);

        Assert.Equal(2, result.Count);
        Assert.Equal(6.5f, result[0].CalculatedRisk);
        Assert.Equal("GET /Vulnerabilities/3/RisksScores", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRisksScoresThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/3/RisksScores", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetRisksScores(3));
    }

    [Fact]
    public void TestGetRisksScoresWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/3/RisksScores", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksScores(3));
    }

    [Fact]
    public async Task TestGetRisksScoresAsyncReturnsTheScores()
    {
        _backend.OnGet("/Vulnerabilities/4/RisksScores", TwoScores());

        var result = await _service.GetRisksScoresAsync(4);

        Assert.Equal(2, result.Count);
        Assert.Equal(2.5f, result[1].CalculatedRisk);
    }

    [Fact]
    public async Task TestGetRisksScoresAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/4/RisksScores", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRisksScoresAsync(4));
    }

    [Fact]
    public async Task TestGetRisksScoresAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Vulnerabilities/4/RisksScores");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksScoresAsync(4));
    }

    // ------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsyncPostsTheVulnerabilityAndReturnsTheSavedOne()
    {
        _backend.OnPost("/Vulnerabilities", Vuln(99, "Created"));

        var created = await _service.CreateAsync(Vuln(0, "Created"));

        Assert.Equal(99, created.Id);
        Assert.Equal("Created", created.Title);
        Assert.Equal("POST /Vulnerabilities", _backend.LastRequest.ToString());
        Assert.Contains("Created", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.CreateAsync(Vuln()));
    }

    [Fact]
    public async Task TestCreateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(Vuln()));
    }

    // --------------------------------------------------------------- FindAsync

    [Fact]
    public async Task TestFindAsyncReturnsTheMatchWhenTheHashIsKnown()
    {
        _backend.OnGet("/Vulnerabilities/Find", Vuln(21, "Found"));

        var (found, vulnerability) = await _service.FindAsync("deadbeef");

        Assert.True(found);
        Assert.NotNull(vulnerability);
        Assert.Equal(21, vulnerability.Id);
        Assert.Equal("/Vulnerabilities/Find", _backend.LastRequest.Path);
        Assert.Contains("hash=deadbeef", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestFindAsyncReturnsFalseWhenTheHashIsUnknown()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/Find", HttpStatusCode.NotFound);

        var (found, vulnerability) = await _service.FindAsync("nothing");

        Assert.False(found);
        Assert.Null(vulnerability);
    }

    [Fact]
    public async Task TestFindAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/Find", HttpStatusCode.NoContent);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.FindAsync("weird"));
    }

    [Fact]
    public async Task TestFindAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/Find", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.FindAsync("boom"));
    }

    // ------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsyncPutsTheVulnerabilityThenAssociatesItsRisks()
    {
        _backend.On(Method.Put, "/Vulnerabilities/31", "", HttpStatusCode.OK);
        _backend.On(Method.Post, "/Vulnerabilities/31/RisksAssociate", "", HttpStatusCode.OK);

        var vulnerability = Vuln(31, "To update");
        vulnerability.Risks = new List<Risk> { new() { Id = 3 }, new() { Id = 4 } };
        vulnerability.FixRequests = new List<FixRequest> { new() { Id = 1 } };

        await _service.UpdateAsync(vulnerability);

        Assert.Equal(2, _backend.Requests.Count);
        Assert.Equal("PUT /Vulnerabilities/31", _backend.Requests[0].ToString());
        Assert.Equal("POST /Vulnerabilities/31/RisksAssociate", _backend.Requests[1].ToString());
        Assert.Equal("[3,4]", _backend.Requests[1].Body);
        // The risks and fix requests are stripped from the payload before it goes on the wire.
        Assert.Empty(vulnerability.Risks);
        Assert.Empty(vulnerability.FixRequests);
    }

    [Fact]
    public async Task TestUpdateAsyncAssociatesAnEmptyListWhenThereAreNoRisks()
    {
        _backend.On(Method.Put, "/Vulnerabilities/32", "", HttpStatusCode.OK);
        _backend.On(Method.Post, "/Vulnerabilities/32/RisksAssociate", "", HttpStatusCode.OK);

        var vulnerability = Vuln(32, "No risks");
        vulnerability.Risks = null!;

        await _service.UpdateAsync(vulnerability);

        Assert.Equal("[]", _backend.Requests[1].Body);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsOnAnUnexpectedSuccessStatusAndSkipsTheAssociation()
    {
        _backend.On(Method.Put, "/Vulnerabilities/33", "", HttpStatusCode.NoContent);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.UpdateAsync(Vuln(33)));

        Assert.False(_backend.Sent(Method.Post, "/Vulnerabilities/33/RisksAssociate"));
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Vulnerabilities/34", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(Vuln(34)));
    }

    // ----------------------------------------------------- AssociateRisksAsync

    [Fact]
    public async Task TestAssociateRisksAsyncPostsTheRiskIds()
    {
        _backend.On(Method.Post, "/Vulnerabilities/41/RisksAssociate", "", HttpStatusCode.OK);

        await _service.AssociateRisksAsync(41, [7, 8, 9]);

        Assert.Equal("POST /Vulnerabilities/41/RisksAssociate", _backend.LastRequest.ToString());
        Assert.Equal("[7,8,9]", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestAssociateRisksAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities/42/RisksAssociate", HttpStatusCode.NoContent);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.AssociateRisksAsync(42, [1]));
    }

    [Fact]
    public async Task TestAssociateRisksAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities/43/RisksAssociate", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.AssociateRisksAsync(43, [1]));
    }

    // ------------------------------------------------------------------ Delete

    [Fact]
    public void TestDeleteSendsTheDeleteRequest()
    {
        _backend.OnDelete("/Vulnerabilities/51", "");

        _service.Delete(Vuln(51));

        Assert.Equal("DELETE /Vulnerabilities/51", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Delete, "/Vulnerabilities/52", HttpStatusCode.NoContent);

        Assert.Throws<InvalidHttpRequestException>(() => _service.Delete(Vuln(52)));
    }

    [Fact]
    public void TestDeleteWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Vulnerabilities/53", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Delete(Vuln(53)));
    }

    // ------------------------------------------------------------ UpdateStatus

    [Fact]
    public async Task TestUpdateStatusAsyncPutsTheStatusAsARawJsonValue()
    {
        _backend.On(Method.Put, "/Vulnerabilities/61/Status", "", HttpStatusCode.OK);

        await _service.UpdateStatusAsync(61, 4);

        Assert.Equal("PUT /Vulnerabilities/61/Status", _backend.LastRequest.ToString());
        // AddJsonBody with a string sends it verbatim rather than re-serializing it.
        Assert.Equal("4", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateStatusAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Put, "/Vulnerabilities/62/Status", HttpStatusCode.NoContent);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.UpdateStatusAsync(62, 1));
    }

    [Fact]
    public async Task TestUpdateStatusAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Vulnerabilities/63/Status", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateStatusAsync(63, 1));
    }

    [Fact]
    public void TestUpdateStatusRunsTheAsyncCallSynchronously()
    {
        _backend.On(Method.Put, "/Vulnerabilities/64/Status", "", HttpStatusCode.OK);

        _service.UpdateStatus(64, 9);

        Assert.Equal("PUT /Vulnerabilities/64/Status", _backend.LastRequest.ToString());
        Assert.Equal("9", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateStatusPropagatesTheWrappedServerError()
    {
        _backend.OnStatus(Method.Put, "/Vulnerabilities/65/Status", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.UpdateStatus(65, 1));
    }

    // ---------------------------------------------------- UpdateCommentsAsync

    [Fact]
    public async Task TestUpdateCommentsAsyncPutsTheCommentDto()
    {
        _backend.On(Method.Put, "/Vulnerabilities/71/Comments", "", HttpStatusCode.OK);

        _service.UpdateCommentsAsync(71, "needs a second look");

        // KNOWN LIMITATION: the method is declared `async void`, so it returns before the request
        // is made and a caller can neither await it nor observe its failures — an exception on any
        // of its branches escapes to the process instead of the caller. Only the happy path is
        // therefore exercisable here; see the notes on this test file's subject.
        for (var i = 0; i < 200 && _backend.Requests.Count == 0; i++) await Task.Delay(10);

        Assert.Single(_backend.Requests);
        Assert.Equal("PUT /Vulnerabilities/71/Comments", _backend.Requests[0].ToString());
        Assert.Contains("needs a second look", _backend.Requests[0].Body);
    }

    // ----------------------------------------------------------- AddActionAsync

    [Fact]
    public async Task TestAddActionAsyncPostsTheActionAndReturnsTheCreatedOne()
    {
        var created = new NrAction
        {
            Id = 77,
            ObjectType = "vulnerability",
            Message = "reviewed",
            UserId = 5,
            DateTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        _backend.On(Method.Post, "/Vulnerabilities/81/Actions", created, HttpStatusCode.Created);

        var result = await _service.AddActionAsync(81, 5, new NrAction
        {
            ObjectType = "vulnerability",
            Message = "reviewed",
            UserId = 5,
            DateTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal(77, result.Id);
        Assert.Equal("reviewed", result.Message);
        Assert.Equal("POST /Vulnerabilities/81/Actions", _backend.LastRequest.ToString());
        Assert.Contains("reviewed", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestAddActionAsyncThrowsWhenTheServerDoesNotReportCreated()
    {
        _backend.On(Method.Post, "/Vulnerabilities/82/Actions", "", HttpStatusCode.OK);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.AddActionAsync(82, 5, new NrAction { ObjectType = "vulnerability" }));
    }

    [Fact]
    public async Task TestAddActionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities/83/Actions", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.AddActionAsync(83, 5, new NrAction { ObjectType = "vulnerability" }));
    }

    // ------------------------------------------------------- ImportNessusAsync

    [Fact]
    public async Task TestImportNessusAsyncStartsTheImport()
    {
        _backend.On(Method.Post, "/Vulnerabilities/import/nessus/scan-1", "", HttpStatusCode.OK);

        await _service.ImportNessusAsync("scan-1");

        Assert.Equal("POST /Vulnerabilities/import/nessus/scan-1", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestImportNessusAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities/import/nessus/scan-2", HttpStatusCode.Accepted);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.ImportNessusAsync("scan-2"));
    }

    [Fact]
    public async Task TestImportNessusAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Vulnerabilities/import/nessus/scan-3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.ImportNessusAsync("scan-3"));
    }

    // ---------------------------------------------------- GetLastScanDateAsync

    [Fact]
    public async Task TestGetLastScanDateAsyncReturnsTheDeserializedDate()
    {
        _backend.OnGet("/Vulnerabilities/LastScanDate", "\"2026-01-15T10:30:00\"");

        var date = await _service.GetLastScanDateAsync();

        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0), date);
        Assert.Equal("GET /Vulnerabilities/LastScanDate", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetLastScanDateAsyncThrowsWhenTheServerHasNoDate()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/LastScanDate", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetLastScanDateAsync());
    }

    [Fact]
    public async Task TestGetLastScanDateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Vulnerabilities/LastScanDate", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetLastScanDateAsync());
    }

    [Fact]
    public async Task TestGetLastScanDateAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Vulnerabilities/LastScanDate");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetLastScanDateAsync());
    }

    // -------------------------------------------------------------- test rig

    private static IVulnerabilitiesService ResolveWithHeaders(HeaderStubBackend backend)
    {
        return global::ClientServices.Tests.DI.ServiceRegistration
            .GetServiceProvider(s => s.AddSingleton<IRestService>(backend))
            .GetRequiredService<IVulnerabilitiesService>();
    }

    /// <summary>
    /// A one-route backend that can also set a response <b>header</b>. <see cref="StubRestBackend"/>
    /// only carries a status and a body, and <c>GetFiltered</c>/<c>GetFilteredAsync</c> read the
    /// page total off the <c>X-Total-Count</c> response header, so those two methods need this.
    /// </summary>
    private sealed class HeaderStubBackend : IRestService
    {
        private const string BaseUrl = "https://localhost:5443";

        private readonly HttpClient _httpClient;

        public HeaderStubBackend()
        {
            _httpClient = new HttpClient(new Handler(this)) { BaseAddress = new Uri(BaseUrl) };
        }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "[]";
        public string? TotalCount { get; init; } = "0";
        public bool FailTransport { get; init; }

        public string LastPath { get; private set; } = "";
        public string LastQuery { get; private set; } = "";

        private sealed class Handler(HeaderStubBackend backend) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                backend.LastPath = request.RequestUri!.AbsolutePath;
                backend.LastQuery = request.RequestUri.Query;

                if (backend.FailTransport)
                    return Task.FromException<HttpResponseMessage>(
                        new HttpRequestException("simulated transport failure"));

                var response = new HttpResponseMessage(backend.Status)
                {
                    Content = new StringContent(backend.Body, Encoding.UTF8, "application/json")
                };

                if (backend.TotalCount != null)
                    response.Headers.TryAddWithoutValidation("X-Total-Count", backend.TotalCount);

                return Task.FromResult(response);
            }
        }

        private RestClient NewClient() => new(
            _httpClient,
            new RestClientOptions(BaseUrl) { ThrowOnAnyError = false },
            disposeHttpClient: false);

        public RestClient GetClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false)
            => NewClient();

        public IRestClient GetReliableClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false)
            => NewClient();
    }
}
