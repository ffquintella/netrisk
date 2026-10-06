using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Risks.Chain;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.1 (S41 §8) — <see cref="RiskChainRestService"/> over <see cref="StubRestBackend"/>, so every
/// URL it builds and every status branch runs for real.
///
/// As with the governance client, the property that matters is that a refusal keeps the server's
/// sentence: "This link comes from the risk's Entity field. Remove it there…" is something the user can
/// act on; "error calling /RiskChain/…" is not.
/// </summary>
[TestSubject(typeof(RiskChainRestService))]
public class RiskChainRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRiskChainService _service;

    public RiskChainRestServiceTest()
    {
        _service = ResolveWith<IRiskChainService>(_backend);
    }

    private static RiskChainLinkDto Link(int id, RiskChainLinkOrigin origin = RiskChainLinkOrigin.Declared) => new()
    {
        Id = id, RiskId = 7, Level = RiskChainLevel.Process, EntityId = 10, TargetName = "Enrolment",
        TargetType = "businessProcess", Origin = origin,
        CreatedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)
    };

    // --- success --------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheChainOfARiskIsReadWithItsFiveLevels()
    {
        var chain = new RiskChainDto { RiskId = 7, ScopeEntityId = 100, ScopeEntityName = "Unit A" };
        foreach (var level in Enum.GetValues<RiskChainLevel>())
            chain.Levels.Add(new RiskChainLevelDto
                { Level = level, Links = level == RiskChainLevel.Process ? [Link(1)] : [] });
        chain.MissingLevels.AddRange([RiskChainLevel.Objective, RiskChainLevel.ItService]);

        _backend.OnGet("/RiskChain/Risks/7", chain);

        var read = await _service.GetRiskChainAsync(7);

        Assert.Equal(5, read.Levels.Count);
        Assert.Equal("Enrolment", Assert.Single(read.Levels[1].Links).TargetName);
        Assert.Equal([RiskChainLevel.Objective, RiskChainLevel.ItService], read.MissingLevels);
        Assert.Equal("Unit A", read.ScopeEntityName);
    }

    [Fact]
    public async Task TestAddingALinkPostsTheTargetAndReturnsTheLink()
    {
        _backend.On(Method.Post, "/RiskChain/Risks/7/Links", Link(3), HttpStatusCode.Created);

        var link = await _service.AddLinkAsync(7, new RiskChainLinkCreateDto { EntityId = 10 });

        Assert.Equal(3, link.Id);
        Assert.Contains("\"entityId\":10", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("level", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(_backend.Sent(Method.Post, "/RiskChain/Risks/7/Links"));
    }

    [Fact]
    public async Task TestAPromotionAnsweredWith200IsReturnedToo()
    {
        _backend.On(Method.Post, "/RiskChain/Risks/7/Links", Link(2));

        Assert.Equal(2, (await _service.AddLinkAsync(7, new RiskChainLinkCreateDto { EntityId = 11 })).Id);
    }

    [Fact]
    public async Task TestADeletedLinkIsNull()
    {
        _backend.OnStatus(Method.Delete, "/RiskChain/Risks/7/Links/5", HttpStatusCode.NoContent);

        Assert.Null(await _service.DeleteLinkAsync(7, 5));
        Assert.True(_backend.Sent(Method.Delete, "/RiskChain/Risks/7/Links/5"));
    }

    [Fact]
    public async Task TestADemotedLinkIsReturnedAsLegacy()
    {
        _backend.On(Method.Delete, "/RiskChain/Risks/7/Links/6", Link(6, RiskChainLinkOrigin.Legacy));

        var demoted = await _service.DeleteLinkAsync(7, 6);

        Assert.NotNull(demoted);
        Assert.Equal(RiskChainLinkOrigin.Legacy, demoted!.Origin);
    }

    [Theory]
    [InlineData(false, "?inferred=false")]
    [InlineData(true, "?inferred=true")]
    public async Task TestTheRisksOfAnEntityCarryTheInferenceFlag(bool inferred, string query)
    {
        _backend.OnGet("/RiskChain/Entities/10/Risks", new List<RiskChainMatchDto>
        {
            new()
            {
                RiskId = 2, Subject = "Risk 2", Status = "Closed", Inferred = true,
                ViaLevel = RiskChainLevel.ItService, ViaEntityId = 20, ViaEntityName = "Student portal"
            }
        });

        var match = Assert.Single(await _service.GetRisksByEntityAsync(10, inferred));

        Assert.Equal(query, _backend.LastRequest.Query);
        Assert.True(match.Inferred);
        Assert.Equal(RiskChainLevel.ItService, match.ViaLevel);
        Assert.Equal("Closed", match.Status);
    }

    [Fact]
    public async Task TestTheRisksOfAHostAreRead()
    {
        _backend.OnGet("/RiskChain/Hosts/3/Risks",
            new List<RiskChainMatchDto> { new() { RiskId = 1, Subject = "Risk 1", Status = "New" } });

        Assert.Equal(1, Assert.Single(await _service.GetRisksByHostAsync(3)).RiskId);
    }

    [Fact]
    public async Task TestTheCoverageIsReadWithItsExactRatio()
    {
        _backend.OnGet("/RiskChain/Coverage/CriticalProcesses", new CriticalProcessCoverageDto
        {
            Threshold = 4, CriticalProcessCount = 3, CoveredCount = 1, CoverageRatio = 1m / 3m,
            ProcessesWithoutCriticality = 2, IsScopeRestricted = true,
            Rows = [new CriticalProcessCoverageRowDto { ProcessId = 10, ProcessName = "Enrolment", Criticality = 5 }]
        });

        var coverage = await _service.GetCriticalProcessCoverageAsync();

        Assert.Equal(1m / 3m, coverage.CoverageRatio);
        Assert.True(coverage.IsScopeRestricted);
        Assert.Equal("Enrolment", Assert.Single(coverage.Rows).ProcessName);
    }

    [Fact]
    public async Task TestANotComputableCoverageKeepsItsNullRatio()
    {
        _backend.OnGet("/RiskChain/Coverage/CriticalProcesses", new CriticalProcessCoverageDto { Threshold = 4 });

        Assert.Null((await _service.GetCriticalProcessCoverageAsync()).CoverageRatio);
    }

    // --- refusals -------------------------------------------------------------------------------

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/RiskChain/Risks/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Delete, "/RiskChain/Risks/7/Links/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Get, "/RiskChain/Hosts/99/Risks", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetRiskChainAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeleteLinkAsync(7, 99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetRisksByHostAsync(99));
    }

    [Fact]
    public async Task TestA409KeepsTheServersExplanation()
    {
        _backend.On(Method.Post, "/RiskChain/Risks/7/Links",
            new { error = "already_exists", message = "The risk is already linked to this target." },
            HttpStatusCode.Conflict);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.AddLinkAsync(7, new RiskChainLinkCreateDto { EntityId = 10 }));

        Assert.Contains("already linked to this target", ex.Message);
    }

    [Fact]
    public async Task TestA422KeepsTheServersExplanation()
    {
        _backend.On(Method.Delete, "/RiskChain/Risks/7/Links/8",
            new { error = "legacy_link", message = "This link comes from the risk's Entity field." },
            HttpStatusCode.UnprocessableEntity);
        _backend.On(Method.Get, "/RiskChain/Entities/100/Risks",
            new { error = "entity_not_in_chain", message = "An entity of type 'organizationUnit' is not a node." },
            HttpStatusCode.UnprocessableEntity);

        var delete = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.DeleteLinkAsync(7, 8));
        Assert.Contains("legacy_link", delete.Message);

        var query = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRisksByEntityAsync(100));
        Assert.Contains("organizationUnit", query.Message);
    }

    [Fact]
    public async Task TestA403WithoutHostsKeepsTheServersExplanation()
    {
        _backend.On(Method.Get, "/RiskChain/Hosts/3/Risks",
            new { error = "insufficient_permission", permission = "hosts" }, HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRisksByHostAsync(3));

        Assert.Contains("hosts", ex.Message);
    }

    [Fact]
    public async Task TestA400IsRefused()
    {
        _backend.On(Method.Post, "/RiskChain/Risks/7/Links",
            new { error = "invalid_parameter", parameterName = "target" }, HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.AddLinkAsync(7, new RiskChainLinkCreateDto()));

        Assert.Contains("invalid_parameter", ex.Message);
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailure()
    {
        _backend.OnStatus(Method.Get, "/RiskChain/Coverage/CriticalProcesses", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetCriticalProcessCoverageAsync());
    }

    [Fact]
    public async Task TestATransportFailureIsACommunicationError()
    {
        _backend.OnTransportFailure(Method.Get, "/RiskChain/Risks/7");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskChainAsync(7));
    }

    /// <summary>
    /// The production client throws on any non-2xx before the body can be read. This service asks for
    /// the error-reporting client instead, so its refusals keep their sentence even there.
    /// </summary>
    [Fact]
    public async Task TestRefusalsSurviveTheProductionClientsThrowOnError()
    {
        var backend = new StubRestBackend { ThrowsOnErrorResponses = true };
        var service = ResolveWith<IRiskChainService>(backend);

        backend.On(Method.Post, "/RiskChain/Risks/7/Links",
            new { error = "entity_not_in_chain", message = "Units are scope, not identification." },
            HttpStatusCode.UnprocessableEntity);
        backend.OnStatus(Method.Get, "/RiskChain/Risks/99", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            service.AddLinkAsync(7, new RiskChainLinkCreateDto { EntityId = 100 }));
        Assert.Contains("Units are scope", ex.Message);

        await Assert.ThrowsAsync<DataNotFoundException>(() => service.GetRiskChainAsync(99));
    }
}
