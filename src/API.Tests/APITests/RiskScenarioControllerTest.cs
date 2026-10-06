using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.Governance;
using Model.Risks.Scenario;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.2 (S42 §8) — the HTTP contract of the stage: <c>POST /Risks/Pending</c> (standalone
/// hypothesis), <c>POST /Risks/ScenarioDuplicates</c>, and the 400 the existing risk and incident
/// writes now answer for an undefined evidence confidence or incident kind instead of a 500.
/// Each test passes its own service double through <see cref="BaseControllerTest.ResolveController{T}"/>.
/// </summary>
[TestSubject(typeof(RiskGovernanceController))]
public class RiskScenarioControllerTest : BaseControllerTest
{
    private readonly IRisksService _risks = Substitute.For<IRisksService>();
    private readonly IIncidentsService _incidents = Substitute.For<IIncidentsService>();

    private RiskGovernanceController Governance() =>
        ResolveController<RiskGovernanceController>(s => s.AddSingleton(_risks));

    private RisksController Risks() => ResolveController<RisksController>(s => s.AddSingleton(_risks));

    private IncidentsController Incidents() =>
        ResolveController<IncidentsController>(s => s.AddSingleton(_incidents));

    private static JsonElement Body(object? value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;

    // --- POST /Risks/Pending ---------------------------------------------------------------------

    [Fact]
    public async Task TestCreatingAHypothesisReturnsCreatedWithTheListing()
    {
        _risks.CreateHypothesisAsync(Arg.Any<HypothesisRequest>(), Arg.Any<int>())
            .Returns(new PendingRiskListing
            {
                Id = 12, Subject = "Lab instruments reachable", Origin = PendingRiskOrigin.Standalone,
                SubmittedById = 1, Status = PendingRiskStatus.Pending
            });

        var result = await Governance().CreateHypothesis(new HypothesisRequest { Subject = "Lab instruments reachable" });

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal("Risks/Pending/12", created.Location);
        var listing = Assert.IsType<PendingRiskListing>(created.Value);
        Assert.Equal(PendingRiskOrigin.Standalone, listing.Origin);

        // The author is the authenticated caller, never something the body can claim.
        await _risks.Received(1).CreateHypothesisAsync(
            Arg.Is<HypothesisRequest>(r => r.Subject == "Lab instruments reachable"), 1);
    }

    [Fact]
    public async Task TestAHypothesisWithoutASubjectIsABadRequestNamingTheField()
    {
        _risks.CreateHypothesisAsync(Arg.Any<HypothesisRequest>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidParameterException("Subject", "A hypothesis needs a subject."));

        var result = await Governance().CreateHypothesis(null);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("invalid_parameter", Body(bad.Value).GetProperty("error").GetString());
        Assert.Equal("Subject", Body(bad.Value).GetProperty("ParameterName").GetString());
    }

    [Fact]
    public async Task TestAHypothesisWithAnUnknownOwnerIsNotFound()
    {
        _risks.CreateHypothesisAsync(Arg.Any<HypothesisRequest>(), Arg.Any<int>())
            .ThrowsAsync(new DataNotFoundException("local", "user", new Exception("User 404 not found")));

        var result = await Governance().CreateHypothesis(new HypothesisRequest { Subject = "x", OwnerId = 404 });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // --- POST /Risks/ScenarioDuplicates ---------------------------------------------------------------

    [Fact]
    public async Task TestScenarioDuplicatesAreReturned()
    {
        _risks.FindScenarioDuplicatesAsync(Arg.Any<RiskScenarioDuplicateQuery>())
            .Returns(new List<RiskScenarioDuplicate>
            {
                new() { RiskId = 3, Subject = "Portal outage", Status = "New" }
            });

        var result = await Governance().FindScenarioDuplicates(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost", ExcludeRiskId = 9
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<RiskScenarioDuplicate>>(ok.Value);
        Assert.Equal(3, Assert.Single(list).RiskId);
        await _risks.Received(1).FindScenarioDuplicatesAsync(
            Arg.Is<RiskScenarioDuplicateQuery>(q => q.ExcludeRiskId == 9 && q.CentralEvent == "Portal unavailable"));
    }

    [Fact]
    public async Task TestHalfAScenarioPairIsABadRequest()
    {
        _risks.FindScenarioDuplicatesAsync(Arg.Any<RiskScenarioDuplicateQuery>())
            .ThrowsAsync(new InvalidParameterException("Consequences", "Half a pair matches nothing."));

        var result = await Governance().FindScenarioDuplicates(null);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Consequences", Body(bad.Value).GetProperty("ParameterName").GetString());
    }

    // --- the existing writes: 400, not 500 ------------------------------------------------------------

    private static Risk ARisk() => new()
    {
        Id = 5, Status = "New", Subject = "s", ReferenceId = "", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", EvidenceConfidence = (EvidenceConfidence)99
    };

    [Fact]
    public async Task TestCreatingARiskWithAnUndefinedConfidenceIsABadRequest()
    {
        _risks.CreateRiskAsync(Arg.Any<Risk>())
            .ThrowsAsync(new InvalidParameterException("EvidenceConfidence", "Not a level."));

        var result = await Risks().CreateAsync(ARisk());

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("EvidenceConfidence", Body(bad.Value).GetProperty("ParameterName").GetString());
    }

    [Fact]
    public async Task TestSavingARiskWithAnUndefinedConfidenceIsABadRequest()
    {
        _risks.SaveRiskAsync(Arg.Any<Risk>())
            .ThrowsAsync(new InvalidParameterException("EvidenceConfidence", "Not a level."));

        var result = await Risks().Save(5, ARisk());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_parameter", Body(bad.Value).GetProperty("error").GetString());
    }

    [Fact]
    public async Task TestCreatingAnIncidentWithAnUndefinedKindIsABadRequest()
    {
        _incidents.CreateAsync(Arg.Any<Incident>(), Arg.Any<User>())
            .ThrowsAsync(new InvalidParameterException("Kind", "Not a kind."));

        var result = await Incidents().CreateAsync(new Incident { Name = "2026-1", Description = "d", Kind = (IncidentKind)3 });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Kind", Body(bad.Value).GetProperty("ParameterName").GetString());
    }

    [Fact]
    public async Task TestUpdatingAnIncidentWithAnUndefinedKindIsABadRequest()
    {
        _incidents.UpdateAsync(Arg.Any<Incident>(), Arg.Any<User>())
            .ThrowsAsync(new InvalidParameterException("Kind", "Not a kind."));

        var result = await Incidents().UpdateAsync(1, new Incident { Name = "2026-1", Description = "d", Kind = 0 });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestANearMissIsCreatedLikeAnIncident()
    {
        _incidents.CreateAsync(Arg.Any<Incident>(), Arg.Any<User>())
            .Returns(call => call.ArgAt<Incident>(0));

        var result = await Incidents().CreateAsync(new Incident
        {
            Id = 4, Name = "2026-4", Description = "d", Kind = IncidentKind.NearMiss
        });

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(IncidentKind.NearMiss, Assert.IsType<Incident>(created.Value).Kind);
    }
}
