using System;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(EmailsRestService))]
public class EmailsRestServiceTest : BaseServiceTest
{
    private const string FixRequestMailPath = "/Email/Vulnerability/FixRequest";

    private readonly StubRestBackend _backend = new();
    private readonly IEmailsService _service;

    public EmailsRestServiceTest()
    {
        _service = ResolveWith<IEmailsService>(_backend);
    }

    private static FixRequestDto ADto() => new()
    {
        VulnerabilityId = 12,
        Comments = "please patch",
        Destination = "ops@example.org",
        FixTeamId = 4,
        Identifier = "FR-0001"
    };

    // ---------------- SendVulnerabilityFixRequestMailAsync ----------------

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailAsync()
    {
        _backend.OnPost(FixRequestMailPath, "\"sent\"");

        await _service.SendVulnerabilityFixRequestMailAsync(ADto());

        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal(FixRequestMailPath, _backend.LastRequest.Path);
        Assert.Contains("please patch", _backend.LastRequest.Body);
        Assert.Contains("ops@example.org", _backend.LastRequest.Body);
        Assert.Contains("sendToGroup=false", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailAsyncForwardsTheSendToGroupFlag()
    {
        _backend.OnPost(FixRequestMailPath, "\"sent\"");

        await _service.SendVulnerabilityFixRequestMailAsync(ADto(), sendToGroup: true);

        Assert.Contains("sendToGroup=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, FixRequestMailPath, HttpStatusCode.NotFound);

        // The guard the method raises for an empty response reaches the caller: only a genuine
        // transport failure is re-wrapped as RestComunicationException.
        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.SendVulnerabilityFixRequestMailAsync(ADto()));
        Assert.Equal("Error sending vulnerability fix request mail", ex.Message);
        Assert.Equal(FixRequestMailPath, ex.Url);
        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, FixRequestMailPath, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SendVulnerabilityFixRequestMailAsync(ADto()));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, FixRequestMailPath);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SendVulnerabilityFixRequestMailAsync(ADto()));
    }

    // ---------------- SendVulnerabilityUpdateMailAsync ----------------

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailAsync()
    {
        _backend.OnPost("/Email/Vulnerability/Update/42", "\"sent\"");

        await _service.SendVulnerabilityUpdateMailAsync(42, "a comment");

        Assert.Equal("POST /Email/Vulnerability/Update/42", _backend.LastRequest.ToString());
        // The comment travels as a bare JSON string, not wrapped in an object.
        Assert.Equal("\"a comment\"", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, "/Email/Vulnerability/Update/42", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.SendVulnerabilityUpdateMailAsync(42, "a comment"));
        Assert.Equal("Error sending vulnerability update mail", ex.Message);
        // The reported URL is the one the request went to, not the fix-request path.
        Assert.Equal("/Email/Vulnerability/Update/42", ex.Url);
        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Email/Vulnerability/Update/42", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SendVulnerabilityUpdateMailAsync(42, "a comment"));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Email/Vulnerability/Update/42");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SendVulnerabilityUpdateMailAsync(42, "a comment"));
    }
}
