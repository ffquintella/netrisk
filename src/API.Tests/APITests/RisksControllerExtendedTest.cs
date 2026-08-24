using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using Model.Risks;
using NSubstitute;
using ServerServices.Interfaces;
using Sieve.Models;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Broad branch coverage for <see cref="RisksController"/>. The three actions already covered by
/// <see cref="RisksControllerTest"/> (GetOpenVulnerabilities / GetIncidentResponsePlan /
/// AssocianteRiskToIncidentResponsePlan) are only revisited here for their error branches.
/// </summary>
[TestSubject(typeof(RisksController))]
public class RisksControllerExtendedTest : BaseControllerTest
{
    private const string StatusClosed = "Closed";

    private readonly IRisksService _risksService = Substitute.For<IRisksService>();
    private readonly IMitigationsService _mitigationsService = Substitute.For<IMitigationsService>();
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IMgmtReviewsService _mgmtReviewsService = Substitute.For<IMgmtReviewsService>();

    private readonly RisksController _controller;

    public RisksControllerExtendedTest()
    {
        ArrangeRisks();
        ArrangeMitigations();
        ArrangeFiles();
        ArrangeMgmtReviews();

        _controller = ResolveController<RisksController>(s =>
        {
            s.AddSingleton(_risksService);
            s.AddSingleton(_mitigationsService);
            s.AddSingleton(_filesService);
            s.AddSingleton(_mgmtReviewsService);
        });

        // GetFilteredVulnerabilities writes an X-Total-Count response header, which needs a real
        // HttpContext on the controller itself (GetUser() uses the injected accessor instead).
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    #region arrange

    private static Risk NewRisk(int id, string status)
    {
        return new Risk
        {
            Id = id,
            Subject = $"Risk {id}",
            Status = status,
            ReferenceId = $"REF-{id}",
            Assessment = "assessment",
            Notes = "notes",
            RiskCatalogMapping = "",
            ThreatCatalogMapping = ""
        };
    }

    private static List<Vulnerability> NewVulnerabilities()
    {
        return new List<Vulnerability>
        {
            new () { Id = 1, AnalystId = 1, Severity = "1", Score = 5 },
            new () { Id = 2, AnalystId = 1, Severity = "1", Score = 5 }
        };
    }

    private void ArrangeRisks()
    {
        // ---- listing --------------------------------------------------------------------------
        _risksService
            .GetAllAsync(Arg.Any<string>(), StatusClosed, Arg.Any<bool>(), Arg.Any<ClaimsPrincipal>())
            .Returns(new List<Risk> { NewRisk(1, StatusClosed) });

        _risksService
            .GetAllAsync(Arg.Any<string>(), null, Arg.Any<bool>(), Arg.Any<ClaimsPrincipal>())
            .Returns(new List<Risk> { NewRisk(1, StatusClosed), NewRisk(2, "New") });

        _risksService
            .GetAllAsync("unauthorized", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<ClaimsPrincipal>())
            .Returns<Task<List<Risk>>>(_ => throw new UserNotAuthorizedException("testUser", 1, "list risks"));

        _risksService.GetToReview(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(new List<Risk> { NewRisk(1, "New"), NewRisk(2, "New") });
        _risksService.GetToReview(999, Arg.Any<string>(), Arg.Any<bool>())
            .Returns<List<Risk>>(_ => throw new Exception("boom"));

        _risksService.GetUserRisks(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new List<Risk> { NewRisk(1, "New"), NewRisk(2, "New") });
        _risksService.GetUserRisks(Arg.Any<User>(), "empty", Arg.Any<string>())
            .Returns(new List<Risk>());
        _risksService.GetUserRisks(Arg.Any<User>(), "unauthorized", Arg.Any<string>())
            .Returns<List<Risk>>(_ => throw new UserNotAuthorizedException("testUser", 1, "list own risks"));

        _risksService.GetRisksNeedingReview(Arg.Any<string>())
            .Returns(new List<Risk> { NewRisk(3, "New") });
        _risksService.GetRisksNeedingReview("unauthorized")
            .Returns<List<Risk>>(_ => throw new UserNotAuthorizedException("testUser", 1, "list risks"));

        // ---- single risk ----------------------------------------------------------------------
        _risksService.GetRisk(1).Returns(NewRisk(1, StatusClosed));
        _risksService.GetRisk(2).Returns(NewRisk(2, RiskHelper.GetRiskStatusName(RiskStatus.MitigationPlanned)));
        _risksService.GetRisk(999).Returns(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.GetRisk(998).Returns(_ => throw new Exception("boom"));
        _risksService.GetRisk(997).Returns(_ => throw new UserNotAuthorizedException("testUser", 1, "read risk"));

        _risksService.GetUserRisk(Arg.Any<User>(), Arg.Any<int>()).Returns(NewRisk(1, "New"));

        // ---- scoring --------------------------------------------------------------------------
        _risksService.GetRiskScoring(1).Returns(new RiskScoring { Id = 1, CalculatedRisk = 5 });
        _risksService.GetRiskScoring(999).Returns(_ => throw new DataNotFoundException("riskScoring", "999"));
        _risksService.GetRiskScoring(998).Returns(_ => throw new Exception("boom"));

        _risksService.CreateRiskScoring(Arg.Any<RiskScoring>())
            .Returns(new RiskScoring { Id = 1, CalculatedRisk = 7 });

        _risksService.When(x => x.DeleteRiskScoring(999))
            .Do(_ => throw new DataNotFoundException("riskScoring", "999"));
        _risksService.When(x => x.DeleteRiskScoring(998))
            .Do(_ => throw new Exception("boom"));

        // ---- entity association ---------------------------------------------------------------
        _risksService.GetRiskEntityByRiskId(1).Returns(new Entity
        {
            Id = 42, DefinitionName = "host", DefinitionVersion = "1", Status = "active"
        });
        _risksService.GetRiskEntityByRiskId(999).Returns(_ => throw new DataNotFoundException("entity", "999"));
        _risksService.GetRiskEntityByRiskId(998).Returns(_ => throw new Exception("boom"));

        _risksService.When(x => x.CleanRiskEntityAssociations(999))
            .Do(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.When(x => x.CleanRiskEntityAssociations(998))
            .Do(_ => throw new Exception("boom"));

        _risksService.When(x => x.DeleteEntityAssociation(999, Arg.Any<int>()))
            .Do(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.When(x => x.DeleteEntityAssociation(998, Arg.Any<int>()))
            .Do(_ => throw new Exception("boom"));

        // ---- vulnerabilities ------------------------------------------------------------------
        _risksService.GetVulnerabilitiesAsync(1, true).Returns(NewVulnerabilities());
        _risksService.GetVulnerabilitiesAsync(999, true)
            .Returns<Task<List<Vulnerability>>>(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.GetVulnerabilitiesAsync(998, true)
            .Returns<Task<List<Vulnerability>>>(_ => throw new Exception("boom"));

        _risksService.GetVulnerabilitiesAsync(999, false)
            .Returns<Task<List<Vulnerability>>>(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.GetVulnerabilitiesAsync(998, false)
            .Returns<Task<List<Vulnerability>>>(_ => throw new Exception("boom"));

        _risksService.GetFilteredVulnerabilitiesAsync(1, Arg.Any<SieveModel>())
            .Returns(new Tuple<int, List<Vulnerability>>(2, NewVulnerabilities()));
        _risksService.GetFilteredVulnerabilitiesAsync(999, Arg.Any<SieveModel>())
            .Returns<Task<Tuple<int, List<Vulnerability>>>>(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.GetFilteredVulnerabilitiesAsync(998, Arg.Any<SieveModel>())
            .Returns<Task<Tuple<int, List<Vulnerability>>>>(_ => throw new Exception("boom"));

        // ---- incident response plan -----------------------------------------------------------
        _risksService.GetIncidentResponsePlanAsync(999)
            .Returns<Task<IncidentResponsePlan>>(_ => throw new DataNotFoundException("irp", "999"));
        _risksService.GetIncidentResponsePlanAsync(998)
            .Returns<Task<IncidentResponsePlan>>(_ => throw new Exception("boom"));

        _risksService.AssocianteRiskToIncidentResponsePlanAsync(998, 1)
            .Returns(_ => throw new Exception("boom"));

        // ---- closure --------------------------------------------------------------------------
        _risksService.GetRiskCloseReasons().Returns(new List<CloseReason>
        {
            new () { Value = 2, Name = "Zeta" },
            new () { Value = 1, Name = "Alpha" }
        });

        _risksService.GetRiskClosureByRiskId(1).Returns(new Closure
        {
            Id = 7, RiskId = 1, CloseReason = 1, Note = "note", UserId = 1
        });
        _risksService.GetRiskClosureByRiskId(999).Returns(_ => throw new DataNotFoundException("closure", "999"));
        _risksService.GetRiskClosureByRiskId(998).Returns(_ => throw new Exception("boom"));

        _risksService.ClosureExists(Arg.Any<int>()).Returns(true);
        _risksService.CreateRiskClosure(Arg.Any<Closure>()).Returns(new Closure
        {
            Id = 11, RiskId = 2, CloseReason = 1, Note = "note", UserId = 1
        });

        // ---- create / save / delete -----------------------------------------------------------
        _risksService.CreateRiskAsync(Arg.Is<Risk>(r => r.Id == 1)).Returns(NewRisk(1, "New"));
        _risksService.CreateRiskAsync(Arg.Is<Risk>(r => r.Id == 2)).Returns((Risk)null);
        _risksService.CreateRiskAsync(Arg.Is<Risk>(r => r.Id == 3))
            .Returns<Task<Risk>>(_ => throw new UserNotAuthorizedException("testUser", 1, "create risk"));

        _risksService.When(x => x.SaveRisk(Arg.Is<Risk>(r => r.Id == 997)))
            .Do(_ => throw new UserNotAuthorizedException("testUser", 1, "save risk"));
        _risksService.When(x => x.SaveRisk(Arg.Is<Risk>(r => r.Id == 996)))
            .Do(_ => throw new Exception("boom"));

        _risksService.When(x => x.DeleteRisk(999))
            .Do(_ => throw new DataNotFoundException("risk", "999"));
        _risksService.When(x => x.DeleteRisk(998))
            .Do(_ => throw new Exception("boom"));

        _risksService.SubjectExists("known").Returns(true);
        _risksService.SubjectExists("unknown").Returns(false);
        _risksService.SubjectExists("unauthorized")
            .Returns(_ => throw new UserNotAuthorizedException("testUser", 1, "check subject"));

        // ---- lookups --------------------------------------------------------------------------
        _risksService.GetRiskCategory(1).Returns(new Category { Value = 1, Name = "Cat 1" });
        _risksService.GetRiskCategory(999).Returns(_ => throw new DataNotFoundException("category", "999"));

        _risksService.GetRiskCategories().Returns(new List<Category>
        {
            new () { Value = 2, Name = "Zeta" },
            new () { Value = 1, Name = "Alpha" }
        });

        _risksService.GetRiskProbabilities().Returns(new List<Likelihood>
        {
            new () { Value = 1, Name = "Low" },
            new () { Value = 2, Name = "High" }
        });

        _risksService.GetRiskImpactsAsync().Returns(new List<Impact>
        {
            new () { Value = 1, Name = "Low" },
            new () { Value = 2, Name = "High" }
        });

        _risksService.GetRiskScore(1, 2).Returns(6.5);
        _risksService.GetRiskScore(999, 999).Returns(_ => throw new DataNotFoundException("score", "999"));

        _risksService.GetRiskCatalog(1).Returns(new RiskCatalog
        {
            Id = 1, Name = "Catalog 1", Number = "1", Description = "d"
        });
        _risksService.GetRiskCatalog(999).Returns(_ => throw new DataNotFoundException("catalog", "999"));

        _risksService.GetRiskCatalogs().Returns(new List<RiskCatalog>
        {
            new () { Id = 1, Name = "Catalog 1", Number = "1", Description = "d" },
            new () { Id = 2, Name = "Catalog 2", Number = "2", Description = "d" }
        });
        _risksService.GetRiskCatalogs(Arg.Any<List<int>>()).Returns(new List<RiskCatalog>
        {
            new () { Id = 2, Name = "Zeta", Number = "2", Description = "d" },
            new () { Id = 1, Name = "Alpha", Number = "1", Description = "d" }
        });
        _risksService.GetRiskCatalogs(Arg.Is<List<int>>(l => l.Contains(999)))
            .Returns(_ => throw new DataNotFoundException("catalog", "999"));

        _risksService.GetRiskSource(1).Returns(new Source { Value = 1, Name = "Source 1" });
        _risksService.GetRiskSource(999).Returns(_ => throw new DataNotFoundException("source", "999"));

        _risksService.GetRiskSources().Returns(new List<Source>
        {
            new () { Value = 1, Name = "Source 1" },
            new () { Value = 2, Name = "Source 2" }
        });
    }

    private void ArrangeMitigations()
    {
        _mitigationsService.GetByRiskId(1).Returns(new Mitigation
        {
            Id = 5,
            RiskId = 1,
            CurrentSolution = "solution",
            SecurityRequirements = "req",
            SecurityRecommendations = "rec"
        });
        _mitigationsService.GetByRiskId(999).Returns(_ => throw new DataNotFoundException("mitigation", "999"));
        _mitigationsService.GetByRiskId(998).Returns(_ => throw new Exception("boom"));
    }

    private void ArrangeFiles()
    {
        _filesService.GetRiskFiles(1).Returns(new List<FileListing>
        {
            new () { Name = "zeta.txt", UniqueName = "z", OwnerId = 1 },
            new () { Name = "alpha.txt", UniqueName = "a", OwnerId = 1 }
        });
        _filesService.GetRiskFiles(998).Returns(_ => throw new Exception("boom"));
    }

    private void ArrangeMgmtReviews()
    {
        _mgmtReviewsService.GetRiskReviews(1).Returns(new List<MgmtReview>
        {
            new () { Id = 1, RiskId = 1, Comments = "c1" },
            new () { Id = 2, RiskId = 1, Comments = "c2" }
        });
        _mgmtReviewsService.GetRiskReviews(998).Returns(_ => throw new Exception("boom"));

        _mgmtReviewsService.GetRiskLastReview(1)
            .Returns(new MgmtReview { Id = 2, RiskId = 1, Comments = "c2" });
        _mgmtReviewsService.GetRiskLastReview(2).Returns((MgmtReview)null);
        _mgmtReviewsService.GetRiskLastReview(999)
            .Returns(_ => throw new DataNotFoundException("review", "999"));
        _mgmtReviewsService.GetRiskLastReview(998).Returns(_ => throw new Exception("boom"));

        _mgmtReviewsService.GetRiskReviewLevel(1)
            .Returns(new ReviewLevel { Id = 1, Value = 1, Name = "Level 1" });
        _mgmtReviewsService.GetRiskReviewLevel(998).Returns(_ => throw new Exception("boom"));
    }

    private static void AssertStatusCode(int expected, IActionResult result)
    {
        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(expected, statusResult.StatusCode);
    }

    private static void AssertObjectStatusCode(int expected, IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expected, objectResult.StatusCode.GetValueOrDefault());
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task TestGetAllAsyncExcludingClosed()
    {
        var result = await _controller.GetAllAsync();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var risks = Assert.IsType<List<Risk>>(ok.Value);
        Assert.Single(risks);
    }

    [Fact]
    public async Task TestGetAllAsyncIncludingClosed()
    {
        var result = await _controller.GetAllAsync(null, true);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var risks = Assert.IsType<List<Risk>>(ok.Value);
        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public async Task TestGetAllAsyncUnauthorized()
    {
        var result = await _controller.GetAllAsync("unauthorized");

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    #endregion

    #region GetToReview

    [Fact]
    public void TestGetToReview()
    {
        var result = _controller.GetToReview(30);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var risks = Assert.IsType<List<Risk>>(ok.Value);
        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public void TestGetToReviewInternalError()
    {
        var result = _controller.GetToReview(999);

        AssertStatusCode(500, result.Result);
    }

    #endregion

    #region GetRisk

    [Fact]
    public void TestGetRisk()
    {
        var result = _controller.GetRisk(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var risk = Assert.IsType<Risk>(ok.Value);
        Assert.Equal(1, risk.Id);
    }

    [Fact]
    public void TestGetRiskNotFound()
    {
        var result = _controller.GetRisk(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetRiskUnauthorized()
    {
        var result = _controller.GetRisk(997);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetRiskInternalError()
    {
        var result = _controller.GetRisk(998);

        AssertStatusCode(500, result.Result);
    }

    #endregion

    #region GetMitigation

    [Fact]
    public void TestGetMitigation()
    {
        var result = _controller.GetMitigation(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var mitigation = Assert.IsType<Mitigation>(ok.Value);
        Assert.Equal(5, mitigation.Id);
    }

    [Fact]
    public void TestGetMitigationNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetMitigation(999).Result);
    }

    [Fact]
    public void TestGetMitigationInternalError()
    {
        AssertStatusCode(500, _controller.GetMitigation(998).Result);
    }

    #endregion

    #region Scoring (read)

    [Fact]
    public void TestGetRiskScoring()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskScoring(1).Result);
        var scoring = Assert.IsType<RiskScoring>(ok.Value);
        Assert.Equal(1, scoring.Id);
    }

    [Fact]
    public void TestGetRiskScoringNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskScoring(999).Result);
    }

    [Fact]
    public void TestGetRiskScoringInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskScoring(998).Result);
    }

    #endregion

    #region Entity association

    [Fact]
    public void TestGetRiskEntity()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskEntity(1).Result);
        Assert.Equal(42, (int)ok.Value);
    }

    [Fact]
    public void TestGetRiskEntityNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskEntity(999).Result);
    }

    [Fact]
    public void TestGetRiskEntityInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskEntity(998).Result);
    }

    [Fact]
    public void TestAssociateRiskEntity()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.AssociateRiskEntity(1, 42).Result);
        Assert.Equal("Entity associated", ok.Value);

        _risksService.Received(1).CleanRiskEntityAssociations(1);
        _risksService.Received(1).AssociateRiskWithEntity(1, 42);
    }

