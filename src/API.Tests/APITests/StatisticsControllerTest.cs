using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO.Statistics;
using Model.Statistics;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The statistics endpoints are thin wrappers over <see cref="IStatisticsService"/>, except for
/// <c>SecurityControls</c> which queries the database directly. Two controllers are built: one over
/// a service that answers, and one over a service that throws, so both sides of every try/catch
/// are exercised.
/// </summary>
[TestSubject(typeof(StatisticsController))]
public class StatisticsControllerTest : BaseControllerTest
{
    private const string Boom = "boom";

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IStatisticsService _statistics = Substitute.For<IStatisticsService>();
    private readonly IStatisticsService _failingStatistics = Substitute.For<IStatisticsService>();

    private readonly StatisticsController _controller;
    private readonly StatisticsController _failingController;

    private readonly List<ValueName> _distribution = [new ValueName { Name = "Critical", Value = 3 }];
    private readonly List<ValueName> _sources = [new ValueName { Name = "Nessus", Value = 7 }];
    private readonly VulnerabilityNumbers _vulnerabilityNumbers = new() { Critical = 1, High = 2, Total = 3 };
    private readonly VulnerabilityNumbersByStatus _numbersByStatus = new();
    private readonly VulnerabilityNumbersByTime _numbersByTime = new();
    private readonly List<LabeledPoints> _risksVsCosts = [new LabeledPoints { Label = "R1" }];
    private readonly List<LabeledPoints> _impactVsProbability = [new LabeledPoints { Label = "R2" }];
    private readonly List<ValueNameType> _entityRiskValues = [new ValueNameType { Name = "HQ", Value = 4, Type = "organization" }];
    private readonly List<RisksOnDay> _risksOverTime = [new RisksOnDay { RisksCreated = 2, TotalRisks = 5 }];
    private readonly List<ImportSeverity> _severityByImport = [new ImportSeverity { ItemCount = 6 }];
    private readonly RisksNumbers _risksNumbers = new();
    private readonly List<TopRisk> _topRisks = [new TopRisk { Name = "Top", Score = 9 }];
    private readonly List<RiskGroup> _riskGroups = [new RiskGroup { Name = "Group", ItemCount = 2 }];
    private readonly List<RiskEntity> _riskEntities = [new RiskEntity { EntityId = 1, EntityName = "HQ" }];

