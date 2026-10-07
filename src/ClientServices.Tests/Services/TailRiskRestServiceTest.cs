using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.TailRisk;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.7 (S48 §7, §8) — <see cref="TailRiskRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds
/// and every status branch runs for real. A correlation matrix that is not positive semidefinite keeps the server's
/// sentence.
/// </summary>
[TestSubject(typeof(TailRiskRestService))]
public class TailRiskRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly ITailRiskService _service;

    public TailRiskRestServiceTest()
    {
        _service = ResolveWith<ITailRiskService>(_backend);
    }

    private static RiskTailDto Tail(bool recomputed) => new()
    {
        RiskId = 4, Recomputed = recomputed,
        Inherent = new TailStatisticsDto
        {
            Run = TailRun.Inherent, Iterations = 10_000, ExpectedLoss = 1_000, P95 = 5_000, Cvar95 = 9_000,
            MagnitudeSource = MagnitudeSource.Components,
            Components = [new LossComponentContributionDto { Component = LossComponent.Fine, Cvar95 = 4_000 }]
        },
        DeclaredComponents = [new LossComponentDto { Component = LossComponent.Fine, Min = 1, MostLikely = 2, Max = 3, Basis = "LGPD art. 52" }],
        Appetite = new TailAppetiteEvaluation
        {
            State = TailAppetiteState.NotAssessable, Reasons = [TailAppetiteNotAssessableReason.NoTailStatistics],
            Explanation = "No tail statistics."
        },
        TailFlag = new TailFlagCriterionDto { Holds = true, Derived = true }
    };

    [Fact]
    public async Task TestTheTailIsReadAndTheComponentsAreWrittenWithTheirBody()
    {
        _backend.OnGet("/TailRisk/Risks/4", Tail(false));
        _backend.OnPut("/TailRisk/Risks/4/LossComponents", Tail(true));

        var read = await _service.GetRiskAsync(4);
        Assert.Equal((TailAppetiteState.NotAssessable, TailAppetiteNotAssessableReason.NoTailStatistics),
            (read.Appetite.State, Assert.Single(read.Appetite.Reasons)));
        Assert.Equal(LossComponent.Fine, Assert.Single(read.Inherent!.Components).Component);

        var saved = await _service.SaveLossComponentsAsync(4, new LossComponentsRequest
        {
            Components = [new LossComponentRequest { Component = LossComponent.Fine, Min = 1, MostLikely = 2, Max = 3, Basis = "LGPD art. 52" }]
        });

        Assert.True(saved.Recomputed);
        Assert.True(_backend.Sent(Method.Put, "/TailRisk/Risks/4/LossComponents"));
        Assert.Contains("\"basis\":\"LGPD art. 52\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"mostLikely\":2", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTheComponentsAreRemovedAndTheTailIsReturned()
    {
        _backend.OnDelete("/TailRisk/Risks/4/LossComponents", Tail(true));

        var removed = await _service.DeleteLossComponentsAsync(4);

        Assert.Equal(4, removed.RiskId);
        Assert.True(_backend.Sent(Method.Delete, "/TailRisk/Risks/4/LossComponents"));
    }

    [Fact]
    public async Task TestTheCorrelationsAreListedWithOrWithoutTheRiskFilter()
    {
        _backend.OnGet("/TailRisk/Correlations", new[]
        {
            new RiskCorrelationDto { Id = 1, RiskAId = 4, RiskBId = 9, Coefficient = 0.4m, Rationale = "Same supplier." }
        });

        var all = await _service.GetCorrelationsAsync();
        Assert.Equal((4, 9, 0.4m), (all.Single().RiskAId, all.Single().RiskBId, all.Single().Coefficient));
        Assert.DoesNotContain("riskId", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);

        await _service.GetCorrelationsAsync(4);
        Assert.Contains("riskId=4", _backend.LastRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestACorrelationIsDeclaredWithItsBodyAndRemoved()
    {
        _backend.OnPut("/TailRisk/Correlations", new RiskCorrelationDto
            { Id = 1, RiskAId = 4, RiskBId = 9, Coefficient = 0.4m, Rationale = "Same supplier." });
        _backend.OnStatus(Method.Delete, "/TailRisk/Correlations/1", HttpStatusCode.NoContent);

        var saved = await _service.SaveCorrelationAsync(new RiskCorrelationRequest
            { RiskAId = 9, RiskBId = 4, Coefficient = 0.4m, Rationale = "Same supplier." });

        Assert.Equal(0.4m, saved.Coefficient);
        Assert.Contains("\"riskAId\":9", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"coefficient\":0.4", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        await _service.DeleteCorrelationAsync(1);
        Assert.True(_backend.Sent(Method.Delete, "/TailRisk/Correlations/1"));
    }

    [Fact]
    public async Task TestThePortfolioSendsItsRequest()
    {
        _backend.OnPost("/TailRisk/Portfolio", new PortfolioTailDto
        {
            Basis = PortfolioBasis.Residual, Dependence = PortfolioDependence.Declared, Cvar95 = 20_000,
            SumOfCvar95 = 25_000, Diversification = 5_000,
            Members = [new PortfolioMemberDto { RiskId = 4, ScenarioAppetite = TailAppetiteState.ExceedsTolerance }],
            NotQuantified = [new PortfolioExcludedRiskDto { RiskId = 5, Reason = PortfolioExclusionReason.NotQuantified }],
            Appetite = new TailAppetiteEvaluation { State = TailAppetiteState.NotConfigured }
        });

        var aggregate = await _service.AggregatePortfolioAsync(new PortfolioTailRequest
            { RiskIds = [4, 5], Basis = PortfolioBasis.Residual, Seed = 7, EntityId = 2 });

        Assert.Equal((PortfolioDependence.Declared, TailAppetiteState.ExceedsTolerance),
            (aggregate.Dependence, aggregate.Members.Single().ScenarioAppetite));
        Assert.Equal(PortfolioExclusionReason.NotQuantified, Assert.Single(aggregate.NotQuantified).Reason);
        Assert.Contains("\"riskIds\":[4,5]", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"seed\":7", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTheTailLimitsAreReadWrittenAndRemovedOnTheAppetiteRoute()
    {
        _backend.OnGet("/RiskAppetites/3/TailLimits", new RiskAppetiteTailLimitsDto
            { AppetiteId = 3, MaxScenarioP95 = 250_000m, Rationale = "Phase 0." });
        _backend.OnPut("/RiskAppetites/3/TailLimits", new RiskAppetiteTailLimitsDto
            { AppetiteId = 3, MaxPortfolioCvar95 = 2_000_000m, Rationale = "Phase 0." });
        _backend.OnStatus(Method.Delete, "/RiskAppetites/3/TailLimits", HttpStatusCode.NoContent);

        Assert.Equal(250_000m, (await _service.GetAppetiteLimitsAsync(3)).MaxScenarioP95);

        var saved = await _service.SaveAppetiteLimitsAsync(3,
            new RiskAppetiteTailLimitsRequest { MaxPortfolioCvar95 = 2_000_000m, Rationale = "Phase 0." });
        Assert.Equal(2_000_000m, saved.MaxPortfolioCvar95);
        Assert.Contains("\"maxPortfolioCvar95\":2000000", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        await _service.DeleteAppetiteLimitsAsync(3);
        Assert.True(_backend.Sent(Method.Delete, "/RiskAppetites/3/TailLimits"));
    }

    [Fact]
    public async Task TestANullRequestIsRefusedBeforeAnyCall()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveLossComponentsAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveCorrelationAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AggregatePortfolioAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveAppetiteLimitsAsync(3, null!));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/TailRisk/Risks/99", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Get, "/RiskAppetites/99/TailLimits", HttpStatusCode.NotFound);
        _backend.OnStatus(Method.Delete, "/TailRisk/Correlations/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetRiskAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetAppetiteLimitsAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeleteCorrelationAsync(99));
    }

    /// <summary>A matrix that is not positive semidefinite, or a validation error, reaches the person with the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "The declared correlations are not consistent: the matrix is not positive semidefinite.")]
    [InlineData(HttpStatusCode.BadRequest, "A correlation needs a rationale.")]
    public async Task TestARefusalKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.OnPut("/TailRisk/Correlations",
            new { error = "correlation_not_positive_semidefinite", message = sentence }, status);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveCorrelationAsync(new RiskCorrelationRequest { RiskAId = 1, RiskBId = 2, Coefficient = 0.9m }));

        Assert.Contains(sentence, ex.Message);
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailure()
    {
        _backend.OnStatus(Method.Post, "/TailRisk/Portfolio", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.AggregatePortfolioAsync(new PortfolioTailRequest { Basis = PortfolioBasis.Residual }));
    }
}
