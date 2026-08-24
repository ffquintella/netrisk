using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Dashboard;
using Model.Exceptions;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(DashboardRestService))]
public class DashboardRestServiceTest : BaseServiceTest
{
    private const string Path = "/Dashboard/Master";

    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IDashboardService _service;

    public DashboardRestServiceTest()
    {
        // A double for the authentication service, so the 401 branch can be observed instead of
        // reaching the real token store.
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IDashboardService>();
    }

    private static MasterDashboard ADashboard() => new()
    {
        GeneratedAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
        FromCache = true,
        Entities = new List<EntityPostureSummary>
        {
            new()
            {
                EntityId = 1, EntityName = "HQ", EntityStatus = "Active",
                OpenRisks = 10, RisksHigh = 3, RisksMedium = 4, RisksLow = 3, AverageRiskScore = 6.5,
                OpenVulnerabilities = 20, VulnerabilitiesCritical = 2, VulnerabilitiesHigh = 5,
                VulnerabilitiesMedium = 8, VulnerabilitiesLow = 5, OpenIncidents = 1, PostureScore = 72.25
            },
            new()
            {
                EntityId = null, EntityName = "Unassigned", OpenRisks = 2, PostureScore = 5
            }
        },
        Totals = new EntityPostureSummary { EntityName = "Totals", OpenRisks = 12, OpenVulnerabilities = 20 }
    };

    [Fact]
    public async Task TestGetMasterDashboardAsync()
    {
        _backend.OnGet(Path, ADashboard());

        var dashboard = await _service.GetMasterDashboardAsync();

        Assert.True(dashboard.FromCache);
        Assert.Equal(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc), dashboard.GeneratedAt);
        Assert.Equal(2, dashboard.Entities.Count);
        Assert.Equal("HQ", dashboard.Entities[0].EntityName);
        Assert.Equal(6.5, dashboard.Entities[0].AverageRiskScore);
        Assert.Equal(72.25, dashboard.Entities[0].PostureScore);
        Assert.Null(dashboard.Entities[1].EntityId);
        Assert.Equal(12, dashboard.Totals.OpenRisks);
        Assert.Equal("GET " + Path, _backend.LastRequest.ToString());
        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncAsksForARefresh()
    {
        _backend.OnGet(Path, ADashboard());

        await _service.GetMasterDashboardAsync(refresh: true);

        Assert.Contains("refresh=true", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetMasterDashboardAsync());
        Assert.Equal(Path, ex.Url);
        Assert.Equal("Error getting the master dashboard", ex.Message);
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncReportsAForbiddenAnswerAsAnInvalidRequest()
    {
        // The endpoint is admin-only: a 403 is an ordinary outcome for a non-admin, and the service
        // keeps it distinguishable from a transport fault.
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetMasterDashboardAsync());
        Assert.Equal("Not authorized to view the master dashboard", ex.Message);
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncDiscardsTheTokenOnUnauthorized()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMasterDashboardAsync());
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMasterDashboardAsync());
        Assert.Equal("Error getting the master dashboard", ex.RestExceptionMessage);
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestGetMasterDashboardAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, Path);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMasterDashboardAsync());
    }
}
