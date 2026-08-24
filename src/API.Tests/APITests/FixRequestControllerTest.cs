using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.DI;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(FixRequestController))]
public class FixRequestControllerTest : BaseControllerTest
{
    private readonly IFixRequestsService _fixRequestsService = Substitute.For<IFixRequestsService>();
    private readonly ICommentsService _commentsService = Substitute.For<ICommentsService>();
    private readonly FixRequestController _controller;

    private static FixRequest SampleFixRequest(int id) => new()
    {
        Id = id,
        VulnerabilityId = 10,
        Identifier = "FR-" + id,
        CreationDate = new DateTime(2024, 1, 1),
        Status = 1,
        Comments = new List<Comment>()
    };

    public FixRequestControllerTest()
    {
        _fixRequestsService.GetAllFixRequestAsync().Returns(new List<FixRequest>
        {
            SampleFixRequest(1),
            SampleFixRequest(2)
        });

        _fixRequestsService.GetVulnerabilitiesFixRequestAsync(Arg.Any<List<int>>())
            .Returns(new List<FixRequest> { SampleFixRequest(1) });

        _fixRequestsService.CreateFixRequestAsync(Arg.Any<FixRequest>()).Returns(SampleFixRequest(5));
        _fixRequestsService.SaveFixRequestAsync(Arg.Any<FixRequest>()).Returns(SampleFixRequest(5));

        _controller = ResolveController<FixRequestController>(s =>
        {
            s.AddSingleton(_fixRequestsService);
            s.AddSingleton(_commentsService);
        });
    }

    private static FixRequestController FailingController()
    {
        var failing = Substitute.For<IFixRequestsService>();
        failing.GetAllFixRequestAsync().Returns<Task<List<FixRequest>>>(_ => throw new InvalidOperationException("boom"));
        failing.GetVulnerabilitiesFixRequestAsync(Arg.Any<List<int>>())
            .Returns<Task<List<FixRequest>>>(_ => throw new InvalidOperationException("boom"));
        failing.CreateFixRequestAsync(Arg.Any<FixRequest>())
            .Returns<Task<FixRequest>>(_ => throw new InvalidOperationException("boom"));

        return ResolveController<FixRequestController>(s =>
        {
            s.AddSingleton(failing);
            s.AddSingleton(Substitute.For<ICommentsService>());
        });
    }

    [Fact]
    public async Task TestGetAll()
    {
        var result = await _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<FixRequest>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetAllRejectsNonAdmin()
    {
        // The shared IUsersService hands back a fresh admin User per provider, so demoting it here
        // only affects this controller instance.
        var provider = ServiceRegistration.GetServiceProvider(s =>
        {
            s.AddSingleton(_fixRequestsService);
            s.AddSingleton(_commentsService);
        });
        provider.GetRequiredService<IUsersService>().GetUser("testUser").Admin = false;
        var controller = provider.GetRequiredService<FixRequestController>();

        var result = await controller.GetAll();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestGetAllInternalError()
    {
        var result = await FailingController().GetAll();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task TestGetByFixRequestsbyVulnerabilities()
    {
        var result = await _controller.GetByFixRequestsbyVulnerabilities(new List<int> { 10, 11 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<FixRequest>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task TestGetByFixRequestsbyVulnerabilitiesInternalError()
    {
        var result = await FailingController().GetByFixRequestsbyVulnerabilities(new List<int> { 10 });

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task TestCreate()
    {
        var dto = new FixRequestDto
        {
            VulnerabilityId = 10,
            Comments = "please fix",
            Destination = "someone@teste.com",
            FixTeamId = null,
            Identifier = "FR-5"
        };

        var result = await _controller.Create(dto);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var request = Assert.IsType<FixRequest>(ok.Value);
        Assert.Equal(5, request.Id);
        Assert.Single(request.Comments);
        await _fixRequestsService.Received(1).SaveFixRequestAsync(Arg.Any<FixRequest>());
    }

    [Fact]
    public async Task TestCreateForGroup()
    {
        var dto = new FixRequestDto
        {
            VulnerabilityId = 10,
            Comments = "team please fix",
            Destination = "",
            FixTeamId = 3,
            Identifier = "FR-6"
        };

        var result = await _controller.Create(dto, true);

        Assert.IsType<OkObjectResult>(result.Result);
        await _fixRequestsService.Received().CreateFixRequestAsync(
            Arg.Is<FixRequest>(f => f.IsTeamFix == true && f.FixTeamId == 3));
    }

    [Fact]
    public async Task TestCreateInternalError()
    {
        var dto = new FixRequestDto { VulnerabilityId = 10, Identifier = "FR-7" };

        var result = await FailingController().Create(dto);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }
}
