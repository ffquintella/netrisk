using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO.Statistics;
using Model.Exceptions;
using Model.Statistics;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives every public member of <see cref="StatisticsRestService"/> over a programmable HTTP
/// backend, so the URLs, the query parameters and the three answer shapes the service distinguishes
/// (a body, an empty <c>404</c>, a failing status) all run for real.
///
/// The <see cref="IAuthenticationService"/> is a substitute on purpose: the concrete
/// <c>AuthenticationRestService</c> discards its token through <c>MutableConfigurationService</c>,
/// which writes a LiteDB file under the application data folder — a unit test must not touch it.
/// </summary>
[TestSubject(typeof(StatisticsRestService))]
public class StatisticsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IStatisticsService _service;

    public StatisticsRestServiceTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IStatisticsService>();
    }

    private static List<RisksOnDay> TwoDays() =>
    [
        new()
        {
            Day = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            RisksCreated = 2, TotalRisks = 10, TotalRiskValue = 25.5f
        },
        new()
        {
            Day = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
            RisksCreated = 3, TotalRisks = 13, TotalRiskValue = 30.5f
        }
    ];

    // ---------------------------------------------------------------- GetRisksOverTime (sync)

    [Fact]
    public void TestGetRisksOverTimeReturnsTheSeriesAndAsksForNinetyDays()
    {
        _backend.OnGet("/Statistics/RisksOverTime", TwoDays());

        var series = _service.GetRisksOverTime();

        Assert.Equal(2, series.Count);
        Assert.Equal(10, series[0].TotalRisks);
        Assert.Equal(30.5f, series[1].TotalRiskValue);
        Assert.Equal("GET /Statistics/RisksOverTime?daysSpan=90", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRisksOverTimeAnswersAnEmptyListWhenTheServerHasNothing()
    {
        // An empty 404 makes RestSharp's typed helper hand back null; this method logs and degrades
        // to an empty series rather than raising.
        _backend.OnStatus(Method.Get, "/Statistics/RisksOverTime", HttpStatusCode.NotFound);

        Assert.Empty(_service.GetRisksOverTime());
    }

    [Fact]
    public void TestGetRisksOverTimeWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksOverTime", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksOverTime());
    }

    // ---------------------------------------------------------------- GetRisksOverTimeAsync

    [Fact]
    public async Task TestGetRisksOverTimeAsyncReturnsTheSeries()
    {
        _backend.OnGet("/Statistics/RisksOverTime", TwoDays());

        var series = await _service.GetRisksOverTimeAsync();

        Assert.Equal(2, series.Count);
        Assert.Equal(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), series[0].Day);
        Assert.Equal(3, series[1].RisksCreated);
        Assert.Equal("?daysSpan=90", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetRisksOverTimeAsyncAnswersAnEmptyListWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksOverTime", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetRisksOverTimeAsync());
    }

    [Fact]
    public async Task TestGetRisksOverTimeAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Statistics/RisksOverTime");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksOverTimeAsync());
    }

    // ---------------------------------------------------------------- GetVulnerabilityNumbersByTimeAsync

    private static VulnerabilityNumbersByTime NumbersByTime() => new()
    {
        Open = new Dictionary<string, VulnerabilityNumbers>
        {
            ["2026-01"] = new() { Critical = 1, High = 2, Medium = 3, Low = 4, Insignificant = 5, Total = 15 }
        },
        Closed = new Dictionary<string, VulnerabilityNumbers>
        {
            ["2026-01"] = new() { Critical = 0, High = 1, Total = 1 }
        }
    };

    [Fact]
    public async Task TestGetVulnerabilityNumbersByTimeAsyncUsesThirtyDaysByDefault()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/NumbersByTime", NumbersByTime());

        var numbers = await _service.GetVulnerabilityNumbersByTimeAsync();

        Assert.Equal(15, numbers.Open["2026-01"].Total);
        Assert.Equal(1, numbers.Closed["2026-01"].High);
        Assert.Equal("GET /Statistics/Vulnerabilities/NumbersByTime?daysSpan=30", _backend.LastRequest.ToString());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(365)]
    public async Task TestGetVulnerabilityNumbersByTimeAsyncForwardsTheRequestedSpan(int daysSpan)
    {
        _backend.OnGet("/Statistics/Vulnerabilities/NumbersByTime", NumbersByTime());

        await _service.GetVulnerabilityNumbersByTimeAsync(daysSpan);

        Assert.Equal($"?daysSpan={daysSpan}", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetVulnerabilityNumbersByTimeAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/NumbersByTime", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilityNumbersByTimeAsync());
    }

    [Fact]
    public async Task TestGetVulnerabilityNumbersByTimeAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/NumbersByTime",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilityNumbersByTimeAsync(15));
    }

    // ---------------------------------------------------------------- GetVulnerabilitiesServerityByImportAsync

    [Fact]
    public async Task TestGetVulnerabilitiesServerityByImportAsyncReturnsTheImports()
    {
        _backend.OnGet("/Statistics/VulnerabilitiesSeverityByImport", new List<ImportSeverity>
        {
            new()
            {
                ImportDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CriticalityLevel = 4, ItemCount = 12, TotalRiskValue = 48
            }
        });

        var imports = await _service.GetVulnerabilitiesServerityByImportAsync();

        var import = Assert.Single(imports);
        Assert.Equal(12, import.ItemCount);
        Assert.Equal(4d, import.CriticalityLevel);
        Assert.Equal(48d, import.TotalRiskValue);
        Assert.Equal("GET /Statistics/VulnerabilitiesSeverityByImport?itemCount=120",
            _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesServerityByImportAsyncAnswersAnEmptyListWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/VulnerabilitiesSeverityByImport", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetVulnerabilitiesServerityByImportAsync());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesServerityByImportAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/VulnerabilitiesSeverityByImport",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesServerityByImportAsync());
    }

    // ---------------------------------------------------------------- GetVulnerabilitiesDistribution

    private static List<ValueName> Distribution() =>
    [
        new() { Name = "Critical", Value = 3 },
        new() { Name = "High", Value = 7 }
    ];

    [Fact]
    public async Task TestGetVulnerabilitiesDistributionAsyncReturnsTheDistribution()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/Distribution", Distribution());

        var distribution = await _service.GetVulnerabilitiesDistributionAsync();

        Assert.Equal(2, distribution.Count);
        Assert.Equal("Critical", distribution[0].Name);
        Assert.Equal(7f, distribution[1].Value);
        Assert.Equal("GET /Statistics/Vulnerabilities/Distribution", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesDistributionAsyncAnswersAnEmptyListWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Distribution", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetVulnerabilitiesDistributionAsync());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesDistributionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Distribution",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesDistributionAsync());
    }

    [Fact]
    public void TestGetVulnerabilitiesDistributionRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/Distribution", Distribution());

