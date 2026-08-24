using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using Model.Rest;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="MitigationRestService"/> over <see cref="StubRestBackend"/>.
///
/// The service also takes an <see cref="IAuthenticationService"/>, which it asks to discard the
/// token whenever the server answers 401. The real implementation writes to a LiteDB file under the
/// user's application-data folder, so a substitute is registered here instead — that keeps the
/// Unauthorized branch observable without touching the disk.
/// </summary>
[TestSubject(typeof(MitigationRestService))]
public class MitigationRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IMitigationService _service;

    public MitigationRestServiceTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(services =>
            {
                services.AddSingleton<IRestService>(_backend);
                services.AddSingleton(_authentication);
            })
            .GetRequiredService<IMitigationService>();
    }

    private static Mitigation AMitigation(int id = 5, int riskId = 3) => new()
    {
        Id = id,
        RiskId = riskId,
        SubmissionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningStrategy = 1,
        MitigationEffort = 2,
        MitigationCost = 3,
        MitigationOwner = 4,
        CurrentSolution = "encrypt the export",
        SecurityRequirements = "no plaintext",
        SecurityRecommendations = "rotate the keys",
        SubmittedBy = 7,
        PlanningDate = new DateOnly(2024, 3, 1),
        MitigationPercent = 10
    };

    private static MitigationDto AMitigationDto(int id = 5) => new()
    {
        Id = id,
        RiskId = 3,
        SubmissionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningStrategy = 1,
        MitigationEffort = 2,
        MitigationCost = 3,
        MitigationOwner = 4,
        CurrentSolution = "encrypt the export",
        SecurityRequirements = "no plaintext",
        SecurityRecommendations = "rotate the keys",
        SubmittedBy = 7,
        PlanningDate = new DateOnly(2024, 3, 1),
        MitigationPercent = 10
    };

    // ------------------------------------------------------------- GetTeamsById

    [Fact]
    public void TestGetTeamsByIdReturnsTheTeams()
    {
        _backend.OnGet("/Mitigations/5/Teams", new List<Team>
        {
            new() { Value = 1, Name = "Infra" },
            new() { Value = 2, Name = "AppSec" }
        });

        var teams = _service.GetTeamsById(5);

        Assert.NotNull(teams);
        Assert.Equal(2, teams.Count);
        Assert.Equal("Infra", teams[0].Name);
        Assert.Equal(2, teams[1].Value);
        Assert.Equal("GET /Mitigations/5/Teams", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetTeamsByIdThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Teams", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetTeamsById(5));
    }

    [Fact]
    public void TestGetTeamsByIdWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Teams", HttpStatusCode.InternalServerError);

        var exception = Assert.Throws<RestComunicationException>(() => _service.GetTeamsById(5));

        Assert.IsType<HttpRequestException>(exception.InnerException);
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public void TestGetTeamsByIdWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Mitigations/5/Teams");

        Assert.Throws<RestComunicationException>(() => _service.GetTeamsById(5));
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    // -------------------------------------------------------------- GetByRiskId

    [Fact]
    public void TestGetByRiskIdReturnsTheMitigation()
    {
        _backend.OnGet("/Risks/3/Mitigation", AMitigation());

        var mitigation = _service.GetByRiskId(3);

        Assert.NotNull(mitigation);
        Assert.Equal(5, mitigation.Id);
        Assert.Equal("encrypt the export", mitigation.CurrentSolution);
        Assert.Equal(new DateOnly(2024, 3, 1), mitigation.PlanningDate);
        Assert.Equal("GET /Risks/3/Mitigation", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetByRiskIdReturnsNullWhenTheRiskHasNoMitigation()
    {
        _backend.OnStatus(Method.Get, "/Risks/3/Mitigation", HttpStatusCode.NotFound);

        Assert.Null(_service.GetByRiskId(3));
    }

    [Fact]
    public void TestGetByRiskIdWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/3/Mitigation", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetByRiskId(3));
    }

    [Fact]
    public async Task TestGetByRiskIdAsyncReturnsTheMitigation()
    {
        _backend.OnGet("/Risks/3/Mitigation", AMitigation());

        var mitigation = await _service.GetByRiskIdAsync(3);

        Assert.NotNull(mitigation);
        Assert.Equal(5, mitigation.Id);
        Assert.Equal(10, mitigation.MitigationPercent);
    }

    [Fact]
    public async Task TestGetByRiskIdAsyncReturnsNullWhenTheRiskHasNoMitigation()
    {
        _backend.OnStatus(Method.Get, "/Risks/3/Mitigation", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetByRiskIdAsync(3));
    }

    [Fact]
    public async Task TestGetByRiskIdAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/3/Mitigation");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByRiskIdAsync(3));
    }

    // ---------------------------------------------------------------- GetFiles

    [Fact]
    public async Task TestGetFilesAsyncReturnsTheFileListing()
    {
        _backend.OnGet("/Mitigations/5/Files", new List<FileListing>
        {
            new() { Name = "plan.pdf", UniqueName = "abc.pdf", Type = "application/pdf", OwnerId = 5 },
            new() { Name = "notes.txt", UniqueName = "def.txt", Type = "text/plain", OwnerId = 5 }
        });

        var files = await _service.GetFilesAsync(5);

        Assert.Equal(2, files.Count);
        Assert.Equal("plan.pdf", files[0].Name);
        Assert.Equal("def.txt", files[1].UniqueName);
        Assert.Equal("GET /Mitigations/5/Files", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetFilesAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Files", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetFilesAsync(5));
    }

    [Fact]
    public async Task TestGetFilesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Files", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetFilesAsync(5));
    }

    [Fact]
    public async Task TestGetFilesAsyncDiscardsTheTokenWhenTheServerRejectsTheCall()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Files", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetFilesAsync(5));

        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public void TestGetFilesRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Mitigations/5/Files", new List<FileListing>
        {
            new() { Name = "plan.pdf", UniqueName = "abc.pdf", OwnerId = 5 }
        });

