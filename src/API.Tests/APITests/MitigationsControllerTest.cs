using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(MitigationsController))]
public class MitigationsControllerTest : BaseControllerTest
{
    private readonly IMitigationsService _mitigationsService = Substitute.For<IMitigationsService>();
    private readonly ITeamsService _teamsService = Substitute.For<ITeamsService>();
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IRisksService _risksService = Substitute.For<IRisksService>();

    private readonly MitigationsController _controller;

    public MitigationsControllerTest()
    {
        _controller = Build(_mitigationsService, _teamsService, _filesService, _risksService);
    }

    private static MitigationsController Build(
        IMitigationsService mitigations,
        ITeamsService teams,
        IFilesService files,
        IRisksService risks)
    {
        return ResolveController<MitigationsController>(s =>
        {
            s.AddSingleton(mitigations);
            s.AddSingleton(teams);
            s.AddSingleton(files);
            s.AddSingleton(risks);
        });
    }

    /// <summary>
    /// A controller over fresh doubles, for branches that need a different outcome from the same
    /// call the shared doubles already answer.
    /// </summary>
    private static (MitigationsController Controller,
        IMitigationsService Mitigations,
        ITeamsService Teams,
        IFilesService Files) NewController()
    {
        var mitigations = Substitute.For<IMitigationsService>();
        var teams = Substitute.For<ITeamsService>();
        var files = Substitute.For<IFilesService>();
        var risks = Substitute.For<IRisksService>();
        return (Build(mitigations, teams, files, risks), mitigations, teams, files);
    }

    private static MitigationDto SampleDto(int id = 1, int riskId = 10)
    {
        return new MitigationDto
        {
            Id = id,
            RiskId = riskId,
            SubmissionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdate = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            PlanningStrategy = 1,
            MitigationEffort = 2,
            MitigationCost = 3,
            MitigationOwner = 4,
            CurrentSolution = "solution",
            SecurityRequirements = "requirements",
            SecurityRecommendations = "recommendations",
            SubmittedBy = 1,
            PlanningDate = new DateOnly(2024, 6, 1),
            MitigationPercent = 25
        };
    }

    private static Mitigation SampleMitigation(int id = 1, int riskId = 10)
    {
        return new Mitigation
        {
            Id = id,
            RiskId = riskId,
            PlanningStrategy = 1,
            MitigationEffort = 2,
            MitigationCost = 3,
            MitigationOwner = 4,
            CurrentSolution = "solution",
            SecurityRequirements = "requirements",
            SecurityRecommendations = "recommendations",
            SubmittedBy = 1,
            MitigationPercent = 25
        };
    }

    #region Create

