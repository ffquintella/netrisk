using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.TailRisk;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.7 (S48 §6, §8) — <see cref="TailRiskController"/> and the <c>TailLimits</c> actions of
/// <see cref="RiskAppetitesController"/>: each action's success shape, and the mapping of every domain exception onto the
/// status code the other controllers use for it.
/// </summary>
[TestSubject(typeof(TailRiskController))]
public class TailRiskControllerTest : BaseControllerTest
{
    private readonly TailRiskController _controller;
    private readonly RiskAppetitesController _appetites;

    private const int Known = MockedTailRiskService.Known;
    private const int Invalid = MockedTailRiskService.Invalid;
    private const int Missing = MockedTailRiskService.Missing;
    private const int Broken = MockedTailRiskService.Broken;

    public TailRiskControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<TailRiskController>();
        _appetites = _serviceProvider.GetRequiredService<RiskAppetitesController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly LossComponentsRequest Components = new()
    {
        Components = [new LossComponentRequest { Component = LossComponent.Response, Min = 1, MostLikely = 2, Max = 3 }]
    };

    private static RiskCorrelationRequest Correlation(int a, int b = 11) => new()
        { RiskAId = a, RiskBId = b, Coefficient = 0.4m, Rationale = "Same supplier." };

    private static readonly RiskAppetiteTailLimitsRequest Limits = new()
        { MaxScenarioP95 = 250_000m, Rationale = "Phase 0 decision." };

    // --- TailRisk ---------------------------------------------------------------------------

    [Fact]
    public async Task TestReadsAnswer200WithTheirDtos()
    {
        var risk = Assert.IsType<OkObjectResult>((await _controller.GetRisk(Known)).Result);
        var tail = Assert.IsType<RiskTailDto>(risk.Value);
        Assert.Equal((Known, TailAppetiteState.WithinTolerance), (tail.RiskId, tail.Appetite.State));

        var correlations = Assert.IsType<OkObjectResult>((await _controller.GetCorrelations(Known)).Result);
        Assert.Equal(0.4m, Assert.Single(Assert.IsType<List<RiskCorrelationDto>>(correlations.Value)).Coefficient);

        var all = Assert.IsType<OkObjectResult>((await _controller.GetCorrelations(null)).Result);
        Assert.Single(Assert.IsType<List<RiskCorrelationDto>>(all.Value));

        var portfolio = Assert.IsType<OkObjectResult>((await _controller.AggregatePortfolio(
            new PortfolioTailRequest { Basis = PortfolioBasis.Residual })).Result);
        var aggregate = Assert.IsType<PortfolioTailDto>(portfolio.Value);
        Assert.Equal((PortfolioBasis.Residual, TailRiskLimits.DefaultPortfolioSeed), (aggregate.Basis, aggregate.Seed));
        Assert.Equal(Known, Assert.Single(aggregate.Members).RiskId);
    }

    [Fact]
    public async Task TestWritesAnswerTheirDtosAndTheCorrelationRemovalIsNoContent()
    {
        var saved = Assert.IsType<OkObjectResult>((await _controller.SaveLossComponents(Known, Components)).Result);
        Assert.Equal(Known, Assert.IsType<RiskTailDto>(saved.Value).RiskId);

        var removed = Assert.IsType<OkObjectResult>((await _controller.DeleteLossComponents(Known)).Result);
        Assert.Equal(Known, Assert.IsType<RiskTailDto>(removed.Value).RiskId);

        var correlation = Assert.IsType<OkObjectResult>((await _controller.SaveCorrelation(Correlation(12, 11))).Result);
        var dto = Assert.IsType<RiskCorrelationDto>(correlation.Value);
        Assert.Equal((11, 12, 0.4m), (dto.RiskAId, dto.RiskBId, dto.Coefficient));

        Assert.IsType<NoContentResult>(await _controller.DeleteCorrelation(5));
    }

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        var bad = Assert.IsType<BadRequestObjectResult>((await _controller.SaveLossComponents(Invalid, null)).Result);
        Assert.Equal(("invalid_parameter", "Components"), (Property(bad.Value, "error"), Property(bad.Value, "ParameterName")));

