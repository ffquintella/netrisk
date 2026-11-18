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
using Model.Governance;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(MgmtReviewsRestService))]
public class MgmtReviewsRestServiceTest : BaseServiceTest
{
    private const string TypesPath = "/MgmtReviews/Types";
    private const string NextStepsPath = "/MgmtReviews/NextSteps";
    private const string CreatePath = "/MgmtReviews";

    private readonly StubRestBackend _backend = new();
    private readonly IMgmtReviewsService _service;

    public MgmtReviewsRestServiceTest()
    {
        _service = ResolveWith<IMgmtReviewsService>(_backend);
    }

    private static MgmtReviewDto ADto() => new()
    {
        RiskId = 3,
        SubmissionDate = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc),
        Review = 1,
        Reviewer = 2,
        NextStep = 4,
        Comments = "looks fine",
        NextReview = new DateOnly(2026, 6, 1)
    };

    // ---------------- GetReviewTypes ----------------

    [Fact]
    public void TestGetReviewTypes()
    {
        _backend.OnGet(TypesPath, new List<Review>
        {
            new() { Value = 1, Name = "Consider for Project" },
            new() { Value = 2, Name = "Accept" }
        });

        var types = _service.GetReviewTypes();

        Assert.Equal(2, types.Count);
        Assert.Equal(1, types[0].Value);
        Assert.Equal("Consider for Project", types[0].Name);
        Assert.Equal("Accept", types[1].Name);
        Assert.Equal("GET " + TypesPath, _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetReviewTypesThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, TypesPath, HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetReviewTypes());
        Assert.Equal("Error getting review types", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestGetReviewTypesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, TypesPath, HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetReviewTypes());
    }

    [Fact]
    public void TestGetReviewTypesWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, TypesPath);

        Assert.Throws<RestComunicationException>(() => _service.GetReviewTypes());
    }

    // ---------------- GetNextSteps ----------------

    [Fact]
    public void TestGetNextSteps()
    {
        _backend.OnGet(NextStepsPath, new List<NextStep>
        {
            new() { Value = 4, Name = "Mitigate" },
            new() { Value = 5, Name = "Close" }
        });

        var steps = _service.GetNextSteps();

        Assert.Equal(2, steps.Count);
        Assert.Equal(4, steps[0].Value);
        Assert.Equal("Mitigate", steps[0].Name);
        Assert.Equal("GET " + NextStepsPath, _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetNextStepsThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, NextStepsPath, HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetNextSteps());
        Assert.Equal("Error getting review next steps", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestGetNextStepsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, NextStepsPath, HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetNextSteps());
    }

    [Fact]
    public void TestGetNextStepsWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, NextStepsPath);

        Assert.Throws<RestComunicationException>(() => _service.GetNextSteps());
    }

    // ---------------- Create ----------------

    [Fact]
    public void TestCreatePostsTheDtoAndDeserializesTheCreatedReview()
    {
        _backend.OnPost(CreatePath,
            "{\"id\":9,\"riskId\":3,\"submissionDate\":\"2026-01-05T10:00:00\",\"review\":1,\"reviewer\":2," +
            "\"nextStep\":4,\"comments\":\"looks fine\",\"nextReview\":\"2026-06-01\"}",
            HttpStatusCode.Created);

        var created = _service.Create(ADto());

        Assert.Equal(9, created.Id);
        Assert.Equal(3, created.RiskId);
        Assert.Equal(4, created.NextStep);
        Assert.Equal("looks fine", created.Comments);
        Assert.Equal(new DateOnly(2026, 6, 1), created.NextReview);
        Assert.Equal("POST " + CreatePath, _backend.LastRequest.ToString());
        Assert.Contains("looks fine", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateThrowsWhenTheServerDoesNotAnswerCreated()
    {
        // 200 instead of 201: RestSharp is happy, the service is not.
        _backend.OnPost(CreatePath, new { id = 9 });

        var ex = Assert.Throws<RestComunicationException>(() => _service.Create(ADto()));
        Assert.Equal("Error creating review", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestCreateThrowsWhenTheServerReturnsNotFound()
    {
        _backend.OnStatus(Method.Post, CreatePath, HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Create(ADto()));
    }

    [Fact]
    public void TestCreateThrowsWhenTheCreatedBodyIsNull()
    {
        // A 201 whose body is literally `null` deserializes to null; the service raises a typed
        // exception there, which its `catch (HttpRequestException)` does not intercept.
        _backend.OnPost(CreatePath, "null", HttpStatusCode.Created);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.Create(ADto()));
        Assert.Equal("Error deserializing review", ex.Message);
        Assert.Equal(CreatePath, ex.Url);
        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public void TestCreateWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, CreatePath, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.Create(ADto()));
        Assert.Equal("Error creating review", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestCreateWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, CreatePath);

        Assert.Throws<RestComunicationException>(() => _service.Create(ADto()));
    }

    /// <summary>
    /// T278: the server refuses a review by someone who submitted, owns or manages the risk with a 422 naming the rule.
    /// That reaches the dialog as the rule, so it can say why; it used to be the same opaque failure as an outage.
    /// </summary>
    [Fact]
    public void TestCreatePassesASegregationRefusalThroughAsTheRule()
    {
        _backend.OnPost(CreatePath,
            new { error = "segregation_of_duties", message = "You cannot review this risk because you own it." },
            HttpStatusCode.UnprocessableEntity);

        var ex = Assert.Throws<RuleBrokenException>(() => _service.Create(ADto()));

        Assert.Equal("segregation_of_duties", ex.RuleName);
    }

    // ---------------- GetOverdueReviewsAsync ----------------

    private const string OverduePath = "/MgmtReviews/Overdue";

    [Fact]
    public async Task TestGetOverdueReviewsReadsTheListWithItsDueDate()
    {
        _backend.OnGet(OverduePath,
            "[{\"riskId\":4,\"subject\":\"Payment gateway\",\"referenceId\":\"R-4\",\"status\":\"New\",\"ownerId\":2," +
            "\"cadenceDays\":90,\"dueAt\":\"2026-10-01T00:00:00Z\",\"dueBasis\":2,\"daysOverdue\":48," +
            "\"lastReviewedAt\":\"2026-09-01T00:00:00Z\"}]");

        var overdue = await _service.GetOverdueReviewsAsync();

        var item = Assert.Single(overdue);
        Assert.Equal((4, ReviewDueBasis.AcceptanceEnd, 48), (item.RiskId, item.DueBasis, item.DaysOverdue));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), item.DueAt.ToUniversalTime());
        Assert.Equal("GET " + OverduePath, _backend.LastRequest.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TestGetOverdueReviewsFailsOnARefusal(HttpStatusCode status)
    {
        _backend.OnStatus(Method.Get, OverduePath, status);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetOverdueReviewsAsync());
        Assert.Equal("Error listing overdue reviews", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestGetOverdueReviewsWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, OverduePath);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetOverdueReviewsAsync());
    }
}