#pragma warning disable CS0618 // the sync overload is deprecated but still shipped and reachable
        var files = _service.GetFiles(5);
#pragma warning restore CS0618

        Assert.Single(files);
        Assert.Equal("plan.pdf", files[0].Name);
    }

    // -------------------------------------------------------------- Strategies

    [Fact]
    public async Task TestGetStrategiesAsyncReturnsTheStrategies()
    {
        _backend.OnGet("/Mitigations/Strategies", new List<PlanningStrategy>
        {
            new() { Value = 1, Name = "Avoid" },
            new() { Value = 2, Name = "Mitigate" }
        });

        var strategies = await _service.GetStrategiesAsync();

        Assert.NotNull(strategies);
        Assert.Equal(2, strategies.Count);
        Assert.Equal("Mitigate", strategies[1].Name);
        Assert.Equal("GET /Mitigations/Strategies", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetStrategiesAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/Strategies", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetStrategiesAsync());
    }

    [Fact]
    public async Task TestGetStrategiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/Strategies", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetStrategiesAsync());
    }

    [Fact]
    public void TestGetStrategiesRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Mitigations/Strategies", new List<PlanningStrategy>
        {
            new() { Value = 3, Name = "Transfer" }
        });

#pragma warning disable CS0618
        var strategies = _service.GetStrategies();
#pragma warning restore CS0618

        Assert.NotNull(strategies);
        Assert.Single(strategies);
        Assert.Equal("Transfer", strategies[0].Name);
    }

    // ------------------------------------------------------------------- Costs

    [Fact]
    public async Task TestGetCostsAsyncReturnsTheCosts()
    {
        _backend.OnGet("/Mitigations/Costs", new List<MitigationCost>
        {
            new() { Value = 1, Name = "Low" },
            new() { Value = 2, Name = "High" }
        });

        var costs = await _service.GetCostsAsync();

        Assert.NotNull(costs);
        Assert.Equal(2, costs.Count);
        Assert.Equal("Low", costs[0].Name);
        Assert.Equal("GET /Mitigations/Costs", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetCostsAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/Costs", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetCostsAsync());
    }

    [Fact]
    public async Task TestGetCostsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Mitigations/Costs");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetCostsAsync());
    }

    [Fact]
    public void TestGetCostsRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Mitigations/Costs", new List<MitigationCost> { new() { Value = 1, Name = "Low" } });

#pragma warning disable CS0618
        var costs = _service.GetCosts();
#pragma warning restore CS0618

        Assert.NotNull(costs);
        Assert.Single(costs);
    }

    // ----------------------------------------------------------------- Efforts

    [Fact]
    public async Task TestGetEffortsAsyncReturnsTheEfforts()
    {
        _backend.OnGet("/Mitigations/Efforts", new List<MitigationEffort>
        {
            new() { Value = 1, Name = "Days" },
            new() { Value = 2, Name = "Weeks" }
        });

        var efforts = await _service.GetEffortsAsync();

        Assert.NotNull(efforts);
        Assert.Equal(2, efforts.Count);
        Assert.Equal("Weeks", efforts[1].Name);
        Assert.Equal("GET /Mitigations/Efforts", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetEffortsAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/Efforts", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetEffortsAsync());
    }

    [Fact]
    public async Task TestGetEffortsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/Efforts", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetEffortsAsync());
    }

    [Fact]
    public void TestGetEffortsRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Mitigations/Efforts", new List<MitigationEffort> { new() { Value = 2, Name = "Weeks" } });

#pragma warning disable CS0618
        var efforts = _service.GetEfforts();
