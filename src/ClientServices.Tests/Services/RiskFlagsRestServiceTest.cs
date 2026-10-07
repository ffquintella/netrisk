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
using Model.RiskFlags;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.5 (S46 §7, §8) — <see cref="RiskFlagsRestService"/> over <see cref="StubRestBackend"/>, so every URL it
/// builds and every status branch runs for real. A Gate A or segregation refusal keeps the server's sentence.
/// </summary>
[TestSubject(typeof(RiskFlagsRestService))]
public class RiskFlagsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRiskFlagsService _service;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public RiskFlagsRestServiceTest()
    {
        _service = ResolveWith<IRiskFlagsService>(_backend);
    }

    private static RiskFlagsStateDto State(bool gateA) => new()
    {
        RiskId = 4,
        Flags = [new RiskFlagStateDto { Code = RiskFlagCode.KnownExploitation, Number = 3, Derived = true, IsSet = true,
            DerivedBasis = "KEV CVE-2021-44228 on finding #5", Derivation = RiskFlagDerivation.Kev }],
        GateA = new GateAEvaluationDto { Holds = gateA, Conditions = gateA ? [RiskFlagCode.KnownExploitation] : [] }
    };

    [Fact]
    public async Task TestTheCatalogueTheStateAndARefreshAreRead()
    {
        _backend.OnGet("/RiskFlags/Catalogue", RiskFlagCatalogue.All);
        _backend.OnGet("/RiskFlags/Risks/4", State(gateA: true));
        _backend.OnPost("/RiskFlags/Risks/4/Refresh", State(gateA: false));

        var catalogue = await _service.GetCatalogueAsync();
        Assert.Equal(12, catalogue.Count);
        Assert.Equal(RiskFlagDerivation.Bia, catalogue[3].Derivation);
        Assert.Null(catalogue[11].Number);

        var state = await _service.GetRiskFlagsAsync(4);
        Assert.True(state.GateA.Holds);
        Assert.Equal("KEV CVE-2021-44228 on finding #5", Assert.Single(state.Flags).DerivedBasis);

        Assert.False((await _service.RefreshAsync(4)).GateA.Holds);
        Assert.True(_backend.Sent(Method.Post, "/RiskFlags/Risks/4/Refresh"));
    }

    [Fact]
    public async Task TestDeclarationsWithdrawalsAndDecisionsSendTheirBodies()
    {
        _backend.OnPut("/RiskFlags/Risks/4/Flags/1", State(gateA: true));
        _backend.OnPost("/RiskFlags/Risks/4/Flags/1/Withdraw", State(gateA: false));
        _backend.OnPost("/RiskFlags/Risks/4/Decisions", new RiskDecisionDto
        {
            Id = 9, RiskId = 4, Decision = RiskDecisionKind.ActImmediately, Source = RiskDecisionSource.Declared,
            Reason = "Now.", DecidedAt = When, EscalatedAt = When
        }, HttpStatusCode.Created);
        _backend.OnGet("/RiskFlags/Risks/4/Decisions", new List<RiskDecisionDto>
        {
            new() { Id = 1, RiskId = 4, Decision = RiskDecisionKind.ActImmediately, Source = RiskDecisionSource.GateA,
                GateAConditions = [RiskFlagCode.KnownExploitation], DecidedAt = When }
        });

        await _service.DeclareAsync(4, RiskFlagCode.HumanSafety, "Patients.");
        Assert.Contains("\"reason\":\"Patients.\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        await _service.WithdrawAsync(4, RiskFlagCode.HumanSafety, "Decommissioned.");
        Assert.Contains("Decommissioned.", _backend.LastRequest.Body);

        var decision = await _service.RecordDecisionAsync(4, RiskDecisionKind.ActImmediately, "Now.");
        Assert.Equal((9, When), (decision.Id, decision.EscalatedAt!.Value));
        Assert.Contains("\"decision\":1", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        var log = Assert.Single(await _service.GetDecisionsAsync(4));
        Assert.Equal((RiskDecisionSource.GateA, RiskFlagCode.KnownExploitation), (log.Source, Assert.Single(log.GateAConditions)));
    }

    [Fact]
    public async Task TestTheQueryAndTopRisksPassTheirParameters()
    {
        _backend.OnGet("/RiskFlags/Flagged", new List<FlaggedRiskDto>
            { new() { RiskId = 4, Flags = [RiskFlagCode.HumanSafety], GateA = true } });
        _backend.OnGet("/RiskFlags/TopRisks", new TopRisksDto
        {
            Limit = 5, OpenRisks = 40,
            Items = [new TopRiskDto { Rank = 1, RiskId = 4, GateA = true,
                Trend = new RiskTrendDto { Direction = RiskTrendDirection.Rising, Delta = 2 },
                NextDecision = new NextDecisionDto { Kind = NextDecisionKind.GateAEscalation, Overdue = true } }]
        });

        Assert.True(Assert.Single(await _service.GetFlaggedAsync(RiskFlagCode.HumanSafety, true)).GateA);
        Assert.Contains("flag=1", _backend.LastRequest.Query);
        Assert.Contains("gateA=true", _backend.LastRequest.Query);

        await _service.GetFlaggedAsync();
        Assert.DoesNotContain("flag=", _backend.LastRequest.Query);

        var top = await _service.GetTopRisksAsync(5);
        Assert.Contains("limit=5", _backend.LastRequest.Query);
        var row = Assert.Single(top.Items);
        Assert.Equal((RiskTrendDirection.Rising, NextDecisionKind.GateAEscalation),
            (row.Trend.Direction, row.NextDecision.Kind));
    }

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/RiskFlags/Risks/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetRiskFlagsAsync(99));
    }

    /// <summary>A Gate A or segregation refusal reaches the person with the server's sentence intact.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "This risk carries a non-discretionary (Gate A) condition.")]
    [InlineData(HttpStatusCode.BadRequest, "A decision needs a written reason.")]
    [InlineData(HttpStatusCode.Forbidden, "insufficient_permission")]
    public async Task TestARefusalKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.OnPost("/RiskFlags/Risks/4/Decisions", new { error = "gate_a_non_discretionary", message = sentence }, status);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.RecordDecisionAsync(4, RiskDecisionKind.MonitorAccept, "Within appetite."));

        Assert.Contains(sentence, ex.Message);
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailure()
    {
        _backend.OnStatus(Method.Get, "/RiskFlags/TopRisks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTopRisksAsync());
    }
}
