using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="HostsRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every status branch and its in-process cache are exercised for real.
///
/// <see cref="HostsRestServiceTest"/> already covers the happy path of
/// <c>GetAllHostServiceAsync</c> and <c>GetAllHostVulnerabilitiesAsync</c> through the older shared
/// mock; this class covers everything else.
/// </summary>
[TestSubject(typeof(HostsRestService))]
public class HostsRestServiceStubTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IHostsService _service;

    public HostsRestServiceStubTest()
    {
        _service = ResolveWith<IHostsService>(_backend);
    }

    private static Host AHost(int id = 1, string hostName = "alpha", string ip = "10.0.0.1") => new()
    {
        Id = id,
        Ip = ip,
        HostName = hostName,
        Status = 1,
        Source = "manual",
        RegistrationDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static HostsService AService(int id = 3, int hostId = 1, string name = "ssh") => new()
    {
        Id = id,
        HostId = hostId,
        Name = name,
        Protocol = "tcp",
        Port = 22
    };

    private static Vulnerability AVulnerability(int id = 1, string title = "Open port") => new()
    {
        Id = id,
        Title = title,
        Severity = "high",
        Score = 7.5,
        Status = 1,
        DetectionCount = 2,
        FirstDetection = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// <c>UpdateAsync</c> and <c>Delete</c> are declared <c>async void</c>, so the caller cannot
    /// await them. The exchange is still recorded synchronously inside the handler, so waiting for
    /// the recording is enough to assert what was sent. Both are only ever stubbed with the status
    /// they treat as success — an <c>async void</c> that throws would surface as an unhandled
    /// exception on the thread pool rather than a test failure.
    /// </summary>
    private async Task<RecordedRequest> WaitForRequestAsync()
    {
        for (var attempt = 0; attempt < 200 && _backend.Requests.Count == 0; attempt++)
            await Task.Delay(5);

        Assert.NotEmpty(_backend.Requests);
        return _backend.LastRequest;
    }

    // ---------------------------------------------------------------- GetOne

    [Fact]
    public void TestGetOneReturnsTheHost()
    {
        _backend.OnGet("/Hosts/7", AHost(7, "seven"));

        var host = _service.GetOne(7);

        Assert.NotNull(host);
        Assert.Equal(7, host.Id);
        Assert.Equal("seven", host.HostName);
        Assert.Equal("GET /Hosts/7", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetOneServesASecondCallFromTheCache()
    {
        _backend.OnGet("/Hosts/7", AHost(7, "seven"));

        var first = _service.GetOne(7);
        var second = _service.GetOne(7);

        Assert.Same(first, second);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestGetOneUsesTheFullCacheFilledByGetAllAsync()
    {
        _backend.OnGet("/Hosts", new List<Host> { AHost(1, "alpha"), AHost(2, "beta") });

        await _service.GetAllAsync();
        var host = _service.GetOne(2);
        var missing = _service.GetOne(99);

        Assert.NotNull(host);
        Assert.Equal("beta", host.HostName);
        // Once the full list is cached an unknown id is answered locally with null - no request.
        Assert.Null(missing);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public void TestGetOneThrowsWhenTheHostIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Hosts/9", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetOne(9));
    }

    [Fact]
    public void TestGetOneWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/9", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetOne(9));
    }

    [Fact]
    public void TestGetOneWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Hosts/9");

        Assert.Throws<RestComunicationException>(() => _service.GetOne(9));
    }

    // ------------------------------------------------------- GetAll/GetAllAsync

    [Fact]
    public async Task TestGetAllAsyncOrdersTheHostsByName()
    {
        _backend.OnGet("/Hosts", new List<Host> { AHost(1, "zeta"), AHost(2, "alpha") });

        var hosts = await _service.GetAllAsync();

        Assert.Equal(2, hosts.Count);
        Assert.Equal("alpha", hosts[0].HostName);
        Assert.Equal("zeta", hosts[1].HostName);
        Assert.Equal("GET /Hosts", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncCachesTheWholeList()
    {
        _backend.OnGet("/Hosts", new List<Host> { AHost() });

        var first = await _service.GetAllAsync();
        var second = await _service.GetAllAsync();

        Assert.Same(first, second);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Hosts", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Hosts");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public void TestGetAllRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Hosts", new List<Host> { AHost(1, "zeta"), AHost(2, "alpha") });

        var hosts = _service.GetAll();

        Assert.Equal(2, hosts.Count);
        Assert.Equal("alpha", hosts[0].HostName);
    }

    [Fact]
    public void TestGetAllPropagatesTheWrappedServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetAll());
    }

    // ------------------------------------------------------------ GetFilteredAsync

    [Fact]
    public async Task TestGetFilteredAsyncSendsPagingCultureAndFilter()
    {
        _backend.OnGet("/Hosts/Filtered", new List<Host> { AHost(1, "zeta"), AHost(2, "alpha") });

        var hosts = await _service.GetFilteredAsync(10, 2, "HostName==alpha");

        Assert.Equal(2, hosts.Count);
        Assert.Equal("alpha", hosts[0].HostName);
        Assert.Equal("/Hosts/Filtered", _backend.LastRequest.Path);
        Assert.Contains("page=2", _backend.LastRequest.Query);
        Assert.Contains("pageSize=10", _backend.LastRequest.Query);
        Assert.Contains("culture=", _backend.LastRequest.Query);
        Assert.Contains("filters=", _backend.LastRequest.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task TestGetFilteredAsyncOmitsAnEmptyFilter(string? filter)
    {
        _backend.OnGet("/Hosts/Filtered", new List<Host> { AHost() });

        var hosts = await _service.GetFilteredAsync(25, 1, filter);

        Assert.Single(hosts);
        Assert.DoesNotContain("filters=", _backend.LastRequest.Query);
        Assert.Contains("pageSize=25", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetFilteredAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Filtered", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetFilteredAsync(10, 1, null));
    }

    [Fact]
    public async Task TestGetFilteredAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Filtered", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetFilteredAsync(10, 1, null));
    }

    // ------------------------------------------------------------------- Create

    [Fact]
    public async Task TestCreateReturnsTheCreatedHost()
    {
        _backend.OnPost("/Hosts", AHost(11, "eleven"), HttpStatusCode.Created);

        var created = await _service.Create(AHost(0, "eleven"));

        Assert.NotNull(created);
        Assert.Equal(11, created.Id);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/Hosts", _backend.LastRequest.Path);
        Assert.Contains("eleven", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateThrowsWhenTheServerDoesNotReportCreated()
    {
        // The service accepts nothing but 201; a plain 200 is treated as a failure.
        _backend.OnPost("/Hosts", AHost(11));

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.Create(AHost(0)));
    }

    [Fact]
    public async Task TestCreateThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Post, "/Hosts", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.Create(AHost(0)));
    }

    [Fact]
    public async Task TestCreateWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Hosts", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.Create(AHost(0)));
    }

    [Fact]
    public async Task TestCreateWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Hosts");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.Create(AHost(0)));
    }

    // --------------------------------------------------------------- HostExists

    [Fact]
    public void TestHostExistsIsTrueWhenTheServerAnswersOk()
    {
        _backend.OnGet("/Hosts/Find", AHost());

        Assert.True(_service.HostExists("10.0.0.1"));
        Assert.Equal("/Hosts/Find", _backend.LastRequest.Path);
        Assert.Contains("ip=10.0.0.1", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestHostExistsIsFalseWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.NotFound);

        Assert.False(_service.HostExists("10.0.0.9"));
    }

    [Fact]
    public void TestHostExistsThrowsOnAnUnexpectedSuccessStatus()
    {
        // Anything other than 200 or 404 is neither a yes nor a no.
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.Accepted);

        Assert.Throws<InvalidHttpRequestException>(() => _service.HostExists("10.0.0.1"));
    }

    [Fact]
    public void TestHostExistsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.HostExists("10.0.0.1"));
    }

    [Fact]
    public void TestHostExistsRejectsANullIp()
    {
        Assert.Throws<ArgumentNullException>(() => _service.HostExists(null!));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestHostExistsAsyncIsTrueWhenTheServerAnswersOk()
    {
        _backend.OnGet("/Hosts/Find", AHost());

        Assert.True(await _service.HostExistsAsync("10.0.0.1"));
    }

    [Fact]
    public async Task TestHostExistsAsyncIsFalseWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.NotFound);

        Assert.False(await _service.HostExistsAsync("10.0.0.9"));
    }

    [Fact]
    public async Task TestHostExistsAsyncThrowsOnAnUnexpectedSuccessStatus()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.Accepted);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.HostExistsAsync("10.0.0.1"));
    }

    [Fact]
    public async Task TestHostExistsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Hosts/Find");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.HostExistsAsync("10.0.0.1"));
    }

    [Fact]
    public async Task TestHostExistsAsyncRejectsANullIp()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.HostExistsAsync(null!));
    }

    // ------------------------------------------------------------------ GetByIp

    [Fact]
    public void TestGetByIpReturnsTheHost()
    {
        _backend.OnGet("/Hosts/Find", AHost(4, "four", "10.0.0.4"));

        var host = _service.GetByIp("10.0.0.4");

        Assert.NotNull(host);
        Assert.Equal(4, host.Id);
        Assert.Contains("ip=10.0.0.4", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetByIpThrowsWhenNothingIsFound()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetByIp("10.0.0.9"));
    }

    [Fact]
    public void TestGetByIpWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetByIp("10.0.0.1"));
    }

    [Fact]
    public void TestGetByIpRejectsANullIp()
    {
        Assert.Throws<ArgumentNullException>(() => _service.GetByIp(null!));
    }

    [Fact]
    public async Task TestGetByIpAsyncReturnsTheHost()
    {
        _backend.OnGet("/Hosts/Find", AHost(4, "four", "10.0.0.4"));

        var host = await _service.GetByIpAsync("10.0.0.4");

        Assert.Equal("four", host.HostName);
    }

    [Fact]
    public async Task TestGetByIpAsyncThrowsWhenNothingIsFound()
    {
        _backend.OnStatus(Method.Get, "/Hosts/Find", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetByIpAsync("10.0.0.9"));
    }

    [Fact]
    public async Task TestGetByIpAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Hosts/Find");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIpAsync("10.0.0.1"));
    }

    [Fact]
    public async Task TestGetByIpAsyncRejectsANullIp()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetByIpAsync(null!));
    }

    // ------------------------------------------------------------ UpdateAsync/Delete

    [Fact]
    public async Task TestUpdateAsyncSendsThePutToTheHostRoute()
    {
        _backend.OnPut("/Hosts/5", "");

        _service.UpdateAsync(AHost(5, "five"));
        var request = await WaitForRequestAsync();

        Assert.Equal("PUT /Hosts/5", request.ToString());
        Assert.Contains("five", request.Body);
    }

    [Fact]
    public async Task TestDeleteSendsTheDeleteToTheHostRoute()
    {
        _backend.OnDelete("/Hosts/5", "");

        _service.Delete(5);
        var request = await WaitForRequestAsync();

        Assert.Equal("DELETE /Hosts/5", request.ToString());
    }

    // ------------------------------------------------------------- GetHostService

    [Fact]
    public void TestGetHostServiceReturnsTheService()
    {
        _backend.OnGet("/Hosts/1/Services/3", AService());

        var service = _service.GetHostService(1, 3);

        Assert.Equal(3, service.Id);
        Assert.Equal("ssh", service.Name);
        Assert.Equal(22, service.Port);
        Assert.Equal("GET /Hosts/1/Services/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetHostServiceThrowsWhenTheServiceIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/3", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetHostService(1, 3));
    }

    [Fact]
    public void TestGetHostServiceWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/3", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetHostService(1, 3));
    }

    // --------------------------------------------------------- GetAllHostService

    [Fact]
    public void TestGetAllHostServiceReturnsTheServices()
    {
        _backend.OnGet("/Hosts/1/Services", new List<HostsService> { AService(3), AService(4, 1, "http") });

        var services = _service.GetAllHostService(1);

        Assert.Equal(2, services.Count);
        Assert.Equal("http", services[1].Name);
        Assert.Equal("GET /Hosts/1/Services", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllHostServiceAsyncWrapsAMissingResponse()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services", HttpStatusCode.NotFound);

        // Known limitation: GetAllHostServiceAsync catches Exception rather than
        // HttpRequestException, so the InvalidHttpRequestException it raises for a null response is
        // swallowed and re-wrapped as RestComunicationException. Asserted as-is.
        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllHostServiceAsync(1));
    }

    [Fact]
    public async Task TestGetAllHostServiceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllHostServiceAsync(1));
    }

    // -------------------------------------------------- GetAllHostVulnerabilities

    [Fact]
    public void TestGetAllHostVulnerabilitiesReturnsTheVulnerabilities()
    {
        _backend.OnGet("/Hosts/1/Vulnerabilities",
            new List<Vulnerability> { AVulnerability(1, "Open port"), AVulnerability(2, "Weak cipher") });

        var vulnerabilities = _service.GetAllHostVulnerabilities(1);

        Assert.Equal(2, vulnerabilities.Count);
        Assert.Equal("Weak cipher", vulnerabilities[1].Title);
        Assert.Equal("GET /Hosts/1/Vulnerabilities", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAllHostVulnerabilitiesThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Vulnerabilities", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetAllHostVulnerabilities(1));
    }

    [Fact]
    public void TestGetAllHostVulnerabilitiesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Vulnerabilities", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetAllHostVulnerabilities(1));
    }

    [Fact]
    public async Task TestGetAllHostVulnerabilitiesAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Vulnerabilities", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllHostVulnerabilitiesAsync(1));
    }

    [Fact]
    public async Task TestGetAllHostVulnerabilitiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Vulnerabilities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllHostVulnerabilitiesAsync(1));
    }

    // ------------------------------------------------------- HostHasServiceAsync

    [Fact]
    public async Task TestHostHasServiceAsyncReturnsTheServerAnswer()
    {
        _backend.OnGet("/Hosts/1/Services/Exists", true);

        Assert.True(await _service.HostHasServiceAsync(1, "ssh", 22, "tcp"));
        Assert.Equal("/Hosts/1/Services/Exists", _backend.LastRequest.Path);
        Assert.Contains("name=ssh", _backend.LastRequest.Query);
        Assert.Contains("port=22", _backend.LastRequest.Query);
        Assert.Contains("protocol=tcp", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestHostHasServiceAsyncOmitsThePortWhenItIsNull()
    {
        _backend.OnGet("/Hosts/1/Services/Exists", false);

        Assert.False(await _service.HostHasServiceAsync(1, "ssh", null, "tcp"));
        Assert.DoesNotContain("port=", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestHostHasServiceAsyncIsFalseWhenTheServerAnswersNotFound()
    {
        // A 404 with no body deserializes to default(bool).
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/Exists", HttpStatusCode.NotFound);

        Assert.False(await _service.HostHasServiceAsync(1, "ssh", 22, "tcp"));
    }

    [Fact]
    public async Task TestHostHasServiceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/Exists", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.HostHasServiceAsync(1, "ssh", 22, "tcp"));
    }

    // --------------------------------------------------- CreateAndAddServiceAsync

    [Fact]
    public async Task TestCreateAndAddServiceAsyncReturnsTheCreatedService()
    {
        _backend.OnPost("/Hosts/1/Services", AService(9, 1, "ftp"));

        var created = await _service.CreateAndAddServiceAsync(1,
            new HostsServiceDto { Name = "ftp", Port = 21, Protocol = "tcp" });

        Assert.Equal(9, created.Id);
        Assert.Equal("ftp", created.Name);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/Hosts/1/Services", _backend.LastRequest.Path);
        Assert.Contains("ftp", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAndAddServiceAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, "/Hosts/1/Services", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateAndAddServiceAsync(1, new HostsServiceDto { Name = "ftp" }));
    }

    [Fact]
    public async Task TestCreateAndAddServiceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Hosts/1/Services", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateAndAddServiceAsync(1, new HostsServiceDto { Name = "ftp" }));
    }

    // ------------------------------------------------------------- DeleteService

    [Fact]
    public void TestDeleteServiceThrowsWhenTheServerReportsSuccess()
    {
        _backend.OnDelete("/Hosts/1/Services/3", "");

        // Known bug: the guard reads `if (response.StatusCode == HttpStatusCode.OK)` where every
        // sibling method uses `!=`, so a successful delete is reported as a failure and a failed one
        // passes silently. Asserted as-is rather than fixed here.
        Assert.Throws<InvalidHttpRequestException>(() => _service.DeleteService(1, 3));
        Assert.Equal("DELETE /Hosts/1/Services/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteServiceIsSilentWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Delete, "/Hosts/1/Services/3", HttpStatusCode.NotFound);

        // Same inverted guard as above, seen from the other side.
        _service.DeleteService(1, 3);

        Assert.True(_backend.Sent(Method.Delete, "/Hosts/1/Services/3"));
    }

    [Fact]
    public void TestDeleteServiceWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Hosts/1/Services/3", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.DeleteService(1, 3));
    }

    // ------------------------------------------------------------- UpdateService

    [Fact]
    public void TestUpdateServiceSendsThePutWithTheServiceBody()
    {
        _backend.OnPut("/Hosts/1/Services/3", "");

        _service.UpdateService(1, new HostsServiceDto { Id = 3, Name = "ssh", Port = 22, Protocol = "tcp" });

        Assert.Equal("PUT /Hosts/1/Services/3", _backend.LastRequest.ToString());
        Assert.Contains("ssh", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateServiceThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Put, "/Hosts/1/Services/3", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(
            () => _service.UpdateService(1, new HostsServiceDto { Id = 3, Name = "ssh" }));
    }

    [Fact]
    public void TestUpdateServiceWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Hosts/1/Services/3", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(
            () => _service.UpdateService(1, new HostsServiceDto { Id = 3, Name = "ssh" }));
    }

    // --------------------------------------------------------- FindServiceAsync

    [Fact]
    public async Task TestFindServiceAsyncReturnsTheMatchingService()
    {
        _backend.OnGet("/Hosts/1/Services/Find", AService(5, 1, "ssh"));

        var found = await _service.FindServiceAsync(1, "ssh", 22, "tcp");

        Assert.NotNull(found);
        Assert.Equal(5, found.Id);
        Assert.Equal("/Hosts/1/Services/Find", _backend.LastRequest.Path);
        Assert.Contains("name=ssh", _backend.LastRequest.Query);
        Assert.Contains("port=22", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestFindServiceAsyncReturnsNullWhenNothingMatches()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/Find", HttpStatusCode.NotFound);

        Assert.Null(await _service.FindServiceAsync(1, "ssh", 22, "tcp"));
    }

    [Fact]
    public async Task TestFindServiceAsyncOmitsThePortWhenItIsNull()
    {
        _backend.OnGet("/Hosts/1/Services/Find", AService(5, 1, "ssh"));

        await _service.FindServiceAsync(1, "ssh", null, "tcp");

        Assert.DoesNotContain("port=", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestFindServiceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Hosts/1/Services/Find", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.FindServiceAsync(1, "ssh", 22, "tcp"));
    }
}