    [Fact]
    public void TestAssociateRiskEntityNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.AssociateRiskEntity(999, 42).Result);
    }

    [Fact]
    public void TestAssociateRiskEntityInternalError()
    {
        AssertStatusCode(500, _controller.AssociateRiskEntity(998, 42).Result);
    }

    [Fact]
    public void TestDeleteRiskEntity()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.DeleteRiskEntity(1, 42).Result);
        Assert.Equal("Entity unassociated", ok.Value);

        _risksService.Received(1).DeleteEntityAssociation(1, 42);
    }

    [Fact]
    public void TestDeleteRiskEntityNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.DeleteRiskEntity(999, 42).Result);
    }

    [Fact]
    public void TestDeleteRiskEntityInternalError()
    {
        AssertStatusCode(500, _controller.DeleteRiskEntity(998, 42).Result);
    }

    #endregion

    #region Files and reviews

    [Fact]
    public void TestGetRiskFiles()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskFiles(1).Result);
        var files = Assert.IsAssignableFrom<IEnumerable<FileListing>>(ok.Value).ToList();

        Assert.Equal(2, files.Count);
        Assert.Equal("alpha.txt", files[0].Name);
    }

    [Fact]
    public void TestGetRiskFilesInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskFiles(998).Result);
    }

    [Fact]
    public void TestGetRiskMgmtReviews()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskMgmtReviews(1).Result);
        var reviews = Assert.IsType<List<MgmtReview>>(ok.Value);
        Assert.Equal(2, reviews.Count);
    }

    [Fact]
    public void TestGetRiskMgmtReviewsInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskMgmtReviews(998).Result);
    }

    [Fact]
    public void TestGetRiskLastMgmtReview()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskLastMgmtReview(1).Result);
        var review = Assert.IsType<MgmtReview>(ok.Value);
        Assert.Equal(2, review.Id);
    }

    [Fact]
    public void TestGetRiskLastMgmtReviewNull()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskLastMgmtReview(2).Result);
    }

    [Fact]
    public void TestGetRiskLastMgmtReviewNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskLastMgmtReview(999).Result);
    }

    [Fact]
    public void TestGetRiskLastMgmtReviewInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskLastMgmtReview(998).Result);
    }

    [Fact]
    public void TestGetRiskReviewLevel()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskReviewLevel(1).Result);
        var level = Assert.IsType<ReviewLevel>(ok.Value);
        Assert.Equal("Level 1", level.Name);
    }

    [Fact]
    public void TestGetRiskReviewLevelInternalError()
    {
        AssertStatusCode(500, _controller.GetRiskReviewLevel(998).Result);
    }

    [Fact]
    public void TestGetAllManagementReviews()
    {
        var result = _controller.GetAllManagementReviews();

        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    #endregion

    #region Vulnerabilities

    [Fact]
    public async Task TestGetVulnerabilities()
    {
        var result = await _controller.GetVulnerabilities(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var vulnerabilities = Assert.IsType<List<Vulnerability>>(ok.Value);
        Assert.Equal(2, vulnerabilities.Count);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesNotFound()
    {
        var result = await _controller.GetVulnerabilities(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesInternalError()
    {
        var result = await _controller.GetVulnerabilities(998);
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesNotFound()
    {
        var result = await _controller.GetOpenVulnerabilities(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesInternalError()
    {
        var result = await _controller.GetOpenVulnerabilities(998);
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public async Task TestGetFilteredVulnerabilities()
    {
        var result = await _controller.GetFilteredVulnerabilities(1, new SieveModel());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var vulnerabilities = Assert.IsType<List<Vulnerability>>(ok.Value);
        Assert.Equal(2, vulnerabilities.Count);
        Assert.Equal("2", _controller.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task TestGetFilteredVulnerabilitiesNotFound()
    {
        var result = await _controller.GetFilteredVulnerabilities(999, new SieveModel());
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetFilteredVulnerabilitiesInternalError()
    {
        var result = await _controller.GetFilteredVulnerabilities(998, new SieveModel());
        AssertStatusCode(500, result.Result);
    }

    #endregion

    #region Incident response plan error branches

    [Fact]
    public async Task TestGetIncidentResponsePlanNotFound()
    {
        var result = await _controller.GetIncidentResponsePlan(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetIncidentResponsePlanInternalError()
    {
        var result = await _controller.GetIncidentResponsePlan(998);
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public async Task TestAssocianteRiskToIncidentResponsePlanInternalError()
    {
        var result = await _controller.AssocianteRiskToIncidentResponsePlan(998, 1);
        AssertStatusCode(500, result);
    }

    #endregion

    #region CreateRiskScoring

    [Fact]
    public void TestCreateRiskScoring()
    {
        var result = _controller.CreateRiskScoring(1, new RiskScoring { CalculatedRisk = 7 });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var scoring = Assert.IsType<RiskScoring>(created.Value);
        Assert.Equal(7f, scoring.CalculatedRisk);
    }

    [Fact]
    public void TestCreateRiskScoringNullBody()
    {
        AssertStatusCode(500, _controller.CreateRiskScoring(1, null).Result);
    }

    [Fact]
    public void TestCreateRiskScoringConflict()
    {
        _risksService.CreateRiskScoring(Arg.Any<RiskScoring>())
            .Returns(_ => throw new DataAlreadyExistsException("netrisk", "risk_scoring", "1", "already exists"));

        AssertObjectStatusCode(409, _controller.CreateRiskScoring(1, new RiskScoring()).Result);
    }

    [Fact]
    public void TestCreateRiskScoringInternalError()
    {
        _risksService.CreateRiskScoring(Arg.Any<RiskScoring>()).Returns(_ => throw new Exception("boom"));

        AssertObjectStatusCode(500, _controller.CreateRiskScoring(1, new RiskScoring()).Result);
    }

    #endregion

    #region SaveRiskScoring / DeleteScoring

    [Fact]
    public void TestSaveRiskScoring()
    {
        var result = _controller.SaveRiskScoring(1, new RiskScoring());

        Assert.IsType<OkResult>(result);
        _risksService.Received(1).SaveRiskScoring(Arg.Is<RiskScoring>(s => s.Id == 1));
    }

    [Fact]
    public void TestSaveRiskScoringNullBody()
    {
        AssertStatusCode(500, _controller.SaveRiskScoring(1));
    }

    [Fact]
    public void TestSaveRiskScoringInternalError()
    {
        _risksService.When(x => x.SaveRiskScoring(Arg.Any<RiskScoring>())).Do(_ => throw new Exception("boom"));

        AssertObjectStatusCode(500, _controller.SaveRiskScoring(1, new RiskScoring()));
    }

    [Fact]
    public void TestDeleteScoring()
    {
        Assert.IsType<OkResult>(_controller.DeleteScoring(1));
        _risksService.Received(1).DeleteRiskScoring(1);
    }

    [Fact]
    public void TestDeleteScoringNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.DeleteScoring(999));
    }

    [Fact]
    public void TestDeleteScoringInternalError()
    {
        AssertStatusCode(500, _controller.DeleteScoring(998));
    }

    #endregion

    #region Close reasons / closure

    [Fact]
    public void TestListCloseReasons()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.ListCloseReasons().Result);
        var reasons = Assert.IsAssignableFrom<IEnumerable<CloseReason>>(ok.Value).ToList();

        Assert.Equal(2, reasons.Count);
        Assert.Equal("Alpha", reasons[0].Name);
    }

    [Fact]
    public void TestListCloseReasonsInternalError()
    {
        _risksService.GetRiskCloseReasons().Returns(_ => throw new Exception("boom"));

        AssertObjectStatusCode(500, _controller.ListCloseReasons().Result);
    }

    [Fact]
    public void TestGetClosure()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetClosure(1).Result);
        var closure = Assert.IsType<Closure>(ok.Value);
        Assert.Equal(7, closure.Id);
    }

    [Fact]
    public void TestGetClosureNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.GetClosure(999).Result);
    }

    [Fact]
    public void TestGetClosureInternalError()
    {
        AssertObjectStatusCode(500, _controller.GetClosure(998).Result);
    }

    #endregion

    #region ReopenRisk

    [Fact]
    public void TestReopenRisk()
    {
        var result = _controller.ReopenRisk(1);

        Assert.IsType<OkResult>(result);
        _risksService.Received(1).DeleteRiskClosure(1);
    }

    [Fact]
    public void TestReopenRiskNotClosed()
    {
        Assert.IsType<BadRequestObjectResult>(_controller.ReopenRisk(2));
    }

    [Fact]
    public void TestReopenRiskNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.ReopenRisk(999));
    }

    [Fact]
    public void TestReopenRiskInternalError()
    {
        AssertObjectStatusCode(500, _controller.ReopenRisk(998));
    }

    #endregion

    #region CloseRisk

    [Fact]
    public void TestCloseRisk()
    {
        var closure = new Closure { RiskId = 2, CloseReason = 1, Note = "done", UserId = 1 };

        var result = _controller.CloseRisk(2, closure);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var created = Assert.IsType<Closure>(ok.Value);
        Assert.Equal(11, created.Id);
        _risksService.Received(1).DeleteRiskClosure(2);
    }

    [Fact]
    public void TestCloseRiskMismatchedRiskId()
    {
        var closure = new Closure { RiskId = 3, CloseReason = 1, Note = "done", UserId = 1 };

        Assert.IsType<BadRequestObjectResult>(_controller.CloseRisk(2, closure).Result);
    }

    [Fact]
    public void TestCloseRiskAlreadyClosed()
    {
        var closure = new Closure { RiskId = 1, CloseReason = 1, Note = "done", UserId = 1 };

        Assert.IsType<BadRequestObjectResult>(_controller.CloseRisk(1, closure).Result);
    }

    [Fact]
    public void TestCloseRiskNotFound()
    {
        var closure = new Closure { RiskId = 999, CloseReason = 1, Note = "done", UserId = 1 };

        Assert.IsType<NotFoundObjectResult>(_controller.CloseRisk(999, closure).Result);
    }

    [Fact]
    public void TestCloseRiskInternalError()
    {
        var closure = new Closure { RiskId = 998, CloseReason = 1, Note = "done", UserId = 1 };

        AssertObjectStatusCode(500, _controller.CloseRisk(998, closure).Result);
    }

    #endregion

    #region CreateAsync / Save / Delete

    [Fact]
    public async Task TestCreateAsync()
    {
        var result = await _controller.CreateAsync(NewRisk(1, "Draft"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var risk = Assert.IsType<Risk>(created.Value);
        Assert.Equal(1, risk.Id);
    }

    [Fact]
    public async Task TestCreateAsyncNullBody()
    {
        var result = await _controller.CreateAsync();
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public async Task TestCreateAsyncServiceReturnsNull()
    {
        var result = await _controller.CreateAsync(NewRisk(2, "Draft"));
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public async Task TestCreateAsyncUnauthorized()
    {
        var result = await _controller.CreateAsync(NewRisk(3, "Draft"));
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestSave()
    {
        var risk = NewRisk(1, "New");

        Assert.IsType<OkResult>(_controller.Save(1, risk));
        _risksService.Received(1).SaveRisk(risk);
    }

    [Fact]
    public void TestSaveNullBody()
    {
        AssertStatusCode(500, _controller.Save(1));
    }

    [Fact]
    public void TestSaveUnauthorized()
    {
        Assert.IsType<UnauthorizedResult>(_controller.Save(997, NewRisk(997, "New")));
    }

    [Fact]
    public void TestSaveInternalError()
    {
        AssertStatusCode(500, _controller.Save(996, NewRisk(996, "New")));
    }

    [Fact]
    public void TestDelete()
    {
        Assert.IsType<OkResult>(_controller.Delete(1));
        _risksService.Received(1).DeleteRisk(1);
    }

    [Fact]
    public void TestDeleteNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.Delete(999));
    }

    [Fact]
    public void TestDeleteInternalError()
    {
        AssertStatusCode(500, _controller.Delete(998));
    }

    #endregion

    #region Exists

    [Fact]
    public void TestSubjectExists()
    {
        AssertStatusCode(200, _controller.Create("known").Result);
    }

    [Fact]
    public void TestSubjectDoesNotExist()
    {
        AssertStatusCode(404, _controller.Create("unknown").Result);
    }

    [Fact]
    public void TestSubjectExistsWithoutSubject()
    {
        AssertStatusCode(500, _controller.Create().Result);
    }

    [Fact]
    public void TestSubjectExistsUnauthorized()
    {
        Assert.IsType<UnauthorizedResult>(_controller.Create("unauthorized").Result);
    }

    #endregion

    #region MyRisks / NeedingMgmtReviews

    [Fact]
    public void TestGetMyRisks()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetMyRisks().Result);
        var risks = Assert.IsType<List<Risk>>(ok.Value);
        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public void TestGetMyRisksEmpty()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.GetMyRisks("empty").Result);
    }

    [Fact]
    public void TestGetMyRisksUnauthorized()
    {
        Assert.IsType<UnauthorizedResult>(_controller.GetMyRisks("unauthorized").Result);
    }

    [Fact]
    public void TestGetRisksNeedingMgmtReviews()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRisksNeedingMgmtReviews().Result);
        var risks = Assert.IsType<List<Risk>>(ok.Value);
        Assert.Single(risks);
    }

    [Fact]
    public void TestGetRisksNeedingMgmtReviewsUnauthorized()
    {
        Assert.IsType<UnauthorizedResult>(_controller.GetRisksNeedingMgmtReviews("unauthorized").Result);
    }

    #endregion

    #region Lookups

    [Fact]
    public void TestGetRiskCategory()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskCategory(1).Result);
        var category = Assert.IsType<Category>(ok.Value);
        Assert.Equal("Cat 1", category.Name);
    }

    [Fact]
    public void TestGetRiskCategoryNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskCategory(999).Result);
    }

    [Fact]
    public void TestGetRiskCategories()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskCategories().Result);
        var categories = Assert.IsAssignableFrom<IEnumerable<Category>>(ok.Value).ToList();

        Assert.Equal(2, categories.Count);
        Assert.Equal("Alpha", categories[0].Name);
    }

    [Fact]
    public void TestGetRiskCategoriesNotFound()
    {
        _risksService.GetRiskCategories().Returns(_ => throw new DataNotFoundException("category", "all"));

        AssertStatusCode(500, _controller.GetRiskCategories().Result);
    }

    [Fact]
    public void TestGetRiskProbabilities()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskProbabilities().Result);
        var probabilities = Assert.IsType<List<Likelihood>>(ok.Value);
        Assert.Equal(2, probabilities.Count);
    }

    [Fact]
    public void TestGetRiskProbabilitiesNotFound()
    {
        _risksService.GetRiskProbabilities().Returns(_ => throw new DataNotFoundException("likelihood", "all"));

        AssertStatusCode(500, _controller.GetRiskProbabilities().Result);
    }

    [Fact]
    public async Task TestGetRiskImpacts()
    {
        var result = await _controller.GetRiskImpacts();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var impacts = Assert.IsType<List<Impact>>(ok.Value);
        Assert.Equal(2, impacts.Count);
    }

    [Fact]
    public async Task TestGetRiskImpactsNotFound()
    {
        _risksService.GetRiskImpactsAsync()
            .Returns<Task<List<Impact>>>(_ => throw new DataNotFoundException("impact", "all"));

        var result = await _controller.GetRiskImpacts();
        AssertStatusCode(500, result.Result);
    }

    [Fact]
    public void TestGetRiskScoreValue()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskScoreValue(1, 2).Result);
        Assert.Equal(6.5, (double)ok.Value);
    }

    [Fact]
    public void TestGetRiskScoreValueNotFound()
    {
        AssertStatusCode(500, _controller.GetRiskScoreValue(999, 999).Result);
    }

    [Fact]
    public void TestGetRiskCatalog()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskCatalog(1).Result);
        var catalog = Assert.IsType<RiskCatalog>(ok.Value);
        Assert.Equal("Catalog 1", catalog.Name);
    }

    [Fact]
    public void TestGetRiskCatalogNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskCatalog(999).Result);
    }

    [Fact]
    public void TestGetRisksCatalogAll()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRisksCatalog().Result);
        var catalogs = Assert.IsType<List<RiskCatalog>>(ok.Value);
        Assert.Equal(2, catalogs.Count);
    }

    [Fact]
    public void TestGetRisksCatalogByList()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRisksCatalog("1,2").Result);
        var catalogs = Assert.IsAssignableFrom<IEnumerable<RiskCatalog>>(ok.Value).ToList();

        Assert.Equal(2, catalogs.Count);
        Assert.Equal("Alpha", catalogs[0].Name);
    }

    [Fact]
    public void TestGetRisksCatalogInvalidList()
    {
        AssertStatusCode(409, _controller.GetRisksCatalog("not-a-list").Result);
    }

    [Fact]
    public void TestGetRisksCatalogNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRisksCatalog("999").Result);
    }

    [Fact]
    public void TestGetRiskSource()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskSource(1).Result);
        var source = Assert.IsType<Source>(ok.Value);
        Assert.Equal("Source 1", source.Name);
    }

    [Fact]
    public void TestGetRiskSourceNotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.GetRiskSource(999).Result);
    }

    [Fact]
    public void TestGetRiskSources()
    {
        var ok = Assert.IsType<OkObjectResult>(_controller.GetRiskSources().Result);
        var sources = Assert.IsType<List<Source>>(ok.Value);
        Assert.Equal(2, sources.Count);
    }

    [Fact]
    public void TestGetRiskSourcesNotFound()
    {
        _risksService.GetRiskSources().Returns(_ => throw new DataNotFoundException("source", "all"));

        Assert.IsType<NotFoundResult>(_controller.GetRiskSources().Result);
    }

    #endregion
}
