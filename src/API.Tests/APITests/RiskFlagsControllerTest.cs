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
using Model.RiskFlags;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.5 (S46 §6, §8) — <see cref="RiskFlagsController"/>: each action's success shape, and the mapping of
/// every domain exception onto the status code the other controllers use for it.
/// </summary>
[TestSubject(typeof(RiskFlagsController))]
public class RiskFlagsControllerTest : BaseControllerTest
{
    private readonly RiskFlagsController _controller;

    private const int Known = MockedRiskFlagsService.Known;

    public RiskFlagsControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<RiskFlagsController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly RiskFlagDeclarationRequest Declaration = new() { Reason = "Patients." };
    private static readonly RiskFlagWithdrawalRequest Withdrawal = new() { Reason = "Decommissioned." };
    private static readonly RiskDecisionRequest Decision = new() { Decision = RiskDecisionKind.ActImmediately, Reason = "Now." };

    [Fact]
    public async Task TestReadsAnswer200WithTheirDtos()
    {
        var catalogue = Assert.IsType<OkObjectResult>(_controller.GetCatalogue().Result);
        Assert.Equal(12, Assert.IsAssignableFrom<IReadOnlyList<RiskFlagDescriptor>>(catalogue.Value).Count);

        var state = Assert.IsType<OkObjectResult>((await _controller.GetRiskFlags(Known)).Result);
        Assert.True(Assert.IsType<RiskFlagsStateDto>(state.Value).GateA.Holds);

        var refreshed = Assert.IsType<OkObjectResult>((await _controller.Refresh(Known)).Result);
        Assert.Equal(Known, Assert.IsType<RiskFlagsStateDto>(refreshed.Value).RiskId);

        var decisions = Assert.IsType<OkObjectResult>((await _controller.GetDecisions(Known)).Result);
        Assert.Equal(RiskDecisionKind.ActImmediately,
            Assert.Single(Assert.IsType<List<RiskDecisionDto>>(decisions.Value)).Decision);

        var flagged = Assert.IsType<OkObjectResult>((await _controller.GetFlagged(RiskFlagCode.HumanSafety, true)).Result);
        Assert.True(Assert.Single(Assert.IsType<List<FlaggedRiskDto>>(flagged.Value)).GateA);

        var top = Assert.IsType<OkObjectResult>((await _controller.GetTopRisks(5)).Result);
        Assert.Equal(5, Assert.IsType<TopRisksDto>(top.Value).Limit);
    }

    [Fact]
    public async Task TestWritesAnswerTheirStatesAndTheDecisionIsCreated()
    {
        var declared = Assert.IsType<OkObjectResult>((await _controller.Declare(Known, RiskFlagCode.HumanSafety, Declaration)).Result);
        Assert.IsType<RiskFlagsStateDto>(declared.Value);

        var withdrawn = Assert.IsType<OkObjectResult>((await _controller.Withdraw(Known, RiskFlagCode.HumanSafety, Withdrawal)).Result);
        Assert.IsType<RiskFlagsStateDto>(withdrawn.Value);

        var created = Assert.IsType<CreatedAtActionResult>((await _controller.RecordDecision(Known, Decision)).Result);
        Assert.Equal(nameof(RiskFlagsController.GetDecisions), created.ActionName);
        Assert.Equal(Known, created.RouteValues!["id"]);
        Assert.NotNull(Assert.IsType<RiskDecisionDto>(created.Value).EscalatedAt);
    }

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        const int invalid = MockedRiskFlagsService.Invalid;

        var bad = Assert.IsType<BadRequestObjectResult>(
            (await _controller.Declare(invalid, RiskFlagCode.HumanSafety, Declaration)).Result);
        Assert.Equal(("invalid_parameter", "Reason"), (Property(bad.Value, "error"), Property(bad.Value, "ParameterName")));

        Assert.IsType<BadRequestObjectResult>((await _controller.Withdraw(invalid, RiskFlagCode.HumanSafety, null)).Result);
        Assert.IsType<BadRequestObjectResult>((await _controller.RecordDecision(invalid, null)).Result);
        Assert.Equal("flag", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.GetFlagged((RiskFlagCode)99)).Result).Value, "ParameterName"));
        Assert.Equal("limit", Property(Assert.IsType<BadRequestObjectResult>(
            (await _controller.GetTopRisks(0)).Result).Value, "ParameterName"));
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryActionThatTakesARisk()
    {
        const int missing = MockedRiskFlagsService.Missing;

        Assert.IsType<NotFoundResult>((await _controller.GetRiskFlags(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.Refresh(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.Declare(missing, RiskFlagCode.HumanSafety, Declaration)).Result);
        Assert.IsType<NotFoundResult>((await _controller.Withdraw(missing, RiskFlagCode.HumanSafety, Withdrawal)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetDecisions(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.RecordDecision(missing, Decision)).Result);
    }

    [Fact]
    public async Task TestAnInvalidTransitionIsA422()
    {
        var result = await _controller.Declare(MockedRiskFlagsService.Transition, RiskFlagCode.HumanSafety, Declaration);

        Assert.Equal("invalid_transition", Property(Assert.IsType<UnprocessableEntityObjectResult>(result.Result).Value, "error"));
    }

    /// <summary>Gate A (and segregation of duties) refusals are 422 carrying the rule name the client shows.</summary>
    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        const int gate = MockedRiskFlagsService.GateA;

        var decision = await _controller.RecordDecision(gate, new RiskDecisionRequest
            { Decision = RiskDecisionKind.MonitorAccept, Reason = "Within appetite." });
        Assert.Equal("gate_a_non_discretionary",
            Property(Assert.IsType<UnprocessableEntityObjectResult>(decision.Result).Value, "error"));

        var withdraw = await _controller.Withdraw(gate, RiskFlagCode.HumanSafety, Withdrawal);
        Assert.IsType<UnprocessableEntityObjectResult>(withdraw.Result);
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        var result = await _controller.GetRiskFlags(MockedRiskFlagsService.Broken);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    /// <summary>The acting user's id reaches every write; a controller passing a fixed value would be invisible above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedRiskFlagsService.Create();
        var controller = ResolveController<RiskFlagsController>(s => s.AddSingleton(recording));

        await controller.Declare(Known, RiskFlagCode.KnownExploitation, Declaration);
        await controller.Withdraw(Known, RiskFlagCode.LegalRegulatory, Withdrawal);
        await controller.RecordDecision(Known, Decision);
        await controller.GetFlagged(null, false);
        await controller.GetTopRisks();

        await recording.Received(1).DeclareAsync(Known, RiskFlagCode.KnownExploitation,
            Arg.Is<RiskFlagDeclarationRequest>(r => r.Reason == "Patients."), 1);
        await recording.Received(1).WithdrawAsync(Known, RiskFlagCode.LegalRegulatory,
            Arg.Is<RiskFlagWithdrawalRequest>(r => r.Reason == "Decommissioned."), 1);
        await recording.Received(1).RecordDecisionAsync(Known, Arg.Any<RiskDecisionRequest>(), 1);
        await recording.Received(1).GetFlaggedAsync(null, false);
        await recording.Received(1).GetTopRisksAsync(10);
    }
}
