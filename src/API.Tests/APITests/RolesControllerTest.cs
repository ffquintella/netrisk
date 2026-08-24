using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

[TestSubject(typeof(RolesController))]
public class RolesControllerTest : BaseControllerTest
{
    private readonly IRolesService _rolesService = Substitute.For<IRolesService>();
    private readonly RolesController _controller;

    public RolesControllerTest()
    {
        _controller = ResolveController<RolesController>(s => s.AddSingleton(_rolesService));
    }

    private static Role MakeRole(int value, string name, bool admin = false)
    {
        return new Role { Value = value, Name = name, Admin = admin, Default = false };
    }

    #region GetAll

    [Fact]
    public void TestGetAll()
    {
        _rolesService.GetRoles().Returns(new List<Role>
        {
            MakeRole(1, "Administrator", true),
            MakeRole(2, "Analyst")
        });

        var result = _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<List<Role>>(ok.Value);
        Assert.Equal(2, roles.Count);
    }

    [Fact]
    public void TestGetAllUnexpectedError()
    {
        _rolesService.GetRoles().Returns(_ => throw new Exception("boom"));

        var result = _controller.GetAll();

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region GetRole

    [Fact]
    public void TestGetRole()
    {
        _rolesService.GetRole(1).Returns(MakeRole(1, "Administrator", true));

        var result = _controller.GetRole(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var role = Assert.IsType<Role>(ok.Value);
        Assert.Equal("Administrator", role.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TestGetRoleWithInvalidIdIsBadRequest(int roleId)
    {
        var result = _controller.GetRole(roleId);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetRoleUnexpectedError()
    {
        _rolesService.GetRole(999).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetRole(999);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region DeleteRole

    [Fact]
    public void TestDeleteRole()
    {
        var result = _controller.DeleteRole(1);

        Assert.IsType<OkResult>(result.Result);
        _rolesService.Received(1).DeleteRole(1);
    }

    [Fact]
    public void TestDeleteRoleWithInvalidIdIsBadRequest()
    {
        var result = _controller.DeleteRole(0);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestDeleteRoleUnexpectedError()
    {
        _rolesService.When(x => x.DeleteRole(999)).Do(_ => throw new Exception("boom"));

        var result = _controller.DeleteRole(999);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region CreateRole

    [Fact]
    public void TestCreateRole()
    {
        _rolesService.CreateRole(Arg.Any<Role>()).Returns(MakeRole(5, "Auditor"));

        var result = _controller.CreateRole(MakeRole(0, "Auditor"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var role = Assert.IsType<Role>(created.Value);
        Assert.Equal(5, role.Value);
        Assert.Equal("/Roles/5", created.Location);
    }

    [Fact]
    public void TestCreateRoleUnexpectedError()
    {
        _rolesService.CreateRole(Arg.Any<Role>()).Returns(_ => throw new Exception("boom"));

        var result = _controller.CreateRole(MakeRole(0, "Auditor"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region GetRolePermissions

    [Fact]
    public void TestGetRolePermissions()
    {
        _rolesService.GetRolePermissions(1).Returns(new List<string> { "risks", "assets" });

        var result = _controller.GetRolePermissions(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var permissions = Assert.IsType<List<string>>(ok.Value);
        Assert.Equal(2, permissions.Count);
    }

    [Fact]
    public void TestGetRolePermissionsWithInvalidIdIsBadRequest()
    {
        var result = _controller.GetRolePermissions(0);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetRolePermissionsUnexpectedError()
    {
        _rolesService.GetRolePermissions(999).Returns(_ => throw new Exception("boom"));

        var result = _controller.GetRolePermissions(999);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region UpdateRolePermissions

    [Fact]
    public async Task TestUpdateRolePermissions()
    {
        var result = await _controller.UpdateRolePermissions(1, new List<string> { "risks" });

        Assert.IsType<OkResult>(result);
        await _rolesService.Received(1).UpdatePermissionsAsync(1, Arg.Any<List<string>>());
    }

    [Fact]
    public async Task TestUpdateRolePermissionsWithInvalidIdIsBadRequest()
    {
        var result = await _controller.UpdateRolePermissions(0, new List<string> { "risks" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TestUpdateRolePermissionsNotFound()
    {
        _rolesService.UpdatePermissionsAsync(999, Arg.Any<List<string>>())
            .Returns(_ => throw new DataNotFoundException("roles", "999"));

        var result = await _controller.UpdateRolePermissions(999, new List<string> { "risks" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task TestUpdateRolePermissionsUnexpectedError()
    {
        _rolesService.UpdatePermissionsAsync(2, Arg.Any<List<string>>())
            .Returns(_ => throw new Exception("boom"));

        var result = await _controller.UpdateRolePermissions(2, new List<string> { "risks" });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion
}