    [Fact]
    public void TestCreate()
    {
        _mitigationsService.Create(Arg.Any<Mitigation>()).Returns(SampleMitigation(7));

        var result = _controller.Create(SampleDto());

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(MitigationsController.GetById), created.ActionName);
        var mitigation = Assert.IsType<Mitigation>(created.Value);
        Assert.Equal(7, mitigation.Id);
    }

    [Fact]
    public void TestCreateNotFound()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.Create(Arg.Any<Mitigation>())
            .Returns(_ => throw new DataNotFoundException("risks", "10"));

        var result = controller.Create(SampleDto());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestCreateInternalErrorReturnsBadRequest()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.Create(Arg.Any<Mitigation>()).Returns(_ => throw new Exception("boom"));

        var result = controller.Create(SampleDto());

        Assert.IsType<BadRequestResult>(result.Result);
    }

    #endregion

    #region GetById

    [Fact]
    public void TestGetById()
    {
        _mitigationsService.GetById(1).Returns(SampleMitigation(1, 10));

        var result = _controller.GetById(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var mitigation = Assert.IsType<Mitigation>(ok.Value);
        Assert.Equal(1, mitigation.Id);
        Assert.Equal(10, mitigation.RiskId);
    }

    [Fact]
    public void TestGetByIdNotFound()
    {
        _mitigationsService.GetById(999).Returns(_ => throw new DataNotFoundException("mitigations", "999"));

        var result = _controller.GetById(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetByIdInternalError()
    {
        _mitigationsService.GetById(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetById(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region UpdateById

    [Fact]
    public void TestUpdateById()
    {
        var result = _controller.UpdateById(3, SampleDto(1));

        Assert.IsType<OkResult>(result);
        _mitigationsService.Received(1).Save(Arg.Is<Mitigation>(m => m.Id == 3));
    }

    [Fact]
    public void TestUpdateByIdNotFound()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.When(x => x.Save(Arg.Any<Mitigation>()))
            .Do(_ => throw new DataNotFoundException("mitigations", "999"));

        var result = controller.UpdateById(999, SampleDto());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestUpdateByIdInternalError()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.When(x => x.Save(Arg.Any<Mitigation>())).Do(_ => throw new Exception("boom"));

        var result = controller.UpdateById(1, SampleDto());

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
        Assert.Equal("boom", status.Value);
    }

    #endregion

    #region GetFiles

    [Fact]
    public void TestGetFiles()
    {
        _filesService.GetMitigationFiles(1).Returns(new List<FileListing>
        {
            new() { Name = "one.pdf", UniqueName = "one-1.pdf", OwnerId = 1 },
            new() { Name = "two.pdf", UniqueName = "two-2.pdf", OwnerId = 1 }
        });

        var result = _controller.GetFiles(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsType<List<FileListing>>(ok.Value);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void TestGetFilesInternalError()
    {
        _filesService.GetMitigationFiles(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetFiles(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region GetTeamsById

    [Fact]
    public void TestGetTeamsById()
    {
        _teamsService.GetByMitigationId(1).Returns(new List<Team>
        {
            new() { Value = 1, Name = "Team 1" },
            new() { Value = 2, Name = "Team 2" }
        });

        var result = _controller.GetTeamsById(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var teams = Assert.IsType<List<Team>>(ok.Value);
        Assert.Equal(2, teams.Count);
    }

    [Fact]
    public void TestGetTeamsByIdNotFound()
    {
        _teamsService.GetByMitigationId(999)
            .Returns(_ => throw new DataNotFoundException("teams", "999"));

        var result = _controller.GetTeamsById(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void TestGetTeamsByIdInternalError()
    {
        _teamsService.GetByMitigationId(500).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetTeamsById(500);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region AssociateTeamToMitigation

    [Fact]
    public void TestAssociateTeamToMitigation()
    {
        var result = _controller.AssociateTeamToMitigation(1, 2);

        Assert.IsType<OkResult>(result);
        _teamsService.Received(1).AssociateTeamToMitigation(1, 2);
    }

    [Fact]
    public void TestAssociateTeamToMitigationNotFound()
    {
        _teamsService.When(x => x.AssociateTeamToMitigation(999, 2))
            .Do(_ => throw new DataNotFoundException("mitigations", "999"));

        var result = _controller.AssociateTeamToMitigation(999, 2);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestAssociateTeamToMitigationInternalError()
    {
        _teamsService.When(x => x.AssociateTeamToMitigation(500, 2))
            .Do(_ => throw new Exception("boom"));

        var result = _controller.AssociateTeamToMitigation(500, 2);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region DeleteTeamsAssociations

    [Fact]
    public void TestDeleteTeamsAssociations()
    {
        var result = _controller.AssociateTeamToMitigation(1);

        Assert.IsType<OkResult>(result);
        _mitigationsService.Received(1).DeleteTeamsAssociations(1);
    }

    [Fact]
    public void TestDeleteTeamsAssociationsNotFound()
    {
        _mitigationsService.When(x => x.DeleteTeamsAssociations(999))
            .Do(_ => throw new DataNotFoundException("mitigations", "999"));

        var result = _controller.AssociateTeamToMitigation(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void TestDeleteTeamsAssociationsInternalError()
    {
        _mitigationsService.When(x => x.DeleteTeamsAssociations(500))
            .Do(_ => throw new Exception("boom"));

        var result = _controller.AssociateTeamToMitigation(500);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion

    #region Lists

    [Fact]
    public void TestListMitigationStrategies()
    {
        _mitigationsService.ListStrategies().Returns(new List<PlanningStrategy>
        {
            new() { Value = 1, Name = "Avoid" },
            new() { Value = 2, Name = "Mitigate" }
        });

        var result = _controller.ListMitigationStrategies();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var strategies = Assert.IsType<List<PlanningStrategy>>(ok.Value);
        Assert.Equal(2, strategies.Count);
    }

    [Fact]
    public void TestListMitigationStrategiesInternalError()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.ListStrategies().Returns(_ => throw new Exception("boom"));

        var result = controller.ListMitigationStrategies();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public void TestListMitigationEffort()
    {
        _mitigationsService.ListEfforts().Returns(new List<MitigationEffort>
        {
            new() { Value = 1, Name = "Low" }
        });

        var result = _controller.ListMitigationEffort();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var efforts = Assert.IsType<List<MitigationEffort>>(ok.Value);
        Assert.Single(efforts);
    }

    [Fact]
    public void TestListMitigationEffortInternalError()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.ListEfforts().Returns(_ => throw new Exception("boom"));

        var result = controller.ListMitigationEffort();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public void TestListMitigationCosts()
    {
        _mitigationsService.ListCosts().Returns(new List<MitigationCost>
        {
            new() { Value = 1, Name = "Cheap" },
            new() { Value = 2, Name = "Expensive" }
        });

        var result = _controller.ListMitigationCosts();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var costs = Assert.IsType<List<MitigationCost>>(ok.Value);
        Assert.Equal(2, costs.Count);
    }

    [Fact]
    public void TestListMitigationCostsInternalError()
    {
        var (controller, mitigations, _, _) = NewController();
        mitigations.ListCosts().Returns(_ => throw new Exception("boom"));

        var result = controller.ListMitigationCosts();

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    #endregion
}
