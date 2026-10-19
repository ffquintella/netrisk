using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.RiskIndex;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// M54 (S40 §8.2, §8.9, §12.5) — <see cref="RiskIndexRestService"/> over <see cref="StubRestBackend"/>: every route, verb
/// and query it builds, the API's camelCase body read back into the positional records, <c>X-Total-Count</c>, and every
/// refusal kept with its status, stable <c>error</c> code and the server's sentence (401 also discards the token).
/// </summary>
[TestSubject(typeof(RiskIndexRestService))]
public class RiskIndexRestServiceTest : BaseServiceTest
{
    private static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web);

    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IRiskIndexService _service;

    public RiskIndexRestServiceTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IRiskIndexService>();
    }

    /// <summary>Serialized as the API does (MVC web defaults), not with the stub's PascalCase default.</summary>
    private StubRestBackend Answer(string path, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _backend.On(Method.Get, path, JsonSerializer.Serialize(body, Api), status);

    private static readonly Dictionary<string, string> NoArgs = new();

    private static RiskIndexProfileSummaryDto Profile(int id = 3, RiskIndexProfileStatus status = RiskIndexProfileStatus.Active) =>
        new(id, id, "Equilibrado", "balanced", status, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), null, "ana",
            "bruno", false, "ATA-2026-09");

    private static RiskIndexOverviewDto Overview() => new(
        new RiskIndexNodeRef("e:7", "Unidade X", "organizationUnit"),
        [new RiskIndexNodeRef("org", "Organization", "organization")],
        new DateOnly(2026, 10, 5), new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc), RiskIndexProvenance.Official,
        Profile(), RiskIndexMode.Interim,
        new RiskIndexHeadlineDto(64, 63.6, 63.6, "b1", 1, RiskIndexDisplayState.Point, 53.2, 79.4, false,
            new RiskIndexDeltaDto(4, RiskIndexDeltaDirection.Worse, new DateOnly(2026, 9, 28), null, null), [], true, null),
        [new RiskIndexCategoryDto(RiskIndexCategory.Reg, RiskIndexCategoryState.Available, 70.1, 70, "b1", 0.3, 0.35, 24.5, null,
            0.9, 0.99, 0, null, null, [])],
        [new RiskIndexSealDto("scenarios_above_appetite", 2, NoArgs, "seal=above_appetite")],
        [new RiskIndexChipDto("no_critical_process", NoArgs, null, null)],
        new RiskIndexQualitySummaryDto(0.92, 0.99, 0.91, "high", []), 310,
        [new RiskIndexBandDto("b0", "Baixo", "Low", 0, 1, 0), new RiskIndexBandDto("b1", "Médio", "Medium", 1, 3, 31),
            new RiskIndexBandDto("b2", "Alto", "High", 2, 5, 70)],
        []);

    [Fact]
    public async Task TestTheLatestSnapshotIsReadAndNoContentIsNull()
    {
        Answer("/RiskIndex/Snapshots/Latest",
            new RiskIndexLatestDto(new DateOnly(2026, 10, 5), 3, RiskIndexMode.Interim, RiskIndexPublicationState.Shadow));

        var latest = await _service.GetLatestAsync();

        Assert.Equal((new DateOnly(2026, 10, 5), 3, RiskIndexPublicationState.Shadow),
            (latest!.Date, latest.ProfileId, latest.Publication));
        Assert.Equal("GET /RiskIndex/Snapshots/Latest", _backend.LastRequest.ToString());

        _backend.OnStatus(Method.Get, "/RiskIndex/Snapshots/Latest", HttpStatusCode.NoContent);
        Assert.Null(await _service.GetLatestAsync());
    }

    [Fact]
    public async Task TestTheSnapshotDatesSendTheRangeOnlyWhenGiven()
    {
        Answer("/RiskIndex/Snapshots/Dates", new[] { new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5) });

        var all = await _service.GetSnapshotDatesAsync();
        Assert.Equal([new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5)], all);
        Assert.Equal("", _backend.LastRequest.Query);

        await _service.GetSnapshotDatesAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 5));
        Assert.Contains("from=2026-09-01", _backend.LastRequest.Query);
        Assert.Contains("to=2026-10-05", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheOverviewIsReadWithItsNodeAndDate()
    {
        Answer("/RiskIndex/Overview", Overview());

        var overview = await _service.GetOverviewAsync("e:7", new DateOnly(2026, 10, 5));

        Assert.Equal("e:7", overview.Node.Key);
        Assert.Equal(64, overview.Headline!.Displayed);
        Assert.Equal(RiskIndexDeltaDirection.Worse, overview.Headline.Delta7d!.Direction);
        Assert.Equal(3, overview.Bands.Count);
        Assert.Equal(3, overview.Bands[1].RampStep);
        Assert.Equal("scenarios_above_appetite", Assert.Single(overview.Seals).Key);
        Assert.Equal(RiskIndexProfileStatus.Active, overview.Profile.Status);
        Assert.Equal(0.91, overview.Quality.Quality);
        Assert.Contains("node=e:7", Uri.UnescapeDataString(_backend.LastRequest.Query));
        Assert.Contains("date=2026-10-05", _backend.LastRequest.Query);

        await _service.GetOverviewAsync();
        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheCategoriesTrendAndNodesSendTheirParameters()
    {
        Answer("/RiskIndex/Categories", Overview().Categories);
        Answer("/RiskIndex/Trend", new RiskIndexTrendDto(new RiskIndexNodeRef("org", "Organization", "organization"), 30,
            RiskIndexCategory.Exp, [], [], [], [], [], [], null, null));
        Answer("/RiskIndex/Nodes", new RiskIndexNodeChildrenDto(new RiskIndexNodeRef("org", "Organization", "organization"),
            RiskIndexDimension.Process, [], [], 0));

        Assert.Equal(RiskIndexCategory.Reg, Assert.Single(await _service.GetCategoriesAsync("org")).Category);

        var trend = await _service.GetTrendAsync("org", 30, RiskIndexCategory.Exp);
        Assert.Equal((30, RiskIndexCategory.Exp), (trend.WindowDays, trend.Series));
        Assert.Contains("window=30", _backend.LastRequest.Query);
        Assert.Contains("series=2", _backend.LastRequest.Query);

        var nodes = await _service.GetNodesAsync("org", RiskIndexDimension.Process, "c:server");
        Assert.Equal(RiskIndexDimension.Process, nodes.Dimension);
        Assert.Contains("parent=org", _backend.LastRequest.Query);
        Assert.Contains("dimension=2", _backend.LastRequest.Query);
        Assert.Contains("filters=c:server", Uri.UnescapeDataString(_backend.LastRequest.Query));
    }

    [Fact]
    public async Task TestTheObjectsReadTheTotalFromTheHeader()
    {
        var row = new RiskIndexObjectRowDto(
            new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Host, "12"), "Host #12", "hosts"), "server",
            RiskIndexCategory.Exp, 0.8, 4, "entity", RiskIndexObjectState.Assessed, 1, null, 2.5, false, 0, ["TEN-NES"], "prod");
        Answer("/RiskIndex/Objects", new[] { row }).WithHeader(Method.Get, "/RiskIndex/Objects", "X-Total-Count", "137");

        var page = await _service.GetObjectsAsync("org", null, "category=2", "-r", 2, 100);

        Assert.Equal(137, page.Total);
        Assert.Equal("hosts", Assert.Single(page.Rows).Object.RequiresPermission);
        Assert.Contains("page=2", _backend.LastRequest.Query);
        Assert.Contains("pageSize=100", _backend.LastRequest.Query);
        Assert.Contains("sorts=-r", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheObjectsWithoutATotalHeaderCountTheRows()
    {
        Answer("/RiskIndex/Objects", Array.Empty<RiskIndexObjectRowDto>());

        var page = await _service.GetObjectsAsync();

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Rows);
    }

    [Fact]
    public async Task TestAnObjectIsReadByKindAndEscapedReference()
    {
        Answer("/RiskIndex/Objects/5/V1%3Aasset%2F9", new RiskIndexObjectDetailDto(
            new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.VendorAsset, "V1:asset/9"), "srv-9", null),
            "cloud", new DateOnly(2026, 10, 5), new RiskIndexCriticalityDto(3, null, 0, 3, "entity", null, null, false), true,
            [], null, null, [], [], []));

        var detail = await _service.GetObjectAsync(RiskIndexObjectKind.VendorAsset, "V1:asset/9", "org");

        Assert.Equal("srv-9", detail.Object.Label);
        Assert.Equal("/RiskIndex/Objects/5/V1%3Aasset%2F9", _backend.LastRequest.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetObjectAsync(RiskIndexObjectKind.Host, " "));
    }

    /// <summary>T253: the summary's category-keyed months survive the API's JSON, null staying "no value", never 0.</summary>
    [Fact]
    public async Task TestTheMonthlySummarySendsTabAndMonthsAndKeepsNullMeans()
    {
        Answer("/RiskIndex/MonthlySummary", new RiskIndexMonthlySummaryDto(RiskIndexTabGroup.Devices,
            [RiskIndexCategory.Exp, RiskIndexCategory.Ctl],
            [new RiskIndexMonthDto("2026-10", true, true,
                new Dictionary<RiskIndexCategory, double?> { [RiskIndexCategory.Exp] = 74.4, [RiskIndexCategory.Ctl] = null })],
            RiskIndexProvenance.OfficialDerived));

        var summary = await _service.GetMonthlySummaryAsync("e:7", RiskIndexTabGroup.Devices, 12, new DateOnly(2026, 10, 5));

        Assert.Equal([RiskIndexCategory.Exp, RiskIndexCategory.Ctl], summary.SeriesOrder);
        var month = Assert.Single(summary.Months);
        Assert.Equal(("2026-10", true, true), (month.YearMonth, month.IsPartial, month.HasProfileChange));
        Assert.Equal(74.4, month.MeanByCategory[RiskIndexCategory.Exp]);
        Assert.Null(month.MeanByCategory[RiskIndexCategory.Ctl]);
        Assert.Equal(RiskIndexProvenance.OfficialDerived, summary.Provenance);
        Assert.Contains("tab=1", _backend.LastRequest.Query);
        Assert.Contains("months=12", _backend.LastRequest.Query);
        Assert.Contains("date=2026-10-05", _backend.LastRequest.Query);

        await _service.GetMonthlySummaryAsync();
        Assert.DoesNotContain("tab=", _backend.LastRequest.Query);
        Assert.Contains("months=6", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheTabReadsKeepTheBudgetsRefusal()
    {
        Answer("/RiskIndex/MonthlySummary", new { error = "derived_computation_timeout", message = "Too slow." },
            HttpStatusCode.ServiceUnavailable);
        Answer("/RiskIndex/RiskFactors", new { error = "invalid_tab", message = "Tab." }, HttpStatusCode.BadRequest);

        var timeout = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetMonthlySummaryAsync("e:7;k:5"));
        var tab = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRiskFactorsAsync());

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "derived_computation_timeout"), (timeout.StatusCode, timeout.ErrorCode));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_tab"), (tab.StatusCode, tab.ErrorCode));
    }

    /// <summary>T254: the values present in a node, read with its node and date, and left out of the query when not given.</summary>
    [Fact]
    public async Task TestTheDimensionsAreReadWithTheirNodeAndDate()
    {
        Answer("/RiskIndex/Dimensions", new RiskIndexDimensionsDto(
            [new RiskIndexDimensionValueDto("c:devices", "RiskTab_Devices", 12, "present")],
            [new RiskIndexDimensionValueDto("s:V1-DEV", "RiskSource_V1-DEV", 7, "present")], [],
            [new RiskIndexDimensionValueDto("a:none", "RiskEnvironment_none", 2, "present")]));

        var dimensions = await _service.GetDimensionsAsync("e:7;c:devices", new DateOnly(2026, 10, 5));

        Assert.Equal(("c:devices", 12), (dimensions.AssetClasses[0].Term, dimensions.AssetClasses[0].VisibleObjects));
        Assert.Equal("s:V1-DEV", Assert.Single(dimensions.Sources).Term);
        Assert.Empty(dimensions.Criticalities);
        Assert.Equal("RiskEnvironment_none", Assert.Single(dimensions.Environments).LabelKey);
        Assert.Contains("node=e:7;c:devices", Uri.UnescapeDataString(_backend.LastRequest.Query));
        Assert.Contains("date=2026-10-05", _backend.LastRequest.Query);

        await _service.GetDimensionsAsync();
        Assert.DoesNotContain("node=", _backend.LastRequest.Query);
        Assert.DoesNotContain("date=", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheDimensionsKeepTheBudgetsRefusal()
    {
        Answer("/RiskIndex/Dimensions", new { error = "derived_computation_busy", message = "Busy." },
            HttpStatusCode.ServiceUnavailable);

        var busy = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetDimensionsAsync("e:7"));

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "derived_computation_busy"), (busy.StatusCode, busy.ErrorCode));
    }

    [Fact]
    public async Task TestTheRiskFactorsAreReadWithTheirTab()
    {
        Answer("/RiskIndex/RiskFactors", new[] { new RiskIndexRiskFactorDto("EXP.V.exploited", "RiskFactor_EXP_V_exploited", 31, 9,
            "factor=EXP.V.exploited") });

        var line = Assert.Single(await _service.GetRiskFactorsAsync("org", RiskIndexTabGroup.InternetFacing));

        Assert.Equal(("EXP.V.exploited", 31, 9, "factor=EXP.V.exploited"), (line.FactorKey, line.Count, line.Points, line.ObjectFilter));
        Assert.Contains("tab=2", _backend.LastRequest.Query);
        Assert.Contains("node=org", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestTheInventoryChangesContributionsAndIndicatorsSendTheirParameters()
    {
        Answer("/RiskIndex/Inventory", new RiskIndexInventoryDto(RiskIndexTabGroup.Clouds, 10, "vendor", [],
            new RiskIndexStateCountsDto(5, 2, 2, 1), new Dictionary<string, int> { ["b0"] = 5 }));
        Answer("/RiskIndex/Changes", new RiskIndexChangesDto(new RiskIndexNodeRef("org", "Organization", "organization"),
            new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5), 1.2, 1, [], [], 0.01, []));
        Answer("/RiskIndex/Contributions", new RiskIndexContributionsDto(new RiskIndexNodeRef("org", "Organization", "organization"),
            0.4, []));
        Answer("/RiskIndex/GovernanceIndicators", new RiskIndexGovernanceIndicatorsDto(3, 30, 1, 0.82, 9, 1, 0.71,
            "requires_m43", "requires_m45"));

        Assert.Equal(5, (await _service.GetInventoryAsync("org", RiskIndexTabGroup.Clouds)).RDistributionByBand["b0"]);
        Assert.Contains("tab=5", _backend.LastRequest.Query);

        Assert.Equal(1, (await _service.GetChangesAsync("org", new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5))).DeltaDisplayed);
        Assert.Contains("from=2026-10-04", _backend.LastRequest.Query);
        Assert.Contains("to=2026-10-05", _backend.LastRequest.Query);

        Assert.Equal(0.4, (await _service.GetContributionsAsync("org", null, RiskIndexCategory.Gov)).EffectiveRegisterWeight);
        Assert.Contains("category=5", _backend.LastRequest.Query);

        Assert.Equal(3, (await _service.GetGovernanceIndicatorsAsync("org")).AcceptancesExpiringSoon);
        Assert.Equal("GET /RiskIndex/GovernanceIndicators", _backend.LastRequest.ToString().Split('?')[0]);
    }

    /// <summary>T256 (S40 §5.12): the quality read sends its node and date and keeps an unmeasured count as null.</summary>
    [Fact]
    public async Task TestTheQualityIsReadWithItsNodeAndDateAndKeepsNullCounts()
    {
        Answer("/RiskIndex/Quality", new RiskIndexQualityDto(0.92, 0.99, 0.91, "high", 0.12, true, 0.34,
            [new RiskIndexStaleSourceDto("V1-DEV", new DateTime(2026, 9, 27, 3, 0, 0, DateTimeKind.Utc))], 7, null, null, null, null,
            null, 5, "requires_m40"));

        var quality = await _service.GetQualityAsync("e:7", new DateOnly(2026, 10, 5));

        Assert.Equal("GET /RiskIndex/Quality", _backend.LastRequest.ToString().Split('?')[0]);
        Assert.Contains("node=e:7", Uri.UnescapeDataString(_backend.LastRequest.Query));
        Assert.Contains("date=2026-10-05", _backend.LastRequest.Query);
        Assert.Equal((0.12, true, 0.34), (quality.UnassignedWeightFraction, quality.UnassignedWarning,
            quality.DefaultCriticalityWeightFraction));
        Assert.Equal(new DateTime(2026, 9, 27, 3, 0, 0, DateTimeKind.Utc), Assert.Single(quality.StaleSources).LastReadingUtc);
        Assert.Null(quality.DivergentLinks);
        Assert.Null(quality.SscPseudoHosts);
        Assert.Equal(5, quality.ResidualAboveInherent);
    }

    [Fact]
    public async Task TestAQualityRefusalKeepsItsCode()
    {
        Answer("/RiskIndex/Quality", new { error = "derived_computation_busy", message = "Busy." }, HttpStatusCode.ServiceUnavailable);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetQualityAsync("e:7"));

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "derived_computation_busy"), (refusal.StatusCode, refusal.ErrorCode));
    }

    [Fact]
    public async Task TestThePublicationAndTheProfileReadsUseTheirRoutes()
    {
        Answer("/RiskIndex/Publication", new RiskIndexPublicationDto(RiskIndexPublicationState.Published,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "bruno"));
        Answer("/RiskIndex/Profiles", new[] { Profile(3), Profile(2, RiskIndexProfileStatus.Retired) });
        Answer("/RiskIndex/Profiles/3", new RiskIndexProfileDto(3, 3, "Equilibrado", "balanced", RiskIndexMode.Interim,
            RiskIndexProfileStatus.Active, new RiskIndexParameters { ArrowDelta = 4 }, "why", "ATA-2026-09", null, false, null,
            "ana", DateTime.UtcNow, DateTime.UtcNow, "bruno", DateTime.UtcNow, "role", DateTime.UtcNow, null, null, null, null, 2,
            true));
        Answer("/RiskIndex/Profiles/Diff", new RiskIndexProfileDiffDto(2, 3,
            [new RiskIndexParameterChangeDto("display", "delta_seta", "3", "4", true)]));

        Assert.Equal(RiskIndexPublicationState.Published, (await _service.GetPublicationAsync()).State);
        Assert.Equal([3, 2], (await _service.GetProfilesAsync()).Select(p => p.Id));

        var profile = await _service.GetProfileAsync(3);
        Assert.Equal(4, profile.Parameters.ArrowDelta);
        Assert.Equal(3, profile.Parameters.Bands.Count);

        var diff = await _service.GetProfileDiffAsync(2, 3);
        Assert.True(Assert.Single(diff.Changes).Changed);
        Assert.Contains("a=2", _backend.LastRequest.Query);
        Assert.Contains("b=3", _backend.LastRequest.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "risk_index_in_shadow")]
    [InlineData(HttpStatusCode.Forbidden, "org_requires_global_scope")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_node")]
    [InlineData(HttpStatusCode.NotFound, "no_snapshot")]
    [InlineData(HttpStatusCode.Conflict, "trend_unavailable_partial")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "derived_computation_busy")]
    public async Task TestARefusalKeepsItsStatusCodeAndSentence(HttpStatusCode status, string code)
    {
        Answer("/RiskIndex/Overview", new { error = code, message = "Readable sentence from the server." }, status);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetOverviewAsync());

        Assert.Equal(status, refusal.StatusCode);
        Assert.Equal(code, refusal.ErrorCode);
        Assert.Equal("Readable sentence from the server.", refusal.Message);
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestAServiceUnavailableIsAnsweredOnceNotRetried()
    {
        Answer("/RiskIndex/Overview", new { error = "derived_computation_busy", message = "Busy." },
            HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetOverviewAsync("e:7"));

        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestUnauthorizedDiscardsTheToken()
    {
        _backend.OnStatus(Method.Get, "/RiskIndex/Overview", HttpStatusCode.Unauthorized);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetOverviewAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, refusal.StatusCode);
        Assert.Null(refusal.ErrorCode);
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestANotFoundWithoutACodeIsANotFound()
    {
        _backend.OnStatus(Method.Get, "/RiskIndex/Profiles/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetProfileAsync(99));
    }

    [Fact]
    public async Task TestARefusalThatIsNotJsonKeepsAGenericSentenceAndNoCode()
    {
        _backend.OnContent(Method.Get, "/RiskIndex/Publication", "<html>proxy</html>", "text/html", HttpStatusCode.Forbidden);

        var refusal = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetPublicationAsync());

        Assert.Null(refusal.ErrorCode);
        Assert.Equal("Error calling /RiskIndex/Publication", refusal.Message);
    }

    [Fact]
    public async Task TestATransportFailureIsACommunicationError()
    {
        _backend.OnTransportFailure(Method.Get, "/RiskIndex/Snapshots/Latest", new HttpRequestException("down"));

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetLatestAsync());
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsRefusedRatherThanReadAsNull()
    {
        _backend.On(Method.Get, "/RiskIndex/Overview", "", HttpStatusCode.OK);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetOverviewAsync());
    }
}