        Assert.Equal("RiskAId", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.SaveCorrelation(null)).Result).Value, "ParameterName"));
        Assert.Equal("Basis", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.AggregatePortfolio(null)).Result).Value, "ParameterName"));
        Assert.IsType<BadRequestObjectResult>((await _controller.GetCorrelations(Invalid)).Result);
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryActionThatTakesAnId()
    {
        Assert.IsType<NotFoundResult>((await _controller.GetRisk(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.SaveLossComponents(Missing, Components)).Result);
        Assert.IsType<NotFoundResult>((await _controller.DeleteLossComponents(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetCorrelations(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.SaveCorrelation(Correlation(Missing))).Result);
        Assert.IsType<NotFoundResult>(await _controller.DeleteCorrelation(Missing));
    }

    /// <summary>A matrix that is not positive semidefinite, and a group too large, are 422 carrying the rule name the client shows.</summary>
    [Theory]
    [InlineData(MockedTailRiskService.NotPsd, "correlation_not_positive_semidefinite")]
    [InlineData(MockedTailRiskService.TooLarge, "correlation_group_too_large")]
    public async Task TestABrokenRuleIsA422NamingTheRule(int id, string rule)
    {
        var result = await _controller.SaveCorrelation(Correlation(id));

        var body = Assert.IsType<UnprocessableEntityObjectResult>(result.Result).Value;
        Assert.Equal(rule, Property(body, "error"));
        Assert.False(string.IsNullOrWhiteSpace(Property(body, "Message")));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _controller.GetRisk(Broken)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _controller.SaveLossComponents(Broken, Components)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _controller.DeleteLossComponents(Broken)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _controller.GetCorrelations(Broken)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _controller.SaveCorrelation(Correlation(Broken))).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>(await _controller.DeleteCorrelation(Broken)).StatusCode);
    }

    /// <summary>The portfolio's own failure path: anything the service throws that is not a domain exception is a bare 500.</summary>
    [Fact]
    public async Task TestAPortfolioFailureIsA500AndABrokenRuleA422()
    {
        var failing = Substitute.For<ITailRiskService>();
        failing.AggregatePortfolioAsync(Arg.Any<PortfolioTailRequest>()).Returns<PortfolioTailDto>(
            _ => throw new InvalidOperationException("boom"));
        var controller = ResolveController<TailRiskController>(s => s.AddSingleton(failing));
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await controller.AggregatePortfolio(new PortfolioTailRequest())).Result).StatusCode);

        failing.AggregatePortfolioAsync(Arg.Any<PortfolioTailRequest>()).Returns<PortfolioTailDto>(
            _ => throw new RuleBrokenException("Too large.", "correlation_group_too_large"));
        Assert.Equal("correlation_group_too_large", Property(Assert.IsType<UnprocessableEntityObjectResult>(
            (await controller.AggregatePortfolio(new PortfolioTailRequest())).Result).Value, "error"));
    }

    /// <summary>The acting user's id and the bodies reach every write; a controller passing a fixed value would be invisible above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedTailRiskService.Create();
        var controller = ResolveController<TailRiskController>(s => s.AddSingleton(recording));

        await controller.GetRisk(Known);
        await controller.SaveLossComponents(Known, Components);
        await controller.SaveLossComponents(Known + 1, null);
        await controller.DeleteLossComponents(Known);
        await controller.GetCorrelations(Known);
        await controller.GetCorrelations(null);
        await controller.SaveCorrelation(Correlation(Known));
        await controller.DeleteCorrelation(5);
        await controller.AggregatePortfolio(new PortfolioTailRequest { Basis = PortfolioBasis.Inherent, Seed = 7, EntityId = 3 });

        await recording.Received(1).GetRiskAsync(Known);
        await recording.Received(1).SaveLossComponentsAsync(Known,
            Arg.Is<LossComponentsRequest>(r => r.Components!.Single().Component == LossComponent.Response && r.Components![0].Max == 3), 1);
        await recording.Received(1).SaveLossComponentsAsync(Known + 1,
            Arg.Is<LossComponentsRequest>(r => r.Components == null), 1);
        await recording.Received(1).DeleteLossComponentsAsync(Known, 1);
        await recording.Received(1).GetCorrelationsAsync(Known);
        await recording.Received(1).GetCorrelationsAsync(null);
        await recording.Received(1).SaveCorrelationAsync(
            Arg.Is<RiskCorrelationRequest>(r => r.RiskAId == Known && r.RiskBId == 11 && r.Coefficient == 0.4m
                                                && r.Rationale == "Same supplier."), 1);
        await recording.Received(1).DeleteCorrelationAsync(5, 1);
        await recording.Received(1).AggregatePortfolioAsync(
            Arg.Is<PortfolioTailRequest>(r => r.Basis == PortfolioBasis.Inherent && r.Seed == 7 && r.EntityId == 3));
    }

    // --- RiskAppetites / TailLimits -----------------------------------------------------------

    [Fact]
    public async Task TestTheTailLimitsAreReadSavedAndRemoved()
    {
        var read = Assert.IsType<OkObjectResult>((await _appetites.GetTailLimits(Known)).Result);
        Assert.Equal((Known, 250_000m), (Assert.IsType<RiskAppetiteTailLimitsDto>(read.Value).AppetiteId,
            ((RiskAppetiteTailLimitsDto)read.Value!).MaxScenarioP95!.Value));

        var saved = Assert.IsType<OkObjectResult>((await _appetites.SaveTailLimits(Known, Limits)).Result);
        Assert.Equal(Known, Assert.IsType<RiskAppetiteTailLimitsDto>(saved.Value).AppetiteId);

        Assert.IsType<NoContentResult>(await _appetites.DeleteTailLimits(Known));
    }

    [Fact]
    public async Task TestTheTailLimitsMapDomainExceptionsToTheirStatus()
    {
        var bad = Assert.IsType<BadRequestObjectResult>((await _appetites.GetTailLimits(Invalid)).Result);
        Assert.Equal(("invalid_parameter", "Components"), (Property(bad.Value, "error"), Property(bad.Value, "ParameterName")));
        Assert.IsType<BadRequestObjectResult>((await _appetites.SaveTailLimits(Invalid, Limits)).Result);

        Assert.IsType<NotFoundResult>((await _appetites.GetTailLimits(Missing)).Result);
        Assert.IsType<NotFoundResult>((await _appetites.SaveTailLimits(Missing, Limits)).Result);
        Assert.IsType<NotFoundResult>(await _appetites.DeleteTailLimits(Missing));

        Assert.Equal("correlation_group_too_large", Property(Assert.IsType<UnprocessableEntityObjectResult>(
            (await _appetites.SaveTailLimits(MockedTailRiskService.TooLarge, Limits)).Result).Value, "error"));

        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _appetites.GetTailLimits(Broken)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await _appetites.SaveTailLimits(Broken, Limits)).Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>(await _appetites.DeleteTailLimits(Broken)).StatusCode);
    }

    [Fact]
    public async Task TestTheTailLimitsUserIdAndArgumentsReachTheService()
    {
        var recording = MockedTailRiskService.Create();
        var controller = ResolveController<RiskAppetitesController>(s => s.AddSingleton(recording));

        await controller.GetTailLimits(Known);
        await controller.SaveTailLimits(Known, Limits);
        await controller.SaveTailLimits(Known + 1, null);
        await controller.DeleteTailLimits(Known);

        await recording.Received(1).GetAppetiteLimitsAsync(Known);
        await recording.Received(1).SaveAppetiteLimitsAsync(Known,
            Arg.Is<RiskAppetiteTailLimitsRequest>(r => r.MaxScenarioP95 == 250_000m && r.Rationale == "Phase 0 decision."), 1);
        await recording.Received(1).SaveAppetiteLimitsAsync(Known + 1,
            Arg.Is<RiskAppetiteTailLimitsRequest>(r => r.MaxScenarioP95 == null), 1);
        await recording.Received(1).DeleteAppetiteLimitsAsync(Known, 1);
    }
}
