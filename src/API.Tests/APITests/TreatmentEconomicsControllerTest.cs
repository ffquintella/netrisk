using System;
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
using Model.TreatmentEconomics;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.6 (S47 §6, §8) — <see cref="TreatmentEconomicsController"/>: each action's success shape, and the mapping of
/// every domain exception onto the status code the other controllers use for it.
/// </summary>
[TestSubject(typeof(TreatmentEconomicsController))]
public class TreatmentEconomicsControllerTest : BaseControllerTest
{
    private readonly TreatmentEconomicsController _controller;

    private const int Known = MockedTreatmentEconomicsService.Known;

    public TreatmentEconomicsControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<TreatmentEconomicsController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly MitigationEconomicsRequest Economics = new()
    {
        Option = TreatmentOption.Reduce,
        Cost = new TreatmentCostRequest { OneTime = 0, Annual = 15_000, SideEffectsAnnual = 0 }
    };

    private static readonly RiskTargetRequest Target = new() { TargetScore = 3, Rationale = "MFA." };

    [Fact]
    public async Task TestReadsAnswer200WithTheirDtos()
    {
        var mitigation = Assert.IsType<OkObjectResult>((await _controller.GetMitigation(Known)).Result);
        Assert.Equal(GateCOutcome.Passes, Assert.IsType<MitigationEconomicsDto>(mitigation.Value).GateC.Outcome);

        var risk = Assert.IsType<OkObjectResult>((await _controller.GetRisk(Known)).Result);
        var view = Assert.IsType<RiskTreatmentEconomicsDto>(risk.Value);
        Assert.True(view.Systemic);
        Assert.Equal(3m, view.Target!.TargetScore);

        var portfolio = Assert.IsType<OkObjectResult>((await _controller.SelectPortfolio(
            new PortfolioSelectionRequest { Budget = 100 })).Result);
        var selection = Assert.IsType<PortfolioSelectionDto>(portfolio.Value);
        Assert.Equal((100m, PortfolioTier.Protected), (selection.Budget, selection.Items.Single().Tier));
    }

    [Fact]
    public async Task TestWritesAnswerTheirDtosAndTheRemovalIsNoContent()
    {
        var saved = Assert.IsType<OkObjectResult>((await _controller.SaveMitigation(Known, Economics)).Result);
        Assert.Equal(Known, Assert.IsType<MitigationEconomicsDto>(saved.Value).MitigationId);

        var target = Assert.IsType<OkObjectResult>((await _controller.SaveTarget(Known, Target)).Result);
        Assert.True(Assert.IsType<RiskTargetDto>(target.Value).Status.WithinAppetite);

        Assert.IsType<NoContentResult>(await _controller.DeleteTarget(Known));
    }

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        const int invalid = MockedTreatmentEconomicsService.Invalid;

        var bad = Assert.IsType<BadRequestObjectResult>((await _controller.SaveMitigation(invalid, null)).Result);
        Assert.Equal(("invalid_parameter", "Option"), (Property(bad.Value, "error"), Property(bad.Value, "ParameterName")));

        Assert.IsType<BadRequestObjectResult>((await _controller.SaveTarget(invalid, Target)).Result);
        Assert.Equal("Budget", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.SelectPortfolio(null)).Result).Value, "ParameterName"));
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryActionThatTakesAnId()
    {
        const int missing = MockedTreatmentEconomicsService.Missing;

        Assert.IsType<NotFoundResult>((await _controller.GetMitigation(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.SaveMitigation(missing, Economics)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetRisk(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.SaveTarget(missing, Target)).Result);
        Assert.IsType<NotFoundResult>(await _controller.DeleteTarget(missing));
    }

    /// <summary>Gate A refusing "accept", and a dependency cycle, are 422 carrying the rule name the client shows.</summary>
    [Theory]
    [InlineData(MockedTreatmentEconomicsService.GateA, "gate_a_non_discretionary")]
    [InlineData(MockedTreatmentEconomicsService.Cycle, "dependency_cycle")]
    public async Task TestABrokenRuleIsA422NamingTheRule(int id, string rule)
    {
        var result = await _controller.SaveMitigation(id, new MitigationEconomicsRequest { Option = TreatmentOption.Accept });

        Assert.Equal(rule, Property(Assert.IsType<UnprocessableEntityObjectResult>(result.Result).Value, "error"));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        var result = await _controller.GetRisk(MockedTreatmentEconomicsService.Broken);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>(await _controller.DeleteTarget(MockedTreatmentEconomicsService.Broken)).StatusCode);
    }

    /// <summary>The acting user's id and the bodies reach every write; a controller passing a fixed value would be invisible above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedTreatmentEconomicsService.Create();
        var controller = ResolveController<TreatmentEconomicsController>(s => s.AddSingleton(recording));

        await controller.SaveMitigation(Known, Economics);
        await controller.SaveTarget(Known, Target);
        await controller.DeleteTarget(Known);
        await controller.SelectPortfolio(new PortfolioSelectionRequest { Budget = 7 });

        await recording.Received(1).SaveMitigationAsync(Known,
            Arg.Is<MitigationEconomicsRequest>(r => r.Option == TreatmentOption.Reduce && r.Cost!.Annual == 15_000), 1);
        await recording.Received(1).SaveTargetAsync(Known, Arg.Is<RiskTargetRequest>(r => r.Rationale == "MFA."), 1);
        await recording.Received(1).DeleteTargetAsync(Known, 1);
        await recording.Received(1).SelectPortfolioAsync(Arg.Is<PortfolioSelectionRequest>(r => r.Budget == 7));
    }
}
