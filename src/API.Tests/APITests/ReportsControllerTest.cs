using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.EntitiesDto;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(ReportsController))]
public class ReportsControllerTest : BaseControllerTest
{
    private readonly IReportsService _reportsService = Substitute.For<IReportsService>();
    private readonly ReportsController _controller;

    public ReportsControllerTest()
    {
        _reportsService.GetAll().Returns(new List<Report>
        {
            new()
            {
                Id = 1,
                Name = "first report",
                CreatorId = 1,
                CreationDate = new DateTime(2024, 1, 1),
                Type = 1,
                Status = 1
            },
            new()
            {
                Id = 2,
                Name = "second report",
                CreatorId = 1,
                CreationDate = new DateTime(2024, 2, 1),
                Type = 2,
                Status = 1
            }
        });

        _reportsService.CreateAsync(Arg.Any<Report>(), Arg.Any<User>()).Returns(new Report
        {
            Id = 10,
            Name = "created report",
            CreatorId = 1,
            CreationDate = new DateTime(2024, 3, 1),
            Type = 1,
            Status = 1
        });

        _reportsService.CreateAsync(Arg.Is<Report>(r => r.Name == "boom"), Arg.Any<User>())
            .Returns<Task<Report>>(_ => throw new Exception("report engine offline"));

        _reportsService.When(x => x.Delete(999))
            .Do(_ => throw new DataNotFoundException("reports", "999"));

        _reportsService.When(x => x.Delete(500))
            .Do(_ => throw new Exception("report engine offline"));

        _controller = ResolveController<ReportsController>(s => s.AddSingleton(_reportsService));
    }

    [Fact]
    public void TestGet()
    {
        var result = _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var reports = Assert.IsType<List<Report>>(ok.Value);
        Assert.Equal(2, reports.Count);
        Assert.Equal("first report", reports[0].Name);
    }

    [Fact]
    public async Task TestCreate()
    {
        var result = await _controller.Create(new ReportDto { Name = "created report", Type = 1 });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var report = Assert.IsType<Report>(created.Value);
        Assert.Equal(10, report.Id);
        Assert.Equal("Reports/10", created.Location);
    }

    [Fact]
    public async Task TestCreateWithAlternateLocalization()
    {
        var result = await _controller.Create(new ReportDto { Name = "created report", Type = 1 }, "en-US");

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateWithInvalidLocalizationReturnsBadRequest()
    {
        var result = await _controller.Create(new ReportDto { Name = "created report", Type = 1 }, "xx-XX");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Invalid localization", badRequest.Value);
    }

    [Fact]
    public async Task TestCreateWhenServiceFailsReturnsInternalServerError()
    {
        var result = await _controller.Create(new ReportDto { Name = "boom", Type = 1 });

        var statusCode = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, statusCode.StatusCode);
    }

    [Fact]
    public void TestDelete()
    {
        var result = _controller.Delete(1);

        Assert.IsType<OkResult>(result.Result);
        _reportsService.Received(1).Delete(1);
    }

    [Fact]
    public void TestDeleteUnknownReportReturnsNotFound()
    {
        var result = _controller.Delete(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Data of type reports not found by id 999", notFound.Value);
    }

    [Fact]
    public void TestDeleteWhenServiceFailsReturnsInternalServerError()
    {
        var result = _controller.Delete(500);

        var statusCode = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, statusCode.StatusCode);
    }
}