#pragma warning disable CS0618 // the synchronous overload is obsolete but still shipped
        var distribution = _service.GetVulnerabilitiesDistribution();
#pragma warning restore CS0618

        Assert.Equal(2, distribution.Count);
        Assert.Equal("High", distribution[1].Name);
        Assert.True(_backend.Sent(Method.Get, "/Statistics/Vulnerabilities/Distribution"));
    }

    // ---------------------------------------------------------------- GetVulnerabilityImportSources

    [Fact]
    public void TestGetVulnerabilityImportSourcesReturnsTheSources()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/Sources", new List<ValueName>
        {
            new() { Name = "Nessus", Value = 42 }
        });

        var sources = _service.GetVulnerabilityImportSources();

        var source = Assert.Single(sources);
        Assert.Equal("Nessus", source.Name);
        Assert.Equal(42f, source.Value);
        Assert.Equal("GET /Statistics/Vulnerabilities/Sources", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetVulnerabilityImportSourcesAnswersAnEmptyListWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Sources", HttpStatusCode.NotFound);

        Assert.Empty(_service.GetVulnerabilityImportSources());
    }

    [Fact]
    public void TestGetVulnerabilityImportSourcesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Sources", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetVulnerabilityImportSources());
    }

    // ---------------------------------------------------------------- GetVulnerabilitiesVerifiedPercentageAsync

    [Fact]
    public async Task TestGetVulnerabilitiesVerifiedPercentageAsyncReturnsThePercentage()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/VerifiedPercentage", "12.5");

        var percentage = await _service.GetVulnerabilitiesVerifiedPercentageAsync();

        Assert.Equal(12.5f, percentage);
        Assert.Equal("GET /Statistics/Vulnerabilities/VerifiedPercentage", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesVerifiedPercentageAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/VerifiedPercentage",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesVerifiedPercentageAsync());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesVerifiedPercentageAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Statistics/Vulnerabilities/VerifiedPercentage");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesVerifiedPercentageAsync());
    }

    // ---------------------------------------------------------------- GetRisksNumbersAsync

    [Fact]
    public async Task TestGetRisksNumbersAsyncReturnsTheGeneralAndStatusBreakdown()
    {
        var numbers = new RisksNumbers();
        numbers.General.Total = 20;
        numbers.General.High = 5;
        numbers.General.Medium = 9;
        numbers.General.Low = 6;
        numbers.ByStatus.Statuses["Open"] = 12;
        numbers.ByStatus.Statuses["Closed"] = 8;

        _backend.OnGet("/Statistics/Risks/Numbers", numbers);

        var result = await _service.GetRisksNumbersAsync();

        Assert.Equal(20, result.General.Total);
        Assert.Equal(5, result.General.High);
        Assert.Equal(12, result.ByStatus.Statuses["Open"]);
        Assert.Equal("GET /Statistics/Risks/Numbers", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRisksNumbersAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/Numbers", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksNumbersAsync());
    }

    [Fact]
    public async Task TestGetRisksNumbersAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/Numbers", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksNumbersAsync());
    }

    // ---------------------------------------------------------------- GetRisksTopGroupsAsync

    [Fact]
    public async Task TestGetRisksTopGroupsAsyncReturnsTheGroups()
    {
        _backend.OnGet("/Statistics/Risks/TopGroups", new List<RiskGroup>
        {
            new() { Name = "Infrastructure", Score = 8.5f, ItemCount = 4 },
            new() { Name = "People", Score = 3.25f, ItemCount = 2 }
        });

        var groups = await _service.GetRisksTopGroupsAsync();

        Assert.Equal(2, groups.Count);
        Assert.Equal("Infrastructure", groups[0].Name);
        Assert.Equal(8.5f, groups[0].Score);
        Assert.Equal(2, groups[1].ItemCount);
        Assert.Equal("GET /Statistics/Risks/TopGroups", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRisksTopGroupsAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/TopGroups", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksTopGroupsAsync());
    }

    [Fact]
    public async Task TestGetRisksTopGroupsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/TopGroups", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksTopGroupsAsync());
    }

    // ---------------------------------------------------------------- GetRisksTopEntitiesAsync

    private static List<RiskEntity> TopEntities() =>
    [
        new() { EntityId = 3, EntityName = "HQ", EntityType = "organization", TotalCalculatedRisk = 12.5f }
    ];

    [Fact]
    public async Task TestGetRisksTopEntitiesAsyncSendsOnlyTheCountByDefault()
    {
        _backend.OnGet("/Statistics/Risks/TopEntities", TopEntities());

        var entities = await _service.GetRisksTopEntitiesAsync();

        var entity = Assert.Single(entities);
        Assert.Equal(3, entity.EntityId);
        Assert.Equal("HQ", entity.EntityName);
        Assert.Equal(12.5f, entity.TotalCalculatedRisk);
        Assert.Equal("GET /Statistics/Risks/TopEntities?count=10", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRisksTopEntitiesAsyncAddsTheEntityTypeWhenGiven()
    {
        _backend.OnGet("/Statistics/Risks/TopEntities", TopEntities());

        await _service.GetRisksTopEntitiesAsync(5, "organization");

        Assert.Equal("?count=5&entityType=organization", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetRisksTopEntitiesAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/TopEntities", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRisksTopEntitiesAsync());
    }

    [Fact]
    public async Task TestGetRisksTopEntitiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Risks/TopEntities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetRisksTopEntitiesAsync(3, "site"));
    }

    // ---------------------------------------------------------------- GetVulnerabilityNumbers

    [Fact]
    public void TestGetVulnerabilityNumbersReturnsTheCounts()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/Numbers", new VulnerabilityNumbers
        {
            Critical = 1, High = 2, Medium = 3, Low = 4, Insignificant = 5, Total = 15
        });

        var numbers = _service.GetVulnerabilityNumbers();

        Assert.Equal(1, numbers.Critical);
        Assert.Equal(5, numbers.Insignificant);
        Assert.Equal(15, numbers.Total);
        Assert.Equal("GET /Statistics/Vulnerabilities/Numbers", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetVulnerabilityNumbersThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Numbers", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetVulnerabilityNumbers());
    }

    [Fact]
    public void TestGetVulnerabilityNumbersWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/Numbers", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetVulnerabilityNumbers());
    }

    // ---------------------------------------------------------------- GetVulnerabilitiesNumbersByStatusAsync

    [Fact]
    public async Task TestGetVulnerabilitiesNumbersByStatusAsyncReturnsTheCountsKeyedByStatus()
    {
        _backend.OnGet("/Statistics/Vulnerabilities/NumbersByStatus", new VulnerabilityNumbersByStatus
        {
            NumbersByStatus = new Dictionary<int, VulnerabilityNumbers>
            {
                [1] = new() { Critical = 2, Total = 6 },
                [4] = new() { Critical = 0, Total = 1 }
            }
        });

        var numbers = await _service.GetVulnerabilitiesNumbersByStatusAsync();

        Assert.Equal(2, numbers.NumbersByStatus.Count);
        Assert.Equal(6, numbers.NumbersByStatus[1].Total);
        Assert.Equal(1, numbers.NumbersByStatus[4].Total);
        Assert.Equal("GET /Statistics/Vulnerabilities/NumbersByStatus", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesNumbersByStatusAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/NumbersByStatus", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesNumbersByStatusAsync());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesNumbersByStatusAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/Vulnerabilities/NumbersByStatus",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetVulnerabilitiesNumbersByStatusAsync());
    }

    // ---------------------------------------------------------------- GetSecurityControlStatistics

    private static SecurityControlsStatistics SecurityControls() => new()
    {
        SecurityControls =
        [
            new()
            {
                TotalRisk = 9.5, Framework = "ISO27001", FrameworkId = 1, ControlId = 7,
                ReferemceName = "A.5.1", ControlName = "Policies", ControlNumber = "5.1",
                MaturityId = 2, DesireedMaturityId = 4, Status = 1, Deleted = false
            }
        ],
        FameworkStats =
        [
            new() { Framework = "ISO27001", Count = 1, TotalMaturity = 2, TotalDesiredMaturity = 4 }
        ]
    };

    [Fact]
    public void TestGetSecurityControlStatisticsReturnsTheControlsAndFrameworkStats()
    {
        _backend.OnGet("/Statistics/SecurityControls", SecurityControls());

        var statistics = _service.GetSecurityControlStatistics();

        var control = Assert.Single(statistics.SecurityControls);
        Assert.Equal("ISO27001", control.Framework);
        Assert.Equal(9.5, control.TotalRisk);
        Assert.Equal("A.5.1", control.ReferemceName);
        Assert.NotNull(statistics.FameworkStats);
        Assert.Equal(4, statistics.FameworkStats![0].TotalDesiredMaturity);
        Assert.Equal("GET /Statistics/SecurityControls", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetSecurityControlStatisticsAnswersAnEmptySetWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/SecurityControls", HttpStatusCode.NotFound);

        var statistics = _service.GetSecurityControlStatistics();

        Assert.Empty(statistics.SecurityControls);
        Assert.Null(statistics.FameworkStats);
    }

    [Fact]
    public void TestGetSecurityControlStatisticsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/SecurityControls", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetSecurityControlStatistics());
    }

    [Fact]
    public async Task TestGetSecurityControlStatisticsAsyncReturnsTheControls()
    {
        _backend.OnGet("/Statistics/SecurityControls", SecurityControls());

        var statistics = await _service.GetSecurityControlStatisticsAsync();

        var control = Assert.Single(statistics.SecurityControls);
        Assert.Equal(7, control.ControlId);
        Assert.Equal("Policies", control.ControlName);
        Assert.True(_backend.Sent(Method.Get, "/Statistics/SecurityControls"));
    }

    [Fact]
    public async Task TestGetSecurityControlStatisticsAsyncAnswersAnEmptySetWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/SecurityControls", HttpStatusCode.NotFound);

        Assert.Empty((await _service.GetSecurityControlStatisticsAsync()).SecurityControls);
    }

    [Fact]
    public async Task TestGetSecurityControlStatisticsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Statistics/SecurityControls");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetSecurityControlStatisticsAsync());
    }

    // ---------------------------------------------------------------- GetRisksVsCosts

    // Raw JSON rather than a serialized LabeledPoints: the type derives from LiveCharts'
    // ObservablePoint, and only the three members the service is expected to carry are exercised.
    private const string TwoLabeledPoints =
        """[{"Label":"R-1","X":1.5,"Y":200.0},{"Label":"R-2","X":2.5,"Y":300.0}]""";

    [Fact]
    public void TestGetRisksVsCostsReturnsTheLabeledPoints()
    {
        _backend.OnGet("/Statistics/RisksVsCosts", TwoLabeledPoints);

        var points = _service.GetRisksVsCosts(1.0, 5.0);

        Assert.Equal(2, points.Count);
        Assert.Equal("R-1", points[0].Label);
        Assert.Equal(1.5, (double)points[0].X!);
        Assert.Equal(300.0, (double)points[1].Y!);
    }

    [Fact]
    public void TestGetRisksVsCostsSendsMinRiskTwiceAndNeverSendsMaxRisk()
    {
        // Known defect in StatisticsRestService.GetRisksVsCosts: it adds the "minRisk" parameter
        // twice and never sends "maxRisk", so the server can only ever apply the lower bound.
        // Asserted as-is rather than fixed.
        _backend.OnGet("/Statistics/RisksVsCosts", TwoLabeledPoints);

        _service.GetRisksVsCosts(1.5, 9.5);

        var query = _backend.LastRequest.Query;

        // The number formatting itself is left out of the assertion — only the parameter names
        // matter here, and they are what the defect is about.
        Assert.Equal(2, query.Split("minRisk=").Length - 1);
        Assert.DoesNotContain("maxRisk", query);
    }

    [Fact]
    public void TestGetRisksVsCostsThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksVsCosts", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksVsCosts(0, 10));
    }

    [Fact]
    public void TestGetRisksVsCostsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksVsCosts", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksVsCosts(0, 10));
    }

    // ---------------------------------------------------------------- GetRisksImpactVsProbability

    [Fact]
    public void TestGetRisksImpactVsProbabilityReturnsTheLabeledPoints()
    {
        _backend.OnGet("/Statistics/RisksImpactVsProbability", TwoLabeledPoints);

        var points = _service.GetRisksImpactVsProbability(1.0, 5.0);

        Assert.Equal(2, points.Count);
        Assert.Equal("R-2", points[1].Label);
        Assert.Equal(2.5, (double)points[1].X!);
        Assert.Equal(2, _backend.LastRequest.Query.Split("minRisk=").Length - 1);
        Assert.DoesNotContain("maxRisk", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetRisksImpactVsProbabilityThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksImpactVsProbability", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksImpactVsProbability(0, 10));
    }

    [Fact]
    public void TestGetRisksImpactVsProbabilityWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/RisksImpactVsProbability",
            HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRisksImpactVsProbability(0, 10));
    }

    // ---------------------------------------------------------------- GetEntitiesRiskValues

    private static List<ValueNameType> EntityRiskValues() =>
    [
        new() { Name = "HQ", Value = 12.5f, Type = "organization" },
        new() { Name = "Branch", Value = 3f, Type = "site" }
    ];

    [Fact]
    public void TestGetEntitiesRiskValuesSendsOnlyTheTopCountWhenThereIsNoParent()
    {
        _backend.OnGet("/Statistics/EntitiesRiskValues", EntityRiskValues());

        var values = _service.GetEntitiesRiskValues();

        Assert.Equal(2, values.Count);
        Assert.Equal("HQ", values[0].Name);
        Assert.Equal("organization", values[0].Type);
        Assert.Equal(3f, values[1].Value);
        Assert.Equal("GET /Statistics/EntitiesRiskValues?topCount=10", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetEntitiesRiskValuesAddsTheParentWhenGiven()
    {
        _backend.OnGet("/Statistics/EntitiesRiskValues", EntityRiskValues());

        _service.GetEntitiesRiskValues(7, 3);

        Assert.Equal("?topCount=3&parentId=7", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetEntitiesRiskValuesThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Statistics/EntitiesRiskValues", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetEntitiesRiskValues());
    }

    [Fact]
    public void TestGetEntitiesRiskValuesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Statistics/EntitiesRiskValues", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetEntitiesRiskValues(2));
    }
}
