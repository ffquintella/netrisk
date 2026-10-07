using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Continuity;
using Model.Exceptions;
using Model.Risks.Chain;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.3 (S43 §8) — <see cref="ContinuityRestService"/> over <see cref="StubRestBackend"/>, so every
/// URL it builds and every status branch runs for real. A refusal keeps the server's sentence; the void
/// reason and the parameters travel in the body.
/// </summary>
[TestSubject(typeof(ContinuityRestService))]
public class ContinuityRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IContinuityService _service;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public ContinuityRestServiceTest()
    {
        _service = ResolveWith<IContinuityService>(_backend);
    }

    // --- success --------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheSubjectsAndAProfileAreRead()
    {
        _backend.OnGet("/Continuity/Subjects", new List<ContinuitySubjectDto>
        {
            new() { EntityId = 10, Name = "Enrolment", RtoStatus = ObjectiveVerificationStatus.Unverified, ThreatWeight = 0.5m }
        });
        _backend.OnGet("/Continuity/Subjects/10", new ContinuityProfileDto
        {
            Subject = new ContinuitySubjectDto { EntityId = 10 },
            Threat = new ContinuityThreatDto
            {
                ThreatWeight = 0.5m, UnverifiedWeight = 0.5m, IsThreatened = true,
                Items = [new ContinuityThreatItemDto { Reason = ContinuityThreatReason.Unverified, Weight = 0.5m }]
            },
            Cascade = new ContinuityCascadeDto { CycleMembers = [20] }
        });

        var subject = Assert.Single(await _service.GetSubjectsAsync());
        Assert.Equal((ObjectiveVerificationStatus.Unverified, 0.5m), (subject.RtoStatus, subject.ThreatWeight));

        var profile = await _service.GetProfileAsync(10);
        Assert.Equal(ContinuityThreatReason.Unverified, Assert.Single(profile.Threat.Items).Reason);
        Assert.Equal([20], profile.Cascade.CycleMembers);
    }

    [Fact]
    public async Task TestSavingTheBiaPutsTheObjectives()
    {
        _backend.On(Method.Put, "/Continuity/Subjects/10/Bia",
            new BusinessImpactAnalysisDto { Id = 1, EntityId = 10, RtoMinutes = 240, AssessedAt = When }, HttpStatusCode.Created);

        var bia = await _service.SaveBiaAsync(10, new BusinessImpactAnalysisRequest { RtoMinutes = 240, MtpdMinutes = 480 });

        Assert.Equal(240, bia.RtoMinutes);
        Assert.Contains("\"rtoMinutes\":240", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"mtpdMinutes\":480", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestDeletesUseTheNodeRoute()
    {
        _backend.OnStatus(Method.Delete, "/Continuity/Subjects/10/Bia", HttpStatusCode.NoContent);
        _backend.OnStatus(Method.Delete, "/Continuity/Subjects/10/Dependencies/3", HttpStatusCode.NoContent);

        await _service.DeleteBiaAsync(10);
        await _service.DeleteDependencyAsync(10, 3);

        Assert.True(_backend.Sent(Method.Delete, "/Continuity/Subjects/10/Bia"));
        Assert.True(_backend.Sent(Method.Delete, "/Continuity/Subjects/10/Dependencies/3"));
    }

    [Fact]
    public async Task TestADependencyAndATestArePosted()
    {
        _backend.On(Method.Post, "/Continuity/Subjects/10/Dependencies",
            new BiaDependencyDto { Id = 3, DependentEntityId = 10, ProviderEntityId = 20 }, HttpStatusCode.Created);
        _backend.On(Method.Post, "/Continuity/Subjects/10/RestorationTests",
            new RestorationTestDto { Id = 5, EntityId = 10, Outcome = RestorationTestOutcome.Succeeded, AchievedRtoMinutes = 200 },
            HttpStatusCode.Created);

        Assert.Equal(20, (await _service.AddDependencyAsync(10, new BiaDependencyCreateRequest { ProviderEntityId = 20 })).ProviderEntityId);
        Assert.Contains("\"providerEntityId\":20", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        var test = await _service.RecordRestorationTestAsync(10, new RestorationTestCreateRequest
            { TestedAt = When, Outcome = RestorationTestOutcome.Succeeded, AchievedRtoMinutes = 200 });
        Assert.Equal(200, test.AchievedRtoMinutes);
    }

    /// <summary>The void reason travels in the body, never in the URL that proxies and access logs record.</summary>
    [Fact]
    public async Task TestTheVoidReasonGoesInTheBody()
    {
        _backend.On(Method.Post, "/Continuity/Subjects/10/RestorationTests/5/Void",
            new RestorationTestDto { Id = 5, VoidReason = "Recorded against the wrong process", VoidedAt = When });

        var voided = await _service.VoidRestorationTestAsync(10, 5, "Recorded against the wrong process");

        Assert.Equal("Recorded against the wrong process", voided.VoidReason);
        Assert.Contains("wrong process", _backend.LastRequest.Body);
        Assert.DoesNotContain("wrong", _backend.LastRequest.Path + _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheTestsTheMetricAndTheParametersAreRead()
    {
        _backend.OnGet("/Continuity/Subjects/10/RestorationTests", new List<RestorationTestDto> { new() { Id = 5 } });
        _backend.OnGet("/Continuity/Metrics/RestorationVerification", new RestorationVerificationMetricDto
        {
            ValidityDays = 365, UnverifiedWeight = 0.5m, ThreatenedCriticalProcessesWeighted = 1.5m,
            All = new RestorationVerificationSummaryDto { Rto = new ObjectiveVerificationSummaryDto { Declared = 3, Met = 1, MetRatio = 1m / 3m } }
        });
        _backend.OnGet("/Continuity/Settings", new ContinuitySettingsDto { RestorationTestValidityDays = 365, UnverifiedThreatWeight = 0.5m });

        Assert.Equal(5, Assert.Single(await _service.GetRestorationTestsAsync(10)).Id);

        var metric = await _service.GetRestorationVerificationMetricAsync();
        Assert.Equal(1.5m, metric.ThreatenedCriticalProcessesWeighted);
        Assert.Equal(1m / 3m, metric.All.Rto.MetRatio);

        Assert.Equal(0.5m, (await _service.GetSettingsAsync()).UnverifiedThreatWeight);
    }

    /// <summary>The weight round-trips as a decimal with no loss, both ways.</summary>
    [Theory]
    [InlineData("0.5")]
    [InlineData("0.25")]
    public async Task TestTheWeightRoundTripsAsADecimal(string raw)
    {
        var weight = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        _backend.On(Method.Put, "/Continuity/Settings",
            new ContinuitySettingsDto { RestorationTestValidityDays = 30, UnverifiedThreatWeight = weight });

        var saved = await _service.SaveSettingsAsync(new ContinuitySettingsRequest
            { RestorationTestValidityDays = 30, UnverifiedThreatWeight = weight });

        Assert.Equal(weight, saved.UnverifiedThreatWeight);
        Assert.Contains($"\"unverifiedThreatWeight\":{raw}", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTheCoverageCarriesTheCriticalitySource()
    {
        var chain = ResolveWith<IRiskChainService>(_backend);
        _backend.OnGet("/RiskChain/Coverage/CriticalProcesses", new CriticalProcessCoverageDto
        {
            Rows = [new CriticalProcessCoverageRowDto { ProcessId = 10, Criticality = 5, CriticalitySource = CriticalitySource.Bia }]
        });

        Assert.Equal(CriticalitySource.Bia, Assert.Single((await chain.GetCriticalProcessCoverageAsync()).Rows).CriticalitySource);
    }

    // --- refusals -------------------------------------------------------------------------------

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/Continuity/Subjects/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetProfileAsync(99));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "continuity_unverified_threat_weight must be from 0.01 to 1.00")]
    [InlineData(HttpStatusCode.Forbidden, "global_scope")]
    public async Task TestARefusedSaveOfTheParametersKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.On(Method.Put, "/Continuity/Settings", new { error = "x", message = sentence }, status);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.SaveSettingsAsync(
            new ContinuitySettingsRequest { RestorationTestValidityDays = 30, UnverifiedThreatWeight = 0m }));

        Assert.Contains(sentence, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "This dependency is already declared.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "A process or service cannot depend on itself.")]
    public async Task TestARefusedDependencyKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.On(Method.Post, "/Continuity/Subjects/10/Dependencies", new { error = "x", message = sentence }, status);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.AddDependencyAsync(10, new BiaDependencyCreateRequest { ProviderEntityId = 10 }));

        Assert.Contains(sentence, ex.Message);
    }
}
