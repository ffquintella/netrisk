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
            (await _controller.GetGovernanceIndicators(node)).Result, (await _controller.GetQuality(node)).Result,
            (await _controller.GetMonthlySummary(node, RiskIndexTabGroup.Devices)).Result,
            (await _controller.GetRiskFactors(node, RiskIndexTabGroup.Devices)).Result,
            (await _controller.GetDimensions(node)).Result
        };

        Assert.All(results, r =>
        {
            Assert.Equal(status, ((ObjectResult)r!).StatusCode);
            Assert.Equal(code, Error(r));
        });
        if (status == 503) Assert.Equal("5", _controller.Response.Headers["Retry-After"].ToString());
    }

    /// <summary>T253 (S40 §8.2): the two tab reads answer their DTOs, the summary keyed by category and null kept as no value.</summary>
    [Fact]
    public async Task TestTheMonthlySummaryAndTheRiskFactorsAnswer200()
    {
        var summary = Assert.IsType<RiskIndexMonthlySummaryDto>(Assert.IsType<OkObjectResult>(
            (await _controller.GetMonthlySummary("e:7", RiskIndexTabGroup.Devices, 12)).Result).Value);
        var factors = Assert.IsAssignableFrom<IReadOnlyList<RiskIndexRiskFactorDto>>(Assert.IsType<OkObjectResult>(
            (await _controller.GetRiskFactors("e:7", RiskIndexTabGroup.Devices)).Result).Value);

        Assert.Equal((RiskIndexTabGroup?)RiskIndexTabGroup.Devices, summary.Tab);
        Assert.Equal(74.2, summary.Months[0].MeanByCategory[RiskIndexCategory.Exp]);
        Assert.Null(summary.Months[0].MeanByCategory[RiskIndexCategory.Gov]);
        Assert.Equal(("EXP.V", 1204, 18, "factor=EXP.V"), (factors[0].FactorKey, factors[0].Count, factors[0].Points, factors[0].ObjectFilter));
    }

    /// <summary>T254 (S40 §8.2): the values present in a node, each with its node-key term, for the Explore panel's filters.</summary>
    [Fact]
    public async Task TestTheDimensionsAnswer200WithTheirTerms()
    {
        var dimensions = Assert.IsType<RiskIndexDimensionsDto>(Assert.IsType<OkObjectResult>(
            (await _controller.GetDimensions("e:7")).Result).Value);

        Assert.Equal(["c:devices", "c:server"], dimensions.AssetClasses.Select(v => v.Term));
        Assert.Equal(("s:V1-DEV", 7), (dimensions.Sources[0].Term, dimensions.Sources[0].VisibleObjects));
        Assert.Equal("k:5", Assert.Single(dimensions.Criticalities).Term);
        Assert.Equal("a:prod", Assert.Single(dimensions.Environments).Term);
    }

    /// <summary>T256 (S40 §5.12, §8.2): the data quality answers its fractions, stale sources and the counts it cannot measure as null.</summary>
    [Fact]
    public async Task TestTheQualityAnswers200WithItsUnmeasuredCountsAsNull()
    {
        var quality = Assert.IsType<RiskIndexQualityDto>(Assert.IsType<OkObjectResult>(
            (await _controller.GetQuality("org", MockedRiskIndexReadService.Date)).Result).Value);

        Assert.Equal((0.12, true, 0.34), (quality.UnassignedWeightFraction, quality.UnassignedWarning,
            quality.DefaultCriticalityWeightFraction));
        Assert.Equal("V1-DEV", Assert.Single(quality.StaleSources).SourceKey);
        Assert.Equal((7, 18, 5), (quality.PresumedCritical, quality.OrphanApplications!.Value, quality.ResidualAboveInherent));
        Assert.Null(quality.SscPseudoHosts);
        Assert.Null(quality.ExitedWithBadReading);
        Assert.Equal("requires_m40", quality.EvidenceConfidenceStateKey);
    }

    [Fact]
    public async Task TestTheMonthlySummaryAndTheRiskFactorsRefuseTheirOwnArguments()
    {
        Assert.Equal("invalid_months", Error((await _controller.GetMonthlySummary(months: 3)).Result));
        Assert.Equal(400, ((ObjectResult)(await _controller.GetMonthlySummary(months: 3)).Result!).StatusCode);
        Assert.Equal("invalid_tab", Error((await _controller.GetRiskFactors()).Result));
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
