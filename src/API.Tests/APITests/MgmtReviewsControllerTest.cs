using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(MgmtReviewsController))]
public class MgmtReviewsControllerTest : BaseControllerTest
{
    private readonly IMgmtReviewsService _mgmtReviewsService = Substitute.For<IMgmtReviewsService>();
    private readonly IRisksService _risksService = Substitute.For<IRisksService>();
    private readonly MgmtReviewsController _controller;

    private static MgmtReview SampleReview(int id = 1) => new()
    {
        Id = id,
        RiskId = 1,
        Review = 1,
        Reviewer = 1,
        NextStep = 1,
        Comments = "a comment",
        SubmissionDate = new DateTime(2024, 1, 1),
        NextReview = new DateOnly(2024, 6, 1)
    };

    private static MgmtReviewDto SampleDto(int id) => new()
    {
        Id = id,
        RiskId = 1,
        Review = 1,
        Reviewer = 0,
        NextStep = 1,
        Comments = "a comment",
        SubmissionDate = new DateTime(2024, 1, 1),
        NextReview = new DateOnly(2024, 6, 1)
    };

    public MgmtReviewsControllerTest()
    {
        _mgmtReviewsService.CreateReviewAsync(Arg.Any<MgmtReview>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(SampleReview(7));
        _mgmtReviewsService.UpdateAsync(Arg.Any<MgmtReviewDto>(), Arg.Any<int>()).Returns(SampleReview(3));
        _mgmtReviewsService.GetOne(1).Returns(SampleReview());
        _mgmtReviewsService.GetReviewTypes().Returns(new List<Review>
        {
            new() { Value = 1, Name = "Review one" },
            new() { Value = 2, Name = "Review two" }
        });
        _mgmtReviewsService.GetNextSteps().Returns(new List<NextStep>
        {
            new() { Value = 1, Name = "Step one" },
            new() { Value = 2, Name = "Step two" }
        });

        _controller = ResolveController<MgmtReviewsController>(s =>
        {
            s.AddSingleton(_mgmtReviewsService);
            s.AddSingleton(_risksService);
        });
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    /// <summary>Builds a controller whose create path refuses with <paramref name="refusal"/>.</summary>
    private static MgmtReviewsController RefusingController(Exception refusal)
    {
        var refusing = Substitute.For<IMgmtReviewsService>();
        refusing.CreateReviewAsync(Arg.Any<MgmtReview>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns<Task<MgmtReview>>(_ => throw refusal);

        return ResolveController<MgmtReviewsController>(s =>
        {
            s.AddSingleton(refusing);
            s.AddSingleton(Substitute.For<IRisksService>());
        });
    }

    /// <summary>Builds a controller over a service that fails, to drive the catch blocks.</summary>
    private static MgmtReviewsController FailingController()
    {
        var failing = Substitute.For<IMgmtReviewsService>();
        failing.CreateReviewAsync(Arg.Any<MgmtReview>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns<Task<MgmtReview>>(_ => throw new InvalidOperationException("boom"));
        failing.UpdateAsync(Arg.Any<MgmtReviewDto>(), Arg.Any<int>())
            .Returns<Task<MgmtReview>>(_ => throw new InvalidOperationException("boom"));
        failing.GetOne(Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("boom"));
        failing.GetReviewTypes().Returns(_ => throw new InvalidOperationException("boom"));
        failing.GetNextSteps().Returns(_ => throw new InvalidOperationException("boom"));

        return ResolveController<MgmtReviewsController>(s =>
        {
            s.AddSingleton(failing);
            s.AddSingleton(Substitute.For<IRisksService>());
        });
    }

    [Fact]
    public async Task TestCreate()
    {
        var result = await _controller.Create(SampleDto(0));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var review = Assert.IsType<MgmtReview>(created.Value);
        Assert.Equal(7, review.Id);
    }

    [Fact]
    public async Task TestCreateResetsSuppliedId()
    {
        var dto = SampleDto(42);

        var result = await _controller.Create(dto);

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(0, dto.Id);
        // The authenticated user becomes the reviewer.
        Assert.Equal(1, dto.Reviewer);
    }

    /// <summary>
    /// The compliant POST goes through the enforced service path acting as the caller (user 1): the payload's id (42)
    /// and named reviewer (0) are not what reaches the service. Before the fix the route called the legacy
    /// <c>Create(MgmtReview)</c>, which carried no acting user and applied no segregation of duties (S53 §11, defect 2).
    /// </summary>
    [Fact]
    public async Task TestCreateGoesThroughTheEnforcedPathAsTheCaller()
    {
        var result = await _controller.Create(SampleDto(42));

        Assert.IsType<CreatedResult>(result.Result);
        await _mgmtReviewsService.Received(1).CreateReviewAsync(
            Arg.Is<MgmtReview>(r => r.Id == 0 && r.RiskId == 1 && r.Reviewer == 1), 1, null);
    }

    /// <summary>
    /// A review by someone who submitted, owns or manages the risk is refused with 422 naming the rule, as risk
    /// acceptance answers it — not created, as it was before the fix.
    /// </summary>
    [Fact]
    public async Task TestCreateRefusesAReviewThatBreaksSegregationOfDuties()
    {
        var result = await RefusingController(new RuleBrokenException(
            "You cannot review this risk because you own it.", "segregation_of_duties")).Create(SampleDto(0));

        var refused = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Contains("segregation_of_duties", Json(refused.Value));
        Assert.Contains("because you own it", Json(refused.Value));
    }

    /// <summary>A permission refusal from the enforced path (an override with the break-glass switched off) is 403.</summary>
    [Fact]
    public async Task TestCreatePermissionRefusalIsForbidden()
    {
        var result = await RefusingController(new PermissionInvalidException(
            "risk_workflow_segregation_break_glass", 1, "review")).Create(SampleDto(0));

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Contains("insufficient_authority", Json(forbidden.Value));
    }

    /// <summary>A risk the caller cannot see, or that does not exist, is 404 rather than a review against nothing.</summary>
    [Fact]
    public async Task TestCreateOnAnUnknownRiskIsNotFound()
    {
        var result = await RefusingController(new DataNotFoundException("local", "risks",
            new Exception("Risk with id 1 not found"))).Create(SampleDto(0));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateInternalError()
    {
        var result = await FailingController().Create(SampleDto(0));

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task TestUpdate()
    {
        var result = await _controller.Create(3, SampleDto(3));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var review = Assert.IsType<MgmtReview>(ok.Value);
        Assert.Equal(3, review.Id);
    }

    [Fact]
    public async Task TestUpdateUsesRouteIdInsteadOfBodyId()
    {
        var result = await _controller.Create(3, SampleDto(99));

        Assert.IsType<OkObjectResult>(result.Result);
        await _mgmtReviewsService.Received(1).UpdateAsync(Arg.Is<MgmtReviewDto>(r => r.Id == 3), 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TestUpdateBadRequestWhenIdNotPositive(int id)
    {
        var result = await _controller.Create(id, SampleDto(3));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestUpdateInternalError()
    {
        var result = await FailingController().Create(3, SampleDto(3));

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public void TestGetOne()
    {
        var result = _controller.GetOne(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var review = Assert.IsType<MgmtReview>(ok.Value);
        Assert.Equal(1, review.Id);
    }

    [Fact]
    public void TestGetOneBadRequest()
    {
        var result = _controller.GetOne(0);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetOneInternalError()
    {
        var result = FailingController().GetOne(9);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public void TestGetTypes()
    {
        var result = _controller.GetTypes();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Review>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void TestGetTypesInternalError()
    {
        var result = FailingController().GetTypes();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public void TestGetNextSteps()
    {
        var result = _controller.GetNextSteps();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<NextStep>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void TestGetNextStepsInternalError()
    {
        var result = FailingController().GetNextSteps();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }
}
