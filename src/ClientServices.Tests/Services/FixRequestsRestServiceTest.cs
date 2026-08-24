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

[TestSubject(typeof(FixRequestsRestService))]
public class FixRequestsRestServiceTest : BaseServiceTest
{
    private const string CreatePath = "/FixRequest";
    private const string VulnerabilitiesPath = "/FixRequest/vulnerabilities";

    private readonly StubRestBackend _backend = new();
    private readonly IFixRequestsService _service;

    public FixRequestsRestServiceTest()
    {
        _service = ResolveWith<IFixRequestsService>(_backend);
    }

    private static FixRequestDto ADto() => new()
    {
        VulnerabilityId = 12,
        Comments = "please patch",
        Destination = "ops@example.org",
        FixTeamId = 4,
        Identifier = "FR-0001"
    };

    private static FixRequest ASavedRequest() => new()
    {
        Id = 77,
        VulnerabilityId = 12,
        Identifier = "FR-0001",
        CreationDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = 1,
        FixTeamId = 4,
        IsTeamFix = true,
        SingleFixDestination = "ops@example.org"
    };

    // ---------------- CreateFixRequestAsync ----------------

    [Fact]
    public async Task TestCreateFixRequestAsync()
    {
        _backend.OnPost(CreatePath, ASavedRequest());

        var created = await _service.CreateFixRequestAsync(ADto());

        Assert.Equal(77, created.Id);
        Assert.Equal(12, created.VulnerabilityId);
        Assert.Equal("FR-0001", created.Identifier);
        Assert.Equal(4, created.FixTeamId);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal(CreatePath, _backend.LastRequest.Path);
        Assert.Contains("please patch", _backend.LastRequest.Body);
        Assert.Contains("sendToGroup=false", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestCreateFixRequestAsyncForwardsTheSendToGroupFlag()
    {
        _backend.OnPost(CreatePath, ASavedRequest());

        await _service.CreateFixRequestAsync(ADto(), sendToGroup: true);

        Assert.Contains("sendToGroup=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestCreateFixRequestAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, CreatePath, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateFixRequestAsync(ADto()));
        Assert.Equal("Error creating fix request", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateFixRequestAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, CreatePath, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateFixRequestAsync(ADto()));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateFixRequestAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, CreatePath);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateFixRequestAsync(ADto()));
    }

    // ---------------- GetVulnerabilitiesFixRequestAsync ----------------

    [Fact]
    public async Task TestGetVulnerabilitiesFixRequestAsync()
    {
        _backend.OnPost(VulnerabilitiesPath, new List<FixRequest>
        {
            ASavedRequest(),
            new() { Id = 78, VulnerabilityId = 13, Identifier = "FR-0002", Status = 2 }
        });

        var requests = await _service.GetVulnerabilitiesFixRequestAsync([12, 13]);

        Assert.Equal(2, requests.Count);
        Assert.Equal(77, requests[0].Id);
        Assert.Equal(13, requests[1].VulnerabilityId);
        Assert.Equal("POST " + VulnerabilitiesPath, _backend.LastRequest.ToString());
        Assert.Contains("12", _backend.LastRequest.Body);
        Assert.Contains("13", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesFixRequestAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, VulnerabilitiesPath, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesFixRequestAsync([12]));
        Assert.Equal("Error getting fix requests by vulnerabilities", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesFixRequestAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, VulnerabilitiesPath, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesFixRequestAsync([12]));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesFixRequestAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, VulnerabilitiesPath);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesFixRequestAsync([12]));
    }
}
