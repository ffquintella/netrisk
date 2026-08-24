using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(TeamsController))]
public class TeamsControllerTest : BaseControllerTest
{
    private readonly ITeamsService _teamsService = Substitute.For<ITeamsService>();
    private readonly TeamsController _controller;

    public TeamsControllerTest()
    {
        _controller = ResolveController<TeamsController>(s => s.AddSingleton(_teamsService));
    }

    private static Team MakeTeam(int value, string name)
    {
        return new Team { Value = value, Name = name };
    }

    #region GetAll

    [Fact]
    public void TestGetAll()
    {
        _teamsService.GetAll().Returns(new List<Team> { MakeTeam(1, "Blue"), MakeTeam(2, "Red") });

        var result = _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var teams = Assert.IsType<List<Team>>(ok.Value);
        Assert.Equal(2, teams.Count);
    }

    [Fact]
    public void TestGetAllUnexpectedError()
    {
        _teamsService.GetAll().Returns(_ => throw new Exception("boom"));

        var result = _controller.GetAll();

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region GetTeamUsersIds

    [Fact]
    public void TestGetTeamUsersIds()
    {
        _teamsService.GetUsersIds(1).Returns(new List<int> { 10, 11, 12 });

        var result = _controller.GetTeamUsersIds(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var userIds = Assert.IsType<List<int>>(ok.Value);
        Assert.Equal(3, userIds.Count);
    }

    [Fact]
    public void TestGetTeamUsersIdsNotFound()
    {
        _teamsService.GetUsersIds(999).Returns(_ => throw new DataNotFoundException("teams", "999"));

        var result = _controller.GetTeamUsersIds(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetTeamUsersIdsUnexpectedError()
    {
        _teamsService.GetUsersIds(2).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetTeamUsersIds(2);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region GetBy

    [Fact]
    public void TestGetBy()
    {
        _teamsService.GetById(1).Returns(MakeTeam(1, "Blue"));

        var result = _controller.GetBy(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var team = Assert.IsType<Team>(ok.Value);
        Assert.Equal("Blue", team.Name);
    }

    [Fact]
    public void TestGetByNotFound()
    {
        _teamsService.GetById(999).Returns(_ => throw new DataNotFoundException("teams", "999"));

        var result = _controller.GetBy(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetByUnexpectedError()
    {
        _teamsService.GetById(2).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetBy(2);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region UpdateTeamUsers

    [Fact]
    public void TestUpdateTeamUsers()
    {
        var result = _controller.GetTeamUsersIds(1, new List<int> { 10, 11 });

        Assert.IsType<OkResult>(result.Result);
        _teamsService.Received(1).UpdateTeamUsers(1, Arg.Any<List<int>>());
    }

    [Fact]
    public void TestUpdateTeamUsersNotFound()
    {
        _teamsService.When(x => x.UpdateTeamUsers(999, Arg.Any<List<int>>()))
            .Do(_ => throw new DataNotFoundException("teams", "999"));

        var result = _controller.GetTeamUsersIds(999, new List<int> { 10 });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestUpdateTeamUsersUnexpectedError()
    {
        _teamsService.When(x => x.UpdateTeamUsers(2, Arg.Any<List<int>>()))
            .Do(_ => throw new Exception("boom"));

        var result = _controller.GetTeamUsersIds(2, new List<int> { 10 });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region Create

    [Fact]
    public void TestCreateTeam()
    {
        _teamsService.Create(Arg.Any<Team>()).Returns(MakeTeam(7, "Green"));

        var result = _controller.GetTeamUsersIds(MakeTeam(0, "Green"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var team = Assert.IsType<Team>(created.Value);
        Assert.Equal(7, team.Value);
        Assert.Equal("Teams/7", created.Location);
    }

    [Fact]
    public void TestCreateTeamUnexpectedError()
    {
        _teamsService.Create(Arg.Any<Team>()).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetTeamUsersIds(MakeTeam(0, "Green"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region Delete

    [Fact]
    public void TestDelete()
    {
        var result = _controller.Delete(1);

        Assert.IsType<OkResult>(result);
        _teamsService.Received(1).Delete(1);
    }

    [Fact]
    public void TestDeleteNotFound()
    {
        _teamsService.When(x => x.Delete(999)).Do(_ => throw new DataNotFoundException("teams", "999"));

        var result = _controller.Delete(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void TestDeleteUnexpectedError()
    {
        _teamsService.When(x => x.Delete(2)).Do(_ => throw new Exception("boom"));

        var result = _controller.Delete(2);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion
}