    public StatisticsControllerTest()
    {
        SeedSecurityControls();

        _statistics.GetVulnerabilitiesDistribution().Returns(_distribution);
        _statistics.GetVulnerabilitiesVerifiedPercentage().Returns(42.5f);
        _statistics.GetVulnerabilityNumbers().Returns(_vulnerabilityNumbers);
        _statistics.GetVulnerabilitiesNumbersByStatus().Returns(_numbersByStatus);
        _statistics.GetVulnerabilitiesNumbersByTimeAsync(Arg.Any<int>()).Returns(Task.FromResult(_numbersByTime));
        _statistics.GetVulnerabilitySources().Returns(_sources);
        _statistics.GetRisksVsCosts(Arg.Any<double>(), Arg.Any<double>()).Returns(_risksVsCosts);
        _statistics.GetRisksImpactVsProbability(Arg.Any<double>(), Arg.Any<double>()).Returns(_impactVsProbability);
        _statistics.GetEntitiesRiskValues(Arg.Any<int?>(), Arg.Any<int>()).Returns(_entityRiskValues);
        _statistics.GetRisksOverTime(Arg.Any<int>()).Returns(_risksOverTime);
        _statistics.GetVulnerabilitiesServerityByImportAsync(Arg.Any<int>()).Returns(Task.FromResult(_severityByImport));
        _statistics.GetRisksNumbersAsync().Returns(Task.FromResult(_risksNumbers));
        _statistics.GetRisksTopAsync(Arg.Any<int>()).Returns(Task.FromResult(_topRisks));
        _statistics.GetRisksTopGroupsAsync().Returns(Task.FromResult(_riskGroups));
        _statistics.GetRisksTopEntities(Arg.Any<int>(), Arg.Any<string>()).Returns(Task.FromResult(_riskEntities));

        _failingStatistics.GetVulnerabilitiesDistribution().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilitiesVerifiedPercentage().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilityNumbers().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilitiesNumbersByStatus().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilitiesNumbersByTimeAsync(Arg.Any<int>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilitySources().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksVsCosts(Arg.Any<double>(), Arg.Any<double>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksImpactVsProbability(Arg.Any<double>(), Arg.Any<double>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetEntitiesRiskValues(Arg.Any<int?>(), Arg.Any<int>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksOverTime(Arg.Any<int>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetVulnerabilitiesServerityByImportAsync(Arg.Any<int>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksNumbersAsync().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksTopAsync(Arg.Any<int>()).Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksTopGroupsAsync().Throws(new InvalidOperationException(Boom));
        _failingStatistics.GetRisksTopEntities(Arg.Any<int>(), Arg.Any<string>()).Throws(new InvalidOperationException(Boom));

        _controller = ResolveController<StatisticsController>(s =>
        {
            s.AddSingleton<IDalService>(_dal);
            s.AddSingleton(_statistics);
        });

        _failingController = ResolveController<StatisticsController>(s =>
        {
            s.AddSingleton<IDalService>(_dal);
            s.AddSingleton(_failingStatistics);
        });
    }

    /// <summary>
    /// Two frameworks' worth of controls plus the risks that hang off their control numbers, which
    /// is what <c>GetSecurityControls</c> joins together.
    /// </summary>
    private void SeedSecurityControls()
    {
        using var ctx = _dal.GetContext();

        ctx.Frameworks.Add(new Framework
        {
            Value = 1,
            Parent = 0,
            Name = "ISO 27001",
            Description = "Information security",
            Status = 1,
            Order = 1
        });

        ctx.FrameworkControls.AddRange(
            new FrameworkControl
            {
                Id = 10,
                ShortName = "Access control",
                ControlNumber = "A.9",
                ControlClass = 2,
                ControlMaturity = 3,
                DesiredMaturity = 5,
                ControlPriority = 1,
                Status = 1,
                Deleted = false,
                MitigationPercent = 0,
                SubmissionDate = new DateTime(2024, 1, 1)
            },
            // Kept out of the per-control list (no control number) but still counted by the
            // framework roll-up.
            new FrameworkControl
            {
                Id = 11,
                ShortName = "Unnumbered",
                ControlNumber = "",
                ControlClass = 2,
                ControlMaturity = 1,
                DesiredMaturity = 2,
                Status = 1,
                Deleted = false,
                MitigationPercent = 0,
                SubmissionDate = new DateTime(2024, 1, 1)
            },
            // Dropped by the status/deleted filter.
            new FrameworkControl
            {
                Id = 12,
                ShortName = "Retired",
                ControlNumber = "A.10",
                ControlMaturity = 4,
                DesiredMaturity = 4,
                Status = 1,
                Deleted = true,
                MitigationPercent = 0,
                SubmissionDate = new DateTime(2024, 1, 1)
            });

        ctx.FrameworkControlMappings.AddRange(
            new FrameworkControlMapping { Id = 1, ControlId = 10, Framework = 1, ReferenceName = "A.9 Access" },
            new FrameworkControlMapping { Id = 2, ControlId = 11, Framework = 1, ReferenceName = "Unnumbered" },
            new FrameworkControlMapping { Id = 3, ControlId = 12, Framework = 1, ReferenceName = "A.10 Retired" });

        ctx.Risks.AddRange(
            NewRisk(1, "New", "A.9"),
            NewRisk(2, "New", "A.9"),
            // Closed risks are excluded from the totals.
            NewRisk(3, "Closed", "A.9"),
            // Belongs to no seeded control.
            NewRisk(4, "New", "Z.1"));

        ctx.RiskScorings.AddRange(
            new RiskScoring { Id = 1, ScoringMethod = 1, CalculatedRisk = 4f, ClassicLikelihood = 2f, ClassicImpact = 2f },
            new RiskScoring { Id = 2, ScoringMethod = 1, CalculatedRisk = 6f, ClassicLikelihood = 3f, ClassicImpact = 2f },
            new RiskScoring { Id = 3, ScoringMethod = 1, CalculatedRisk = 9f, ClassicLikelihood = 3f, ClassicImpact = 3f },
            new RiskScoring { Id = 4, ScoringMethod = 1, CalculatedRisk = 1f, ClassicLikelihood = 1f, ClassicImpact = 1f });

        ctx.SaveChanges();
    }

    private static Risk NewRisk(int id, string status, string controlNumber)
    {
        return new Risk
        {
            Id = id,
            Status = status,
            Subject = $"Risk {id}",
            ReferenceId = $"REF-{id}",
            ControlNumber = controlNumber,
            Assessment = "",
            Notes = "",
            RiskCatalogMapping = "",
            ThreatCatalogMapping = "",
            SubmissionDate = new DateTime(2024, 1, 1),
            LastUpdate = new DateTime(2024, 2, 1),
            TemplateGroupId = 0
        };
    }

    [Fact]
    public void TestListAvailable()
    {
        var result = _controller.ListAvailable();

        Assert.NotNull(result.Value);
        Assert.Equal(new List<string> { "RisksOverTime", "SecurityControls", "RisksVsCosts", "Vulnerabilities" },
            result.Value);
    }

    [Fact]
    public void TestListAvailableVulnerabilities()
    {
        var result = _controller.ListAvailableVulnerabilities();

        Assert.NotNull(result.Value);
        Assert.Equal(new List<string> { "Distribution", "VerifiedPercentage", "Numbers", "Sources" }, result.Value);
    }

    [Fact]
    public void TestVulnerabilitiesDistribution()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.VulnerabilitiesDistribution().Result);
        Assert.Same(_distribution, ok.Value);

        AssertInternalServerError(_failingController.VulnerabilitiesDistribution().Result);
    }

    [Fact]
    public void TestVulnerabilitiesVerifiedPercentage()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.VulnerabilitiesVerifiedPercentage().Result);
        Assert.Equal(42.5f, Assert.IsType<float>(ok.Value));

        AssertInternalServerError(_failingController.VulnerabilitiesVerifiedPercentage().Result);
    }

    [Fact]
    public void TestVulnerabilitiesNumbers()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.VulnerabilitiesNumbers().Result);
        Assert.Same(_vulnerabilityNumbers, ok.Value);

        AssertInternalServerError(_failingController.VulnerabilitiesNumbers().Result);
    }

