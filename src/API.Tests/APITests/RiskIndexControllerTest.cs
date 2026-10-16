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
using Model.RiskIndex;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// M53, T249 (S40 §8.2, §12.3 <c>RiskIndexControllerTest</c>): each read action's success shape, <c>X-Total-Count</c>, and
/// the mapping of every refusal of the read service onto <c>{ error, message }</c> with its status — 503 with
/// <c>Retry-After</c> for the derived-computation budget.
/// </summary>
[TestSubject(typeof(RiskIndexController))]
public class RiskIndexControllerTest : BaseControllerTest
{
    private readonly RiskIndexController _controller;

    public RiskIndexControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<RiskIndexController>();
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    private static string? Error(IActionResult? result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(((ObjectResult)result!).Value)).RootElement.GetProperty("error").GetString();

    [Fact]
    public async Task TestTheReadsAnswer200()
    {
        Assert.IsType<RiskIndexLatestDto>(Assert.IsType<OkObjectResult>((await _controller.GetLatest()).Result).Value);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DateOnly>>(
            Assert.IsType<OkObjectResult>((await _controller.GetSnapshotDates()).Result).Value));
        var overview = Assert.IsType<RiskIndexOverviewDto>(Assert.IsType<OkObjectResult>((await _controller.GetOverview("org")).Result).Value);
        Assert.Equal(64, overview.Headline!.Displayed);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<RiskIndexCategoryDto>>(
            Assert.IsType<OkObjectResult>((await _controller.GetCategories()).Result).Value));
        Assert.Equal(30, Assert.IsType<RiskIndexTrendDto>(
            Assert.IsType<OkObjectResult>((await _controller.GetTrend(window: 30)).Result).Value).WindowDays);
        Assert.Equal(RiskIndexDimension.Process, Assert.IsType<RiskIndexNodeChildrenDto>(
            Assert.IsType<OkObjectResult>((await _controller.GetNodes(dimension: RiskIndexDimension.Process)).Result).Value).Dimension);
        Assert.IsType<RiskIndexObjectDetailDto>(Assert.IsType<OkObjectResult>(
            (await _controller.GetObject(RiskIndexObjectKind.Host, "101")).Result).Value);
        Assert.Equal(RiskIndexTabGroup.Devices, Assert.IsType<RiskIndexInventoryDto>(Assert.IsType<OkObjectResult>(
            (await _controller.GetInventory(tab: RiskIndexTabGroup.Devices)).Result).Value).Tab);
        Assert.Equal(-4, Assert.IsType<RiskIndexChangesDto>(Assert.IsType<OkObjectResult>((await _controller.GetChanges()).Result).Value)
            .DeltaDisplayed);
        Assert.IsType<RiskIndexContributionsDto>(Assert.IsType<OkObjectResult>((await _controller.GetContributions()).Result).Value);
        Assert.Equal(3, Assert.IsType<RiskIndexGovernanceIndicatorsDto>(
            Assert.IsType<OkObjectResult>((await _controller.GetGovernanceIndicators()).Result).Value).AcceptancesExpiringSoon);
        Assert.IsType<RiskIndexPublicationDto>(Assert.IsType<OkObjectResult>((await _controller.GetPublication()).Result).Value);
    }

    [Fact]
    public async Task TestTheObjectListCarriesItsTotal()
    {
        var result = await _controller.GetObjects(pageSize: 1);

        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<RiskIndexObjectRowDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
        Assert.Equal("37", _controller.Response.Headers["X-Total-Count"].ToString());
    }

    [Theory]
    [InlineData("bad", 400, "invalid_node")]
    [InlineData("shadow", 403, "risk_index_in_shadow")]
    [InlineData("gone", 404, "node_not_found")]
    [InlineData("partial", 409, "trend_unavailable_partial")]
    [InlineData("busy", 503, "derived_computation_busy")]
    public async Task TestEachRefusalKeepsItsStatusAndCode(string node, int status, string code)
    {
        var results = new IActionResult?[]
        {
            (await _controller.GetOverview(node)).Result, (await _controller.GetCategories(node)).Result,
            (await _controller.GetTrend(node)).Result, (await _controller.GetNodes(node)).Result,
            (await _controller.GetObjects(node)).Result, (await _controller.GetInventory(node, RiskIndexTabGroup.Devices)).Result,
            (await _controller.GetChanges(node)).Result, (await _controller.GetContributions(node)).Result,
            (await _controller.GetGovernanceIndicators(node)).Result
        };

        Assert.All(results, r =>
        {
            Assert.Equal(status, ((ObjectResult)r!).StatusCode);
            Assert.Equal(code, Error(r));
        });
        if (status == 503) Assert.Equal("5", _controller.Response.Headers["Retry-After"].ToString());
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsABare500()
    {
        var result = (await _controller.GetOverview("boom")).Result;

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task TestTheRemainingRefusals()
    {
        Assert.Equal("object_not_found", Error((await _controller.GetObject(RiskIndexObjectKind.Host, "404")).Result));
        Assert.Equal("invalid_tab", Error((await _controller.GetInventory()).Result));
        Assert.Equal("no_approved_profile",
            Error((await _controller.SetPublication(new RiskIndexPublicationRequest(RiskIndexPublicationState.Published))).Result));
        Assert.Equal("invalid_state", Error((await _controller.SetPublication(null)).Result));
        Assert.IsType<OkObjectResult>((await _controller.SetPublication(new RiskIndexPublicationRequest(RiskIndexPublicationState.Shadow))).Result);
    }
}
