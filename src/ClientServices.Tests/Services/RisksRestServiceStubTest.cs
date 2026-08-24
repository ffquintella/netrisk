using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using Model.Rest;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers <see cref="RisksRestService"/> over the programmable <see cref="StubRestBackend"/>, so the
/// real RestSharp client, the real serializers and the real status handling all take part.
///
/// Status semantics relied upon (RestSharp 114, <c>ThrowOnAnyError = false</c>):
/// <list type="bullet">
///   <item>2xx and 404 are both "completed" exchanges — the verb extensions hand the response back
///     instead of throwing, which is what drives every <c>response.StatusCode</c> check here.</item>
///   <item>a 404 with an empty body makes the typed extensions return <c>null</c>, driving the
///     services' own null guards.</item>
///   <item>any other failing status, and a transport failure, surface as
///     <c>HttpRequestException</c>, driving the <c>catch (HttpRequestException)</c> branches.</item>
/// </list>
/// The 401 branches (which call <c>DiscardAuthenticationToken</c> and would write to the real client
/// configuration store) are deliberately not exercised.
/// <see cref="RisksRestServiceTest"/> covers the two incident-response-plan methods.
/// </summary>
[TestSubject(typeof(RisksRestService))]
public class RisksRestServiceStubTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRisksService _service;

    public RisksRestServiceStubTest()
    {
        _service = ResolveWith<IRisksService>(_backend);
    }

    // ---------------------------------------------------------------- fixtures

    private static List<Risk> TwoRisks() =>
    [
        new() { Id = 1, Subject = "Risk one", Status = "New", ReferenceId = "REF-1" },
        new() { Id = 2, Subject = "Risk two", Status = "Closed", ReferenceId = "REF-2" }
    ];

    private static MgmtReview OneReview() => new()
    {
        Id = 11,
        RiskId = 1,
        Review = 2,
        Reviewer = 3,
        NextStep = 4,
        Comments = "Reviewed",
        SubmissionDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        NextReview = new DateOnly(2026, 2, 10)
    };

    private static Closure OneClosure() => new()
    {
        Id = 21,
        RiskId = 1,
        UserId = 2,
        CloseReason = 3,
        Note = "Closing note",
        ClosureDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    private static List<Vulnerability> TwoVulnerabilities() =>
    [
        new() { Id = 31, Title = "Vuln one", Status = 1, Severity = "high" },
        new() { Id = 32, Title = "Vuln two", Status = 2, Severity = "low" }
    ];

    private static OperationError AnError() => new()
    {
        Title = "Validation failed",
        Status = 400,
        Errors = new Dictionary<string, string[]> { ["Subject"] = ["required"] }
    };

    // ------------------------------------------------------- GetAllRisksAsync

    [Fact]
    public async Task TestGetAllRisksAsync()
    {
        _backend.OnGet("/Risks", TwoRisks());

        var risks = await _service.GetAllRisksAsync();

        Assert.Equal(2, risks.Count);
        Assert.Equal("Risk one", risks[0].Subject);
        Assert.Equal("GET /Risks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllRisksAsyncAsksForClosedOnesWhenRequested()
    {
        _backend.OnGet("/Risks", TwoRisks());

        await _service.GetAllRisksAsync(includeClosed: true);

        Assert.Contains("includeClosed=true", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetAllRisksAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks", HttpStatusCode.NotFound);

        var risks = await _service.GetAllRisksAsync();

        Assert.Empty(risks);
    }

    [Fact]
    public async Task TestGetAllRisksAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllRisksAsync());
    }

    [Fact]
    public async Task TestGetAllRisksAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllRisksAsync());
    }

    // ------------------------------------------------------ GetUserRisksAsync

    [Fact]
    public async Task TestGetUserRisksAsync()
    {
        _backend.OnGet("/Risks/MyRisks", TwoRisks());

        var risks = await _service.GetUserRisksAsync();

        Assert.Equal(2, risks.Count);
        Assert.Equal("GET /Risks/MyRisks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetUserRisksAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/MyRisks", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetUserRisksAsync());
    }

    [Fact]
    public async Task TestGetUserRisksAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/MyRisks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserRisksAsync());
    }

    // --------------------------------------------------- GetRiskCategoryAsync

    [Fact]
    public async Task TestGetRiskCategoryAsync()
    {
        _backend.OnGet("/Risks/Categories/7", new Category { Value = 7, Name = "Operational" });

        var name = await _service.GetRiskCategoryAsync(7);

        Assert.Equal("Operational", name);
        Assert.Equal("GET /Risks/Categories/7", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskCategoryAsyncReportsErrorWhenTheCategoryIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Risks/Categories/7", HttpStatusCode.NotFound);

        Assert.Equal("ERROR", await _service.GetRiskCategoryAsync(7));
    }

    [Fact]
    public async Task TestGetRiskCategoryAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Categories/7", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskCategoryAsync(7));
    }

    // ---------------------------------------------------- GetRiskScoringAsync

    [Fact]
    public async Task TestGetRiskScoringAsync()
    {
        _backend.OnGet("/Risks/5/Scoring", new RiskScoring
        {
            Id = 5, ScoringMethod = 1, CalculatedRisk = 7.5f, ClassicLikelihood = 3, ClassicImpact = 5
        });

        var scoring = await _service.GetRiskScoringAsync(5);

        Assert.Equal(5, scoring.Id);
        Assert.Equal(7.5f, scoring.CalculatedRisk);
        Assert.Equal(3f, scoring.ClassicLikelihood);
        Assert.Equal("GET /Risks/5/Scoring", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskScoringAsyncThrowsWhenThereIsNoScoring()
    {
        _backend.OnStatus(Method.Get, "/Risks/5/Scoring", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskScoringAsync(5));
        Assert.Equal("Error getting scoring for risk 5", ex.Message);
    }

    [Fact]
    public async Task TestGetRiskScoringAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/5/Scoring", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskScoringAsync(5));
    }

    // -------------------------------------------------- AssociateEntityToRisk

    [Fact]
    public void TestAssociateEntityToRisk()
    {
        _backend.OnPut("/Risks/4/Entity", "\"ok\"");

        _service.AssociateEntityToRisk(4, 9);

        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal("/Risks/4/Entity", _backend.LastRequest.Path);
        Assert.Equal("9", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestAssociateEntityToRiskThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Put, "/Risks/4/Entity", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.AssociateEntityToRisk(4, 9));
        Assert.Equal("Error adding entity 9 for risk 4", ex.Message);
    }

    [Fact]
    public void TestAssociateEntityToRiskWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Risks/4/Entity", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.AssociateEntityToRisk(4, 9));
    }

    [Fact]
    public void TestAssociateEntityToRiskWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Risks/4/Entity");

        Assert.Throws<RestComunicationException>(() => _service.AssociateEntityToRisk(4, 9));
    }

    // --------------------------------------------------- GetEntityIdFromRisk

    [Fact]
    public void TestGetEntityIdFromRisk()
    {
        _backend.OnGet("/Risks/4/Entity", "12");

        Assert.Equal(12, _service.GetEntityIdFromRisk(4));
        Assert.Equal("GET /Risks/4/Entity", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetEntityIdFromRiskReturnsNullWhenTheRiskHasNoEntity()
    {
        _backend.OnStatus(Method.Get, "/Risks/4/Entity", HttpStatusCode.NotFound);

        Assert.Null(_service.GetEntityIdFromRisk(4));
    }

    [Fact]
    public void TestGetEntityIdFromRiskWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/4/Entity", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetEntityIdFromRisk(4));
    }

    [Fact]
    public void TestGetEntityIdFromRiskWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/4/Entity");

        Assert.Throws<RestComunicationException>(() => _service.GetEntityIdFromRisk(4));
    }

    // ------------------------------------------------------ GetRiskFilesAsync

    [Fact]
    public async Task TestGetRiskFilesAsync()
    {
        _backend.OnGet("/Risks/3/Files", new List<FileListing>
        {
            new() { Name = "report.pdf", UniqueName = "u-1", Type = "pdf", OwnerId = 3 }
        });

        var files = await _service.GetRiskFilesAsync(3);

        Assert.Single(files);
        Assert.Equal("report.pdf", files[0].Name);
        Assert.Equal("u-1", files[0].UniqueName);
        Assert.Equal("GET /Risks/3/Files", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskFilesAsyncThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/3/Files", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskFilesAsync(3));
        Assert.Equal("Error getting files for risk: 3", ex.Message);
    }

    [Fact]
    public async Task TestGetRiskFilesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/3/Files", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskFilesAsync(3));
    }

    // ----------------------------------------------------- GetRiskMgmtReviews

    [Fact]
    public void TestGetRiskMgmtReviews()
    {
        _backend.OnGet("/Risks/1/MgmtReviews", new List<MgmtReview> { OneReview() });

        var reviews = _service.GetRiskMgmtReviews(1);

        Assert.Single(reviews);
        Assert.Equal(11, reviews[0].Id);
        Assert.Equal("Reviewed", reviews[0].Comments);
        Assert.Equal(new DateOnly(2026, 2, 10), reviews[0].NextReview);
        Assert.Equal("GET /Risks/1/MgmtReviews", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskMgmtReviewsThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/MgmtReviews", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetRiskMgmtReviews(1));
        Assert.Equal("Error getting reviews for risk: 1", ex.Message);
    }

    [Fact]
    public void TestGetRiskMgmtReviewsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/MgmtReviews", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskMgmtReviews(1));
    }

    // ------------------------------------------------------ GetRiskReviewLevel

    [Fact]
    public void TestGetRiskReviewLevel()
    {
        _backend.OnGet("/Risks/1/ReviewLevel", new ReviewLevel { Id = 2, Value = 5, Name = "Very High" });

        var level = _service.GetRiskReviewLevel(1);

        Assert.Equal(2, level.Id);
        Assert.Equal(5, level.Value);
        Assert.Equal("Very High", level.Name);
        Assert.Equal("GET /Risks/1/ReviewLevel", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskReviewLevelThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/ReviewLevel", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetRiskReviewLevel(1));
        Assert.Equal("Error getting review level for risk: 1", ex.Message);
    }

    [Fact]
    public void TestGetRiskReviewLevelWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/ReviewLevel", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskReviewLevel(1));
    }

    // --------------------------------------------------- GetRiskLastMgmtReview

    [Fact]
    public void TestGetRiskLastMgmtReview()
    {
        _backend.OnGet("/Risks/1/LastMgmtReview", OneReview());

        var review = _service.GetRiskLastMgmtReview(1);

        Assert.NotNull(review);
        Assert.Equal(11, review.Id);
        Assert.Equal(1, review.RiskId);
        Assert.Equal("Reviewed", review.Comments);
        Assert.Equal("GET /Risks/1/LastMgmtReview", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskLastMgmtReviewReturnsNullWhenThereIsNone()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/LastMgmtReview", HttpStatusCode.NotFound);

        Assert.Null(_service.GetRiskLastMgmtReview(1));
    }

    [Fact]
    public void TestGetRiskLastMgmtReviewWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/1/LastMgmtReview");

        Assert.Throws<RestComunicationException>(() => _service.GetRiskLastMgmtReview(1));
    }

    [Fact]
    public async Task TestGetRiskLastMgmtReviewAsync()
    {
        _backend.OnGet("/Risks/2/LastMgmtReview", OneReview());

        var review = await _service.GetRiskLastMgmtReviewAsync(2);

        Assert.NotNull(review);
        Assert.Equal(11, review.Id);
        Assert.Equal(4, review.NextStep);
        Assert.Equal("GET /Risks/2/LastMgmtReview", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskLastMgmtReviewAsyncReturnsNullWhenThereIsNone()
    {
        _backend.OnStatus(Method.Get, "/Risks/2/LastMgmtReview", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetRiskLastMgmtReviewAsync(2));
    }

    [Fact]
    public async Task TestGetRiskLastMgmtReviewAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/2/LastMgmtReview");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskLastMgmtReviewAsync(2));
    }

    // ---------------------------------------------------------- GetRiskClosure

    [Fact]
    public void TestGetRiskClosure()
    {
        _backend.OnGet("/Risks/1/Closure", OneClosure());

        var closure = _service.GetRiskClosure(1);

        Assert.NotNull(closure);
        Assert.Equal(21, closure.Id);
        Assert.Equal(3, closure.CloseReason);
        Assert.Equal("Closing note", closure.Note);
        Assert.Equal("GET /Risks/1/Closure", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskClosureReturnsNullWhenTheRiskIsOpen()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Closure", HttpStatusCode.NotFound);

        Assert.Null(_service.GetRiskClosure(1));
    }

    [Fact]
    public void TestGetRiskClosureWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Closure", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskClosure(1));
    }

    // ----------------------------------------------------- GetRiskCloseReasons

    [Fact]
    public void TestGetRiskCloseReasons()
    {
        _backend.OnGet("/Risks/CloseReasons", new List<CloseReason>
        {
            new() { Value = 1, Name = "Risk avoided" },
            new() { Value = 2, Name = "Risk transferred" }
        });

        var reasons = _service.GetRiskCloseReasons();

        Assert.Equal(2, reasons.Count);
        Assert.Equal("Risk avoided", reasons[0].Name);
        Assert.Equal("GET /Risks/CloseReasons", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskCloseReasonsThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/CloseReasons", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetRiskCloseReasons());
        Assert.Equal("Error getting closure reasons", ex.Message);
    }

    [Fact]
    public void TestGetRiskCloseReasonsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/CloseReasons", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskCloseReasons());
    }

    // ---------------------------------------------------------------- CloseRisk

    [Fact]
    public void TestCloseRiskPostsTheClosureToTheRiskRoute()
    {
        _backend.OnPost("/Risks/1/Closure", OneClosure());

        _service.CloseRisk(OneClosure());

        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/Risks/1/Closure", _backend.LastRequest.Path);
        Assert.Contains("Closing note", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCloseRiskThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Post, "/Risks/1/Closure", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.CloseRisk(OneClosure()));
        Assert.Equal("Error closing risk", ex.Message);
    }

    [Fact]
    public void TestCloseRiskWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Risks/1/Closure", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.CloseRisk(OneClosure()));
    }

    // ----------------------------------------------------------- ReopenRiskAsync

    [Fact]
    public async Task TestReopenRiskAsync()
    {
        _backend.OnStatus(Method.Delete, "/Risks/8/Closure", HttpStatusCode.OK);

        await _service.ReopenRiskAsync(8);

        Assert.Equal("DELETE /Risks/8/Closure", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestReopenRiskAsyncThrowsWhenTheRiskIsNotClosed()
    {
        _backend.OnStatus(Method.Delete, "/Risks/8/Closure", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.ReopenRiskAsync(8));
        Assert.Equal("Error reopening risk", ex.Message);
    }

    [Fact]
    public async Task TestReopenRiskAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Risks/8/Closure");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.ReopenRiskAsync(8));
    }

    // ------------------------------------------------------ GetRiskCategoriesAsync

    [Fact]
    public async Task TestGetRiskCategoriesAsyncReturnsThemSortedByName()
    {
        _backend.OnGet("/Risks/Categories", new List<Category>
        {
            new() { Value = 1, Name = "Operational" },
            new() { Value = 2, Name = "Financial" }
        });

        var categories = await _service.GetRiskCategoriesAsync();

        Assert.Equal(2, categories.Count);
        Assert.Equal("Financial", categories[0].Name);
        Assert.Equal("Operational", categories[1].Name);
        Assert.Equal("GET /Risks/Categories", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskCategoriesAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Categories", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetRiskCategoriesAsync());
    }

    [Fact]
    public async Task TestGetRiskCategoriesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Categories", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskCategoriesAsync());
    }

    // ------------------------------------------------------------ GetRiskSourceAsync

    [Fact]
    public async Task TestGetRiskSourceAsync()
    {
        _backend.OnGet("/Risks/Sources/6", new Source { Value = 6, Name = "Audit finding" });

        Assert.Equal("Audit finding", await _service.GetRiskSourceAsync(6));
        Assert.Equal("GET /Risks/Sources/6", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskSourceAsyncReportsErrorWhenTheSourceIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources/6", HttpStatusCode.NotFound);

        Assert.Equal("ERROR", await _service.GetRiskSourceAsync(6));
    }

    [Fact]
    public async Task TestGetRiskSourceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources/6", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskSourceAsync(6));
    }

    // -------------------------------------------------------------------- GetToReview

    [Fact]
    public void TestGetToReviewSendsEveryFilterItWasGiven()
    {
        _backend.OnGet("/Risks/ToReview", TwoRisks());

        var risks = _service.GetToReview(30, "New", includeNew: true);

        Assert.Equal(2, risks.Count);
        Assert.Equal("/Risks/ToReview", _backend.LastRequest.Path);
        Assert.Contains("daysSinceLastReview=30", _backend.LastRequest.Query);
        Assert.Contains("status=New", _backend.LastRequest.Query);
        Assert.Contains("includeNew=True", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetToReviewOmitsTheStatusWhenItIsNotGiven()
    {
        _backend.OnGet("/Risks/ToReview", TwoRisks());

        _service.GetToReview(15);

        Assert.Contains("daysSinceLastReview=15", _backend.LastRequest.Query);
        Assert.DoesNotContain("status=", _backend.LastRequest.Query);
        Assert.Contains("includeNew=False", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetToReviewThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/ToReview", HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetToReview(30));
        Assert.Equal("Error getting risks to review", ex.Message);
    }

    [Fact]
    public void TestGetToReviewWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/ToReview", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetToReview(30));
    }

    // ------------------------------------------------------------------- CreateRisk

    [Fact]
    public void TestCreateRiskPostsANewRiskAndReturnsTheSavedOne()
    {
        _backend.OnPost("/Risks", new Risk { Id = 42, Subject = "New risk", Status = "New" },
            HttpStatusCode.Created);

        var risk = new Risk { Id = 99, Subject = "New risk", MitigationId = 3 };

        var created = _service.CreateRisk(risk);

        Assert.NotNull(created);
        Assert.Equal(42, created.Id);
        Assert.Equal("New risk", created.Subject);
        Assert.Equal("POST /Risks", _backend.LastRequest.ToString());
        // the service resets the identity and mitigation before sending
        Assert.Equal(0, risk.Id);
        Assert.Null(risk.MitigationId);
        Assert.DoesNotContain("\"id\":99", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"mitigationId\":3", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New risk", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateRiskReportsTheServerValidationError()
    {
        _backend.On(Method.Post, "/Risks", AnError(), HttpStatusCode.NotFound);

        var ex = Assert.Throws<ErrorSavingException>(() => _service.CreateRisk(new Risk { Subject = "x" }));

        Assert.Equal("Error creating risk", ex.Message);
        Assert.Equal("Validation failed", ex.Result.Title);
        Assert.Equal(400, ex.Result.Status);
    }

    [Fact]
    public void TestCreateRiskWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Risks");

        Assert.Throws<RestComunicationException>(() => _service.CreateRisk(new Risk { Subject = "x" }));
    }

    // --------------------------------------------------------------------- SaveRisk

    [Fact]
    public void TestSaveRiskPutsTheRiskOnItsOwnRoute()
    {
        _backend.OnStatus(Method.Put, "/Risks/13", HttpStatusCode.OK);

        _service.SaveRisk(new Risk { Id = 13, Subject = "Saved subject" });

        Assert.Equal("PUT /Risks/13", _backend.LastRequest.ToString());
        Assert.Contains("Saved subject", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveRiskReportsTheServerValidationError()
    {
        _backend.On(Method.Put, "/Risks/13", AnError(), HttpStatusCode.NotFound);

        var ex = Assert.Throws<ErrorSavingException>(() => _service.SaveRisk(new Risk { Id = 13 }));

        Assert.Equal("Error saving risk", ex.Message);
        Assert.Equal("Validation failed", ex.Result.Title);
    }

    [Fact]
    public void TestSaveRiskThrowsWhenTheErrorBodyIsNotAnOperationError()
    {
        // SaveRisk always threw, but it built the exception from an unguarded Deserialize call, so an
        // error body that was not an OperationError escaped as a raw JsonException instead.
        _backend.On(Method.Put, "/Risks/13", "not json at all", HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.SaveRisk(new Risk { Id = 13 }));

        Assert.Equal("/Risks/13", ex.Url);
        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public void TestSaveRiskWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Risks/13");

        Assert.Throws<RestComunicationException>(() => _service.SaveRisk(new Risk { Id = 13 }));
    }

    // ------------------------------------------------------------------- DeleteRisk

    [Fact]
    public void TestDeleteRisk()
    {
        _backend.OnStatus(Method.Delete, "/Risks/13", HttpStatusCode.OK);

        _service.DeleteRisk(new Risk { Id = 13 });

        Assert.Equal("DELETE /Risks/13", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteRiskThrowsWhenTheRiskIsUnknown()
    {
        _backend.OnStatus(Method.Delete, "/Risks/13", HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.DeleteRisk(new Risk { Id = 13 }));
        Assert.Equal("Error deleting risk", ex.Message);
        Assert.Equal("/Risks/13", ex.Url);
        Assert.Equal("DELETE", ex.Method);
    }

    [Fact]
    public void TestDeleteRiskWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Risks/13");

        Assert.Throws<RestComunicationException>(() => _service.DeleteRisk(new Risk { Id = 13 }));
    }

    // ------------------------------------------------------------ GetVulnerabilities

    [Fact]
    public async Task TestGetVulnerabilitiesAsync()
    {
        _backend.OnGet("/Risks/1/Vulnerabilities", TwoVulnerabilities());

        var vulnerabilities = await _service.GetVulnerabilitiesAsync(1);

        Assert.Equal(2, vulnerabilities.Count);
        Assert.Equal("Vuln one", vulnerabilities[0].Title);
        Assert.Equal("GET /Risks/1/Vulnerabilities", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVulnerabilitiesAsyncThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Vulnerabilities", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetVulnerabilitiesAsync(1));
        Assert.Equal("Error getting vulnerabilities for risk", ex.Message);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Vulnerabilities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetVulnerabilitiesAsync(1));
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesAsync()
    {
        _backend.OnGet("/Risks/1/Vulnerabilities/Open", TwoVulnerabilities());

        var vulnerabilities = await _service.GetOpenVulnerabilitiesAsync(1);

        Assert.Equal(2, vulnerabilities.Count);
        Assert.Equal(32, vulnerabilities[1].Id);
        Assert.Equal("GET /Risks/1/Vulnerabilities/Open", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesAsyncThrowsWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Vulnerabilities/Open", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetOpenVulnerabilitiesAsync(1));
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Vulnerabilities/Open", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetOpenVulnerabilitiesAsync(1));
    }

    // ------------------------------------------- GetOpenVulnerabilitiesPageAsync

    [Fact]
    public async Task TestGetOpenVulnerabilitiesPageAsyncSendsThePagingAndStatusFilter()
    {
        _backend.OnGet("/Risks/1/Vulnerabilities/Filtered", TwoVulnerabilities());

        // Known limitation of this backend: it cannot add the X-Total-Count response header the
        // service needs, so the paged happy path cannot be reached from here — the service correctly
        // refuses a page it cannot count. The request it built is still asserted below.
        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetOpenVulnerabilitiesPageAsync(1, 2, 10));

        Assert.Equal("/Risks/1/Vulnerabilities/Filtered", _backend.LastRequest.Path);
        Assert.Contains("pageSize=10", _backend.LastRequest.Query);
        Assert.Contains("page=2", _backend.LastRequest.Query);
        Assert.Contains("filters=", _backend.LastRequest.Query);
        Assert.Contains("36", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesPageAsyncThrowsOnAnUnexpectedStatus()
    {
        _backend.OnStatus(Method.Get, "/Risks/1/Vulnerabilities/Filtered", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetOpenVulnerabilitiesPageAsync(1, 1, 10));
        Assert.Equal("Error getting vulnerabilities for risk", ex.Message);
    }

    [Fact]
    public async Task TestGetOpenVulnerabilitiesPageAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/1/Vulnerabilities/Filtered");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetOpenVulnerabilitiesPageAsync(1, 1, 10));
    }

    // ------------------------------------------------------------- CreateRiskScoring

    [Fact]
    public void TestCreateRiskScoringPostsToTheScoringRoute()
    {
        _backend.OnPost("/Risks/5/Scoring", new RiskScoring { Id = 5, CalculatedRisk = 7.5f },
            HttpStatusCode.Created);

        var created = _service.CreateRiskScoring(new RiskScoring
        {
            Id = 5, ScoringMethod = 1, CalculatedRisk = 7.5f, ClassicLikelihood = 3, ClassicImpact = 5
        });

        Assert.NotNull(created);
        Assert.Equal(5, created.Id);
        Assert.Equal(7.5f, created.CalculatedRisk);
        Assert.Equal("POST /Risks/5/Scoring", _backend.LastRequest.ToString());
        Assert.Contains("7.5", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateRiskScoringReportsTheServerValidationError()
    {
        _backend.On(Method.Post, "/Risks/5/Scoring", AnError(), HttpStatusCode.NotFound);

        var ex = Assert.Throws<ErrorSavingException>(
            () => _service.CreateRiskScoring(new RiskScoring { Id = 5 }));

        Assert.Equal("Error creating risk scoring", ex.Message);
        Assert.Equal("Validation failed", ex.Result.Title);
    }

    [Fact]
    public void TestCreateRiskScoringWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Risks/5/Scoring", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(
            () => _service.CreateRiskScoring(new RiskScoring { Id = 5 }));
    }

    // --------------------------------------------------------------- SaveRiskScoring

    [Fact]
    public void TestSaveRiskScoring()
    {
        _backend.OnStatus(Method.Put, "/Risks/5/Scoring", HttpStatusCode.OK);

        _service.SaveRiskScoring(new RiskScoring { Id = 5, CalculatedRisk = 4.25f });

        Assert.Equal("PUT /Risks/5/Scoring", _backend.LastRequest.ToString());
        Assert.Contains("4.25", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveRiskScoringReportsTheServerValidationError()
    {
        _backend.On(Method.Put, "/Risks/5/Scoring", AnError(), HttpStatusCode.NotFound);

        var ex = Assert.Throws<ErrorSavingException>(
            () => _service.SaveRiskScoring(new RiskScoring { Id = 5 }));

        Assert.Equal("Error saving risk scoring", ex.Message);
        Assert.Equal(400, ex.Result.Status);
    }

    [Fact]
    public void TestSaveRiskScoringThrowsWhenTheErrorBodyIsNotAnOperationError()
    {
        // The pointed end of the bug: the method threw only `if (opResult != null)`, so any 404 whose
        // body did not deserialize into an OperationError - here the JSON literal `null`, which
        // deserializes cleanly to a null reference - returned as though the scoring had been saved.
        _backend.On(Method.Put, "/Risks/5/Scoring", "null", HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(
            () => _service.SaveRiskScoring(new RiskScoring { Id = 5 }));

        Assert.Equal("/Risks/5/Scoring", ex.Url);
        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public void TestSaveRiskScoringThrowsWhenTheServerSendsNoErrorBodyAtAll()
    {
        _backend.OnStatus(Method.Put, "/Risks/5/Scoring", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(
            () => _service.SaveRiskScoring(new RiskScoring { Id = 5 }));
    }

    [Fact]
    public void TestSaveRiskScoringWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Risks/5/Scoring");

        Assert.Throws<RestComunicationException>(
            () => _service.SaveRiskScoring(new RiskScoring { Id = 5 }));
    }

    // ------------------------------------------------------------- DeleteRiskScoring

    [Fact]
    public void TestDeleteRiskScoring()
    {
        _backend.OnStatus(Method.Delete, "/Risks/5/Scoring", HttpStatusCode.OK);

        _service.DeleteRiskScoring(5);

        Assert.Equal("DELETE /Risks/5/Scoring", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteRiskScoringThrowsWhenTheScoringIsUnknown()
    {
        _backend.OnStatus(Method.Delete, "/Risks/5/Scoring", HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.DeleteRiskScoring(5));
        Assert.Equal("Error deleting risk scoring", ex.Message);
        Assert.Equal("/Risks/5/Scoring", ex.Url);
        Assert.Equal("DELETE", ex.Method);
    }

    [Fact]
    public void TestDeleteRiskScoringWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Risks/5/Scoring");

        Assert.Throws<RestComunicationException>(() => _service.DeleteRiskScoring(5));
    }

    // ------------------------------------------------------------- RiskSubjectExists

    [Fact]
    public void TestRiskSubjectExistsIsTrueWhenTheServerConfirmsIt()
    {
        _backend.OnStatus(Method.Get, "/Risks/Exists", HttpStatusCode.OK);

        Assert.True(_service.RiskSubjectExists("Known subject"));
        Assert.Equal("/Risks/Exists", _backend.LastRequest.Path);
        Assert.Contains("subject=", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestRiskSubjectExistsIsFalseWhenTheServerDoesNotFindIt()
    {
        _backend.OnStatus(Method.Get, "/Risks/Exists", HttpStatusCode.NotFound);

        Assert.False(_service.RiskSubjectExists("Unknown subject"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TestRiskSubjectExistsShortCircuitsAnEmptySubject(string? subject)
    {
        Assert.False(_service.RiskSubjectExists(subject!));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestRiskSubjectExistsWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/Exists");

        Assert.Throws<RestComunicationException>(() => _service.RiskSubjectExists("Known subject"));
    }

    // ----------------------------------------------------------------- GetRiskSources

    [Fact]
    public void TestGetRiskSourcesReturnsThemSortedByName()
    {
        _backend.OnGet("/Risks/Sources", new List<Source>
        {
            new() { Value = 1, Name = "Self assessment" },
            new() { Value = 2, Name = "Audit finding" }
        });

        var sources = _service.GetRiskSources();

        Assert.NotNull(sources);
        Assert.Equal("Audit finding", sources[0].Name);
        Assert.Equal("Self assessment", sources[1].Name);
        Assert.Equal("GET /Risks/Sources", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskSourcesReturnsNullWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources", HttpStatusCode.NotFound);

        Assert.Null(_service.GetRiskSources());
    }

    [Fact]
    public void TestGetRiskSourcesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskSources());
    }

    [Fact]
    public async Task TestGetRiskSourcesAsyncReturnsThemSortedByName()
    {
        _backend.OnGet("/Risks/Sources", new List<Source>
        {
            new() { Value = 1, Name = "Self assessment" },
            new() { Value = 2, Name = "Audit finding" }
        });

        var sources = await _service.GetRiskSourcesAsync()!;

        Assert.NotNull(sources);
        Assert.Equal("Audit finding", sources[0].Name);
        Assert.Equal("GET /Risks/Sources", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskSourcesAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources", HttpStatusCode.NotFound);

        var sources = await _service.GetRiskSourcesAsync()!;

        Assert.NotNull(sources);
        Assert.Empty(sources);
    }

    [Fact]
    public async Task TestGetRiskSourcesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Sources", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(async () => await _service.GetRiskSourcesAsync()!);
    }

    // ------------------------------------------------------------ GetProbabilitiesAsync

    [Fact]
    public async Task TestGetProbabilitiesAsync()
    {
        _backend.OnGet("/Risks/Probabilities", new List<Likelihood>
        {
            new() { Value = 1, Name = "Unlikely" },
            new() { Value = 5, Name = "Almost certain" }
        });

        var probabilities = await _service.GetProbabilitiesAsync();

        Assert.NotNull(probabilities);
        Assert.Equal(2, probabilities.Count);
        Assert.Equal("Unlikely", probabilities[0].Name);
        Assert.Equal(5, probabilities[1].Value);
        Assert.Equal("GET /Risks/Probabilities", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetProbabilitiesAsyncReturnsNullWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Probabilities", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetProbabilitiesAsync());
    }

    [Fact]
    public async Task TestGetProbabilitiesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Probabilities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetProbabilitiesAsync());
    }

    // ---------------------------------------------------------------------- Impacts

    [Fact]
    public void TestGetImpacts()
    {
        _backend.OnGet("/Risks/Impacts", new List<Impact>
        {
            new() { Value = 1, Name = "Insignificant" },
            new() { Value = 5, Name = "Catastrophic" }
        });

        var impacts = _service.GetImpacts();

        Assert.NotNull(impacts);
        Assert.Equal(2, impacts.Count);
        Assert.Equal("Catastrophic", impacts[1].Name);
        Assert.Equal("GET /Risks/Impacts", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetImpactsReturnsNullWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Impacts", HttpStatusCode.NotFound);

        Assert.Null(_service.GetImpacts());
    }

    [Fact]
    public void TestGetImpactsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Impacts", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetImpacts());
    }

    [Fact]
    public async Task TestGetImpactsAsync()
    {
        _backend.OnGet("/Risks/Impacts", new List<Impact> { new() { Value = 3, Name = "Moderate" } });

        var impacts = await _service.GetImpactsAsync();

        Assert.NotNull(impacts);
        Assert.Single(impacts);
        Assert.Equal("Moderate", impacts[0].Name);
        Assert.Equal("GET /Risks/Impacts", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetImpactsAsyncReturnsNullWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Impacts", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetImpactsAsync());
    }

    [Fact]
    public async Task TestGetImpactsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Impacts", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetImpactsAsync());
    }

    // ------------------------------------------------------------------ GetRiskScore

    [Fact]
    public void TestGetRiskScore()
    {
        _backend.OnGet("/Risks/ScoreValue-3-4", "7.5");

        Assert.Equal(7.5f, _service.GetRiskScore(3, 4));
        Assert.Equal("GET /Risks/ScoreValue-3-4", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskScoreIsZeroWhenTheServerHasNoValue()
    {
        _backend.OnStatus(Method.Get, "/Risks/ScoreValue-3-4", HttpStatusCode.NotFound);

        Assert.Equal(0f, _service.GetRiskScore(3, 4));
    }

    [Fact]
    public void TestGetRiskScoreWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/ScoreValue-3-4", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskScore(3, 4));
    }

    // ------------------------------------------------------------------- RiskTypes

    private static List<RiskCatalog> TwoCatalogs() =>
    [
        new() { Id = 1, Number = "1.1", Name = "Catalog one", Description = "D1", Grouping = 1, Function = 1, Order = 1 },
        new() { Id = 2, Number = "1.2", Name = "Catalog two", Description = "D2", Grouping = 1, Function = 1, Order = 2 }
    ];

    [Fact]
    public async Task TestGetRiskTypesAsyncAsksForEveryCatalog()
    {
        _backend.OnGet("/Risks/Catalogs", TwoCatalogs());

        var catalogs = await _service.GetRiskTypesAsync();

        Assert.Equal(2, catalogs.Count);
        Assert.Equal("Catalog one", catalogs[0].Name);
        // the no-argument overload asks for everything, so it sends no list filter
        Assert.Equal("GET /Risks/Catalogs", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetRiskTypesAsyncSendsTheRequestedIdsWithoutTheTrailingComma()
    {
        _backend.OnGet("/Risks/Catalogs", TwoCatalogs());

        var catalogs = await _service.GetRiskTypesAsync("1,2,");

        Assert.Equal(2, catalogs.Count);
        Assert.Contains("list=1", _backend.LastRequest.Query);
        Assert.DoesNotContain("2%2C", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetRiskTypesAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Catalogs", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetRiskTypesAsync());
    }

    [Fact]
    public async Task TestGetRiskTypesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Catalogs", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRiskTypesAsync());
    }

    [Fact]
    public void TestGetRiskTypesAsksForEveryCatalog()
    {
        _backend.OnGet("/Risks/Catalogs", TwoCatalogs());

        var catalogs = _service.GetRiskTypes();

        Assert.Equal(2, catalogs.Count);
        Assert.Equal("1.2", catalogs[1].Number);
        Assert.Equal("GET /Risks/Catalogs", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRiskTypesSendsTheRequestedIds()
    {
        _backend.OnGet("/Risks/Catalogs", TwoCatalogs());

        var catalogs = _service.GetRiskTypes("7,", false);

        Assert.Equal(2, catalogs.Count);
        Assert.Contains("list=7", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetRiskTypesReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks/Catalogs", HttpStatusCode.NotFound);

        Assert.Empty(_service.GetRiskTypes());
    }

    [Fact]
    public void TestGetRiskTypesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks/Catalogs", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRiskTypes());
    }

    // --------------------------------------------------------------- GetFilteredAsync

    [Fact]
    public async Task TestGetFilteredAsyncSendsTheFilter()
    {
        _backend.OnGet("/Risks", TwoRisks());

        var risks = await _service.GetFilteredAsync("subject==Risk one");

        Assert.Equal(2, risks.Count);
        Assert.Equal("/Risks", _backend.LastRequest.Path);
        Assert.Contains("filters=", _backend.LastRequest.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task TestGetFilteredAsyncSendsNoFilterWhenThereIsNone(string? filter)
    {
        _backend.OnGet("/Risks", TwoRisks());

        var risks = await _service.GetFilteredAsync(filter);

        Assert.Equal(2, risks.Count);
        Assert.Equal("GET /Risks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetFilteredAsyncReturnsAnEmptyListWhenThereIsNoAnswer()
    {
        _backend.OnStatus(Method.Get, "/Risks", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetFilteredAsync("subject==x"));
    }

    [Fact]
    public async Task TestGetFilteredAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Risks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetFilteredAsync("subject==x"));
    }
}