#pragma warning restore CS0618

        Assert.NotNull(efforts);
        Assert.Equal("Weeks", efforts[0].Name);
    }

    // ----------------------------------------------------------------- GetById

    [Fact]
    public void TestGetByIdReturnsTheMitigation()
    {
        _backend.OnGet("/Mitigations/5", AMitigation());

        var mitigation = _service.GetById(5);

        Assert.NotNull(mitigation);
        Assert.Equal(3, mitigation.RiskId);
        Assert.Equal("rotate the keys", mitigation.SecurityRecommendations);
        Assert.Equal("GET /Mitigations/5", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetByIdThrowsWhenTheMitigationIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetById(5));
    }

    [Fact]
    public void TestGetByIdWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetById(5));
    }

    [Fact]
    public void TestGetByIdDiscardsTheTokenWhenTheServerRejectsTheCall()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5", HttpStatusCode.Unauthorized);

        Assert.Throws<RestComunicationException>(() => _service.GetById(5));

        _authentication.Received(1).DiscardAuthenticationToken();
    }

    // -------------------------------------------------------------------- Save

    [Fact]
    public void TestSavePutsTheMitigationToItsOwnRoute()
    {
        _backend.OnPut("/Mitigations/5", "");

        _service.Save(AMitigationDto());

        Assert.Equal("PUT /Mitigations/5", _backend.LastRequest.ToString());
        Assert.Contains("encrypt the export", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveThrowsWhenTheMitigationIsUnknown()
    {
        _backend.OnStatus(Method.Put, "/Mitigations/5", HttpStatusCode.NotFound);

        // Save used to only null-check the response and never inspect the status, so a 404 - which
        // RestSharp reports as a completed exchange - passed silently as a saved mitigation.
        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.Save(AMitigationDto()));

        Assert.Equal("/Mitigations/5", ex.Url);
        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public void TestSaveReportsTheServerValidationError()
    {
        // Same shape as RisksRestService.SaveRisk: when the server does explain itself, the
        // OperationError reaches the caller rather than being flattened into a bare failure.
        _backend.On(Method.Put, "/Mitigations/5", new OperationError
        {
            Title = "Validation failed",
            Status = 400,
            Errors = new Dictionary<string, string[]> { ["Name"] = ["required"] }
        }, HttpStatusCode.NotFound);

        var ex = Assert.Throws<ErrorSavingException>(() => _service.Save(AMitigationDto()));

        Assert.Equal("Error saving mitigation", ex.Message);
        Assert.Equal("Validation failed", ex.Result.Title);
        Assert.Equal(400, ex.Result.Status);
    }

    [Fact]
    public void TestSaveWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Mitigations/5", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Save(AMitigationDto()));
    }

    [Fact]
    public void TestSaveWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Mitigations/5");

        Assert.Throws<RestComunicationException>(() => _service.Save(AMitigationDto()));
    }

    // ------------------------------------------------------------------ Create

    [Fact]
    public void TestCreateReturnsTheCreatedMitigation()
    {
        _backend.OnPost("/Mitigations", AMitigation(12));

        var created = _service.Create(AMitigationDto(0));

        Assert.NotNull(created);
        Assert.Equal(12, created.Id);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/Mitigations", _backend.LastRequest.Path);
        Assert.Contains("encrypt the export", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, "/Mitigations", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Create(AMitigationDto(0)));
    }

    [Fact]
    public void TestCreateWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Mitigations", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Create(AMitigationDto(0)));
    }

    // ------------------------------------------------- DeleteTeamsAssociations

    [Fact]
    public void TestDeleteTeamsAssociationsSendsTheDelete()
    {
        _backend.OnDelete("/Mitigations/5/Teams", "");

        _service.DeleteTeamsAssociations(5);

        Assert.Equal("DELETE /Mitigations/5/Teams", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteTeamsAssociationsThrowsOnANonOkStatus()
    {
        _backend.OnStatus(Method.Delete, "/Mitigations/5/Teams", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.DeleteTeamsAssociations(5));
    }

    [Fact]
    public void TestDeleteTeamsAssociationsWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Mitigations/5/Teams", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.DeleteTeamsAssociations(5));
    }

    // ---------------------------------------------- AssociateMitigationToTeam

    [Fact]
    public void TestAssociateMitigationToTeamCallsTheAssociateRoute()
    {
        _backend.OnGet("/Mitigations/5/Teams/Associate/2", "");

        _service.AssociateMitigationToTeam(5, 2);

        Assert.Equal("GET /Mitigations/5/Teams/Associate/2", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestAssociateMitigationToTeamThrowsOnANonOkStatus()
    {
        _backend.OnStatus(Method.Get, "/Mitigations/5/Teams/Associate/2", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.AssociateMitigationToTeam(5, 2));
    }

    [Fact]
    public void TestAssociateMitigationToTeamWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Mitigations/5/Teams/Associate/2");

        Assert.Throws<RestComunicationException>(() => _service.AssociateMitigationToTeam(5, 2));
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }
}