    [Fact]
    public void TestVulnerabilitiesNumbersByStatus()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.VulnerabilitiesNumbersByStatus().Result);
        Assert.Same(_numbersByStatus, ok.Value);

        AssertInternalServerError(_failingController.VulnerabilitiesNumbersByStatus().Result);
    }

    [Fact]
    public async Task TestVulnerabilitiesNumbersByTime()
    {
        var result = await _controller.VulnerabilitiesNumbersByTime(15);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_numbersByTime, ok.Value);
        await _statistics.Received(1).GetVulnerabilitiesNumbersByTimeAsync(15);

        var failed = await _failingController.VulnerabilitiesNumbersByTime();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public void TestVulnerabilitiesSources()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.VulnerabilitiesSources().Result);
        Assert.Same(_sources, ok.Value);

        AssertInternalServerError(_failingController.VulnerabilitiesSources().Result);
    }

    [Fact]
    public void TestRisksVsCosts()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.RisksVsCosts(9, 1).Result);
        Assert.Same(_risksVsCosts, ok.Value);
        _statistics.Received(1).GetRisksVsCosts(1, 9);

        AssertInternalServerError(_failingController.RisksVsCosts().Result);
    }

    [Fact]
    public void TestRisksImpactVsProbability()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.RisksImpactVsProbability(8, 2).Result);
        Assert.Same(_impactVsProbability, ok.Value);
        _statistics.Received(1).GetRisksImpactVsProbability(2, 8);

        AssertInternalServerError(_failingController.RisksImpactVsProbability().Result);
    }

    [Fact]
    public void TestEntitiesRiskValues()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.EntitiesRiskValues(3, 5).Result);
        Assert.Same(_entityRiskValues, ok.Value);
        _statistics.Received(1).GetEntitiesRiskValues(3, 5);

        AssertInternalServerError(_failingController.EntitiesRiskValues().Result);
    }

    [Fact]
    public void TestGetRisksOverTime()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRisksOverTime(60).Result);
        Assert.Same(_risksOverTime, ok.Value);
        _statistics.Received(1).GetRisksOverTime(60);

        AssertInternalServerError(_failingController.GetRisksOverTime().Result);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesSeverityByImport()
    {
        var result = await _controller.GetVulnerabilitiesServerityByImport(45);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_severityByImport, ok.Value);
        await _statistics.Received(1).GetVulnerabilitiesServerityByImportAsync(45);

        var failed = await _failingController.GetVulnerabilitiesServerityByImport();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public async Task TestRisksNumbers()
    {
        var result = await _controller.RisksNumbers();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_risksNumbers, ok.Value);

        var failed = await _failingController.RisksNumbers();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public async Task TestRisksTop()
    {
        var result = await _controller.RisksTop(3);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_topRisks, ok.Value);
        await _statistics.Received(1).GetRisksTopAsync(3);

        var failed = await _failingController.RisksTop();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public async Task TestRisksTopGroups()
    {
        var result = await _controller.RisksTopGroups();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_riskGroups, ok.Value);

        var failed = await _failingController.RisksTopGroups();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public async Task TestRisksTopEntities()
    {
        var result = await _controller.RisksTopEntities(4, "organization");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_riskEntities, ok.Value);
        await _statistics.Received(1).GetRisksTopEntities(4, "organization");

        var failed = await _failingController.RisksTopEntities();
        AssertInternalServerError(failed.Result);
    }

    [Fact]
    public void TestGetSecurityControlsAggregatesRisksPerControl()
    {
        var result = _controller.GetSecurityControls();

        Assert.Null(result.Result);
        var statistics = result.Value;
        Assert.NotNull(statistics);

        // Only the control that carries a control number reaches the per-control list.
        var control = Assert.Single(statistics.SecurityControls);
        Assert.Equal(10, control.ControlId);
        Assert.Equal("A.9", control.ControlNumber);
        Assert.Equal("ISO 27001", control.Framework);
        Assert.Equal(1, control.FrameworkId);
        Assert.Equal("A.9 Access", control.ReferemceName);
        Assert.Equal("Access control", control.ControlName);
        Assert.Equal(2, control.ClassId);
        Assert.Equal(3, control.MaturityId);
        Assert.Equal(5, control.DesireedMaturityId);
        Assert.Equal(1, control.PiorityId);
        Assert.Equal(1, control.Status);
        Assert.False(control.Deleted);

        // 4 + 6 from the two open risks; the closed one and the unrelated one are left out.
        Assert.Equal(10d, control.TotalRisk, 3);
    }

    [Fact]
    public void TestGetSecurityControlsRollsUpMaturityPerFramework()
    {
        var result = _controller.GetSecurityControls();

        var statistics = result.Value;
        Assert.NotNull(statistics);
        Assert.NotNull(statistics.FameworkStats);

        var framework = Assert.Single(statistics.FameworkStats);
        Assert.Equal("ISO 27001", framework.Framework);

        // The deleted control is filtered out, so two controls remain.
        Assert.Equal(2, framework.Count);
        Assert.Equal(4, framework.TotalMaturity);
        Assert.Equal(7, framework.TotalDesiredMaturity);
    }

    private static void AssertInternalServerError(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.Equal(Boom, objectResult.Value);
    }
}
