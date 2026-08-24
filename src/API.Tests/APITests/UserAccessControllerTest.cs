using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(UserAccessController))]
public class UserAccessControllerTest : BaseControllerTest
{
    private readonly IDalService _dalService = new InMemoryDalService(Guid.NewGuid().ToString());
    private readonly UserAccessController _controller;

    public UserAccessControllerTest()
    {
        using (var context = _dalService.GetContext())
        {
            // UserEntityRole.User/.Entity/.Role are required navigations, so GetUserEntityRoles'
            // Include chain inner-joins: without these principals the assignments below read back
            // as an empty list.
            foreach (var value in new[] { 1, 2 })
            {
                context.Users.Add(new User
                {
                    Value = value,
                    Enabled = true,
                    Name = $"user{value}",
                    Login = $"user{value}",
                    Email = $"user{value}@teste.com",
                    Type = "local",
                    Password = "secret"u8.ToArray(),
                    RoleId = 1
                });
            }

            foreach (var id in new[] { 5, 6 })
            {
                context.Entities.Add(new Entity
                {
                    Id = id, DefinitionName = "entity", DefinitionVersion = "1", Status = "active"
                });
            }

            foreach (var value in new[] { 7, 8 })
            {
                context.Roles.Add(new Role { Value = value, Name = $"role{value}" });
            }

            context.UserEntityRoles.Add(new UserEntityRole
            {
                UserId = 1, EntityId = 5, RoleId = 7,
                CreatedAt = new DateTime(2024, 1, 1), RevokedAt = null
            });
            context.UserEntityRoles.Add(new UserEntityRole
            {
                UserId = 1, EntityId = 6, RoleId = 8,
                CreatedAt = new DateTime(2024, 1, 1), RevokedAt = new DateTime(2024, 2, 1)
            });
            context.UserEntityRoles.Add(new UserEntityRole
            {
                UserId = 2, EntityId = 5, RoleId = 7,
                CreatedAt = new DateTime(2024, 1, 1), RevokedAt = null
            });
            context.SaveChanges();
        }

        _controller = ResolveController<UserAccessController>(s => s.AddSingleton(_dalService));
    }

    [Fact]
    public async Task TestGetUserEntityRolesReturnsOnlyActiveAssignments()
    {
        var result = await _controller.GetUserEntityRoles(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<UserEntityRole>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(5, list[0].EntityId);
        Assert.Equal(7, list[0].RoleId);
    }

    [Fact]
    public async Task TestGetUserEntityRolesForUserWithoutAssignments()
    {
        var result = await _controller.GetUserEntityRoles(99);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<UserEntityRole>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task TestAssignEntityRole()
    {
        var result = await _controller.AssignEntityRole(1, new AssignEntityRoleRequest
        {
            EntityId = 9, RoleId = 4
        });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var assignment = Assert.IsType<UserEntityRole>(created.Value);
        Assert.Equal(1, assignment.UserId);
        Assert.Equal(9, assignment.EntityId);
        Assert.Equal(4, assignment.RoleId);
        Assert.Null(assignment.RevokedAt);

        using var context = _dalService.GetContext();
        Assert.True(await context.UserEntityRoles.AnyAsync(a => a.EntityId == 9 && a.RoleId == 4));
    }

    [Fact]
    public async Task TestAssignEntityRoleRejectsDuplicate()
    {
        var result = await _controller.AssignEntityRole(1, new AssignEntityRoleRequest
        {
            EntityId = 5, RoleId = 7
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAssignEntityRoleAllowsReAssignmentAfterRevoke()
    {
        // The revoked (5,6,8) row must not block a fresh assignment of the same role.
        var result = await _controller.AssignEntityRole(1, new AssignEntityRoleRequest
        {
            EntityId = 6, RoleId = 8
        });

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestRevokeEntityRole()
    {
        int assignmentId;
        using (var context = _dalService.GetContext())
        {
            assignmentId = context.UserEntityRoles.First(a => a.UserId == 1 && a.EntityId == 5).Id;
        }

        var result = await _controller.RevokeEntityRole(assignmentId);

        Assert.IsType<NoContentResult>(result);

        using var verify = _dalService.GetContext();
        var revoked = await verify.UserEntityRoles.FirstAsync(a => a.Id == assignmentId);
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task TestRevokeEntityRoleNotFound()
    {
        var result = await _controller.RevokeEntityRole(9999);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
