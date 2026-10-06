using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Risks.Chain;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.1 (S41 §8) — <see cref="RiskChainController"/>: each action's success shape, and the
/// mapping of every domain exception onto the status code the other controllers use for it.
/// </summary>
[TestSubject(typeof(RiskChainController))]
public class RiskChainControllerTest : BaseControllerTest
{
    private readonly RiskChainController _controller;

    public RiskChainControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<RiskChainController>();
    }

    private static string? ErrorOf(object? body) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty("error").GetString();

    // --- success shapes -------------------------------------------------------------------------

    [Fact]
    public async Task TestGetRiskChainReturnsTheFiveLevels()
    {
        var result = await _controller.GetRiskChain(MockedRiskChainService.KnownRisk);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var chain = Assert.IsType<RiskChainDto>(ok.Value);
        Assert.Equal(5, chain.Levels.Count);
        Assert.Equal(4, chain.MissingLevels.Count);
    }

    [Fact]
    public async Task TestAddLinkCreatesAndPointsAtTheChain()
    {
        var result = await _controller.AddLink(MockedRiskChainService.KnownRisk,
            new RiskChainLinkCreateDto { EntityId = MockedRiskChainService.NewTarget });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(RiskChainController.GetRiskChain), created.ActionName);
        Assert.Equal(MockedRiskChainService.KnownRisk, created.RouteValues!["riskId"]);
        var link = Assert.IsType<RiskChainLinkDto>(created.Value);
        Assert.Equal(RiskChainLinkOrigin.Declared, link.Origin);
    }

    [Fact]
    public async Task TestAddLinkOverALegacyLinkIsAPromotionAnsweredWith200()
    {
        var result = await _controller.AddLink(MockedRiskChainService.KnownRisk,
            new RiskChainLinkCreateDto { EntityId = MockedRiskChainService.LegacyTarget });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsType<RiskChainLinkDto>(ok.Value).Id);
    }

    [Fact]
    public async Task TestDeleteLinkAnswers204WhenDeleted()
    {
        var result = await _controller.DeleteLink(MockedRiskChainService.KnownRisk, MockedRiskChainService.DeletableLink);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task TestDeleteLinkAnswers200WithTheLinkWhenDemoted()
    {
        var result = await _controller.DeleteLink(MockedRiskChainService.KnownRisk, MockedRiskChainService.DemotableLink);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(RiskChainLinkOrigin.Legacy, Assert.IsType<RiskChainLinkDto>(ok.Value).Origin);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task TestGetRisksByEntityPassesTheInferenceFlag(bool inferred, int expected)
    {
        var result = await _controller.GetRisksByEntity(MockedRiskChainService.NewTarget, inferred);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expected, Assert.IsType<List<RiskChainMatchDto>>(ok.Value).Count);
    }

    [Fact]
    public async Task TestGetRisksByHost()
    {
        var result = await _controller.GetRisksByHost(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<List<RiskChainMatchDto>>(ok.Value));
    }

    [Fact]
    public async Task TestGetCriticalProcessCoverage()
    {
        var result = await _controller.GetCriticalProcessCoverage();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var coverage = Assert.IsType<CriticalProcessCoverageDto>(ok.Value);
        Assert.Equal(1m / 3m, coverage.CoverageRatio);
        Assert.True(coverage.IsScopeRestricted);
    }

    // --- error mapping --------------------------------------------------------------------------

    [Fact]
    public async Task TestNeitherTargetIsABadRequestNamingTheParameter()
    {
        var result = await _controller.AddLink(MockedRiskChainService.KnownRisk, new RiskChainLinkCreateDto());

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("invalid_parameter", ErrorOf(bad.Value));
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryAction()
    {
        Assert.IsType<NotFoundResult>((await _controller.GetRiskChain(MockedRiskChainService.Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.AddLink(MockedRiskChainService.Missing,
            new RiskChainLinkCreateDto { EntityId = MockedRiskChainService.NewTarget })).Result);
        Assert.IsType<NotFoundResult>((await _controller.DeleteLink(1, MockedRiskChainService.Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetRisksByEntity(MockedRiskChainService.Missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetRisksByHost(MockedRiskChainService.Missing)).Result);
    }

    [Fact]
    public async Task TestAnExistingDeclaredLinkIsAConflict()
    {
        var result = await _controller.AddLink(MockedRiskChainService.KnownRisk,
            new RiskChainLinkCreateDto { EntityId = MockedRiskChainService.Conflicting });

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("already_exists", ErrorOf(conflict.Value));
    }

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        var notInChain = await _controller.AddLink(MockedRiskChainService.KnownRisk,
            new RiskChainLinkCreateDto { EntityId = MockedRiskChainService.OutsideChain });
        Assert.Equal("entity_not_in_chain",
            ErrorOf(Assert.IsType<UnprocessableEntityObjectResult>(notInChain.Result).Value));

        var legacy = await _controller.DeleteLink(MockedRiskChainService.KnownRisk, MockedRiskChainService.LegacyLink);
        Assert.Equal("legacy_link", ErrorOf(Assert.IsType<UnprocessableEntityObjectResult>(legacy.Result).Value));

        var query = await _controller.GetRisksByEntity(MockedRiskChainService.OutsideChain);
        Assert.Equal("entity_not_in_chain", ErrorOf(Assert.IsType<UnprocessableEntityObjectResult>(query.Result).Value));
    }

    [Fact]
    public async Task TestAMissingHostsPermissionIsA403()
    {
        var add = await _controller.AddLink(MockedRiskChainService.KnownRisk,
            new RiskChainLinkCreateDto { HostId = MockedRiskChainService.Forbidden });
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(add.Result).StatusCode);

        var delete = await _controller.DeleteLink(MockedRiskChainService.KnownRisk, MockedRiskChainService.Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(delete.Result).StatusCode);

        var byHost = await _controller.GetRisksByHost(MockedRiskChainService.Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(byHost.Result).StatusCode);
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500()
    {
        var failing = Substitute.For<IRiskChainService>();
        failing.GetCriticalProcessCoverageAsync().ThrowsAsync(new InvalidOperationException("boom"));

        var controller = ResolveController<RiskChainController>(s => s.AddSingleton(failing));

        var result = await controller.GetCriticalProcessCoverage();

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    // --- the principal reaches the service ------------------------------------------------------

    /// <summary>
    /// The <c>hosts</c> check lives in the service and reads the principal it is handed. A controller
    /// that passed null would make every host call a refusal; one that passed a fixed principal would
    /// make it a bypass. Either is invisible to the mocks above, so the double records what arrived.
    /// </summary>
    [Fact]
    public async Task TestTheRequestPrincipalAndUserIdReachTheService()
    {
        var recording = MockedRiskChainService.Create();
        var controller = ResolveController<RiskChainController>(s => s.AddSingleton(recording));

        await controller.GetRiskChain(MockedRiskChainService.KnownRisk);
        await controller.AddLink(MockedRiskChainService.KnownRisk, new RiskChainLinkCreateDto { HostId = 1 });
        await controller.DeleteLink(MockedRiskChainService.KnownRisk, MockedRiskChainService.DeletableLink);
        await controller.GetRisksByHost(1);

        await recording.Received(1).GetRiskChainAsync(MockedRiskChainService.KnownRisk,
            Arg.Is<ClaimsPrincipal?>(p => p != null && p.Identity!.Name == "testUser"));
        await recording.Received(1).AddLinkAsync(MockedRiskChainService.KnownRisk,
            Arg.Is<RiskChainLinkCreateDto>(r => r.HostId == 1), 1,
            Arg.Is<ClaimsPrincipal?>(p => p != null && p.Identity!.Name == "testUser"));
        await recording.Received(1).DeleteLinkAsync(MockedRiskChainService.KnownRisk,
            MockedRiskChainService.DeletableLink,
            Arg.Is<ClaimsPrincipal?>(p => p != null && p.Identity!.Name == "testUser"));
        await recording.Received(1).GetRisksByHostAsync(1,
            Arg.Is<ClaimsPrincipal?>(p => p != null && p.Identity!.Name == "testUser"));
    }
}
