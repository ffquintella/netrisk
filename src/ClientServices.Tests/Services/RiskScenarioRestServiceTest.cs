using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using Model.Risks.Scenario;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.2 (S42 §8) — the two calls the desktop client gained: registering a standalone hypothesis
/// and asking for scenario duplicates, over <see cref="StubRestBackend"/>, with the server's refusal
/// reaching the caller as the sentence the server wrote.
/// </summary>
[TestSubject(typeof(RiskGovernanceRestService))]
public class RiskScenarioRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRiskGovernanceService _service;

    public RiskScenarioRestServiceTest()
    {
        _service = ResolveWith<IRiskGovernanceService>(_backend);
    }

    [Fact]
    public async Task RegisteringAHypothesisPostsItAndReadsTheListing()
    {
        _backend.On(Method.Post, "/Risks/Pending", new PendingRiskListing
        {
            Id = 12, Subject = "Lab instruments reachable", Origin = PendingRiskOrigin.Standalone,
            SubmittedById = 7, Status = PendingRiskStatus.Pending
        }, HttpStatusCode.Created);

        var listing = await _service.CreateHypothesisAsync(new HypothesisRequest
        {
            Subject = "Lab instruments reachable", Description = "Noticed in a walkthrough"
        });

        Assert.Equal(12, listing.Id);
        Assert.Equal(PendingRiskOrigin.Standalone, listing.Origin);
        Assert.Null(listing.AssessmentId);
        Assert.Contains("Noticed in a walkthrough", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task AHypothesisWithoutASubjectSurfacesTheServersRefusal()
    {
        _backend.On(Method.Post, "/Risks/Pending",
            new { error = "invalid_parameter", message = "A hypothesis needs a subject." },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.CreateHypothesisAsync(new HypothesisRequest()));

        Assert.Contains("needs a subject", ex.Message);
    }

    [Fact]
    public async Task ScenarioDuplicatesArePostedAndRead()
    {
        _backend.On(Method.Post, "/Risks/ScenarioDuplicates", new List<RiskScenarioDuplicate>
        {
            new() { RiskId = 3, Subject = "Portal outage", Status = "Closed" }
        });

        var found = await _service.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost", ExcludeRiskId = 9
        });

        var duplicate = Assert.Single(found);
        Assert.Equal(3, duplicate.RiskId);
        Assert.Equal("Closed", duplicate.Status);

        // The scenario text travels in the body, never in the URL.
        Assert.Contains("Portal unavailable", _backend.LastRequest!.Body);
        Assert.DoesNotContain("Portal", _backend.LastRequest!.Query);
    }

    [Fact]
    public async Task AForbiddenDuplicateCheckIsARefusalNotATransportFailure()
    {
        // The editor treats any failure of the check as "no warning available" and saves anyway (S42
        // §7); what it must not get is a RestComunicationException that reads as "server unreachable".
        _backend.On(Method.Post, "/Risks/ScenarioDuplicates", new { }, HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
            {
                CentralEvent = "Portal unavailable", Consequences = "Enrolments lost"
            }));
    }
}
