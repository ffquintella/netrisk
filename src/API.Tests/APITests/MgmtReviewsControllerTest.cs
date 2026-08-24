using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
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
        _mgmtReviewsService.Create(Arg.Any<MgmtReview>()).Returns(SampleReview(7));
        _mgmtReviewsService.Update(Arg.Any<MgmtReviewDto>()).Returns(SampleReview(3));
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

    /// <summary>Builds a controller over a service that fails, to drive the catch blocks.</summary>
    private static MgmtReviewsController FailingController()
    {
        var failing = Substitute.For<IMgmtReviewsService>();
        failing.Create(Arg.Any<MgmtReview>()).Returns(_ => throw new InvalidOperationException("boom"));
        failing.Update(Arg.Any<MgmtReviewDto>()).Returns(_ => throw new InvalidOperationException("boom"));
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
    public void TestCreate()
    {
        var result = _controller.Create(SampleDto(0));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var review = Assert.IsType<MgmtReview>(created.Value);
        Assert.Equal(7, review.Id);
    }

    [Fact]
    public void TestCreateResetsSuppliedId()
    {
        var dto = SampleDto(42);

        var result = _controller.Create(dto);

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(0, dto.Id);
        // The authenticated user becomes the reviewer.
        Assert.Equal(1, dto.Reviewer);
    }

    [Fact]
    public void TestCreateInternalError()
    {
        var result = FailingController().Create(SampleDto(0));

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public void TestUpdate()
    {
        var result = _controller.Create(3, SampleDto(3));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var review = Assert.IsType<MgmtReview>(ok.Value);
        Assert.Equal(3, review.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TestUpdateBadRequestWhenIdNotPositive(int id)
    {
        var result = _controller.Create(1, SampleDto(id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestUpdateInternalError()
    {
        var result = FailingController().Create(3, SampleDto(3));

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
