using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

internal static class AccessFixtures
{
    public static Permission NewPermission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = "" };

    public static User NewUser(int value, int roleId = 0) => new()
    {
        Value = value, Name = $"U{value}", Type = "local", Enabled = true, RoleId = roleId,
        Email = "u@x.io", Password = new byte[] { 1 }
    };
}

public class RolesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRolesService _svc;
    public RolesServiceInMemoryTest() => _svc = GetService<IRolesService>();

    [Fact]
    public void TestCreateGetDeleteRole()
    {
        var created = _svc.CreateRole(new Role { Name = "Admin", Admin = true });
        Assert.True(created.Value > 0);

        Assert.Single(_svc.GetRoles());
        Assert.NotNull(_svc.GetRole(created.Value));

        _svc.DeleteRole(created.Value);
        Assert.Empty(_svc.GetRoles());
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteRole(created.Value));
    }

    [Fact]
    public void TestGetRoleNotFound()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.GetRole(999));
        Assert.Throws<Exception>(() => _svc.GetRolePermissions(999));
    }

    [Fact]
    public async Task TestUpdateAndGetPermissions()
    {
        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "R", Admin = false });
            ctx.Permissions.Add(AccessFixtures.NewPermission(1, "read"));
            ctx.Permissions.Add(AccessFixtures.NewPermission(2, "write"));
        });

        await _svc.UpdatePermissionsAsync(1, new List<string> { "read", "write" });

        var permissions = await _svc.GetRolePermissionsAsync(1);
        Assert.Equal(2, permissions.Count);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.UpdatePermissionsAsync(99, new List<string>()));
    }
}

public class PermissionsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IPermissionsService _svc;
    public PermissionsServiceInMemoryTest() => _svc = GetService<IPermissionsService>();

    [Fact]
    public void TestGetAllAndGetByKey()
    {
        Seed(ctx =>
        {
            ctx.Permissions.Add(AccessFixtures.NewPermission(1, "submit_risks"));
            ctx.Permissions.Add(AccessFixtures.NewPermission(2, "reports"));
        });

        Assert.Equal(2, _svc.GetAllPermissions().Count);
        Assert.Equal("reports", _svc.GetByKey("reports").Key);
        Assert.Equal(2, _svc.GetDefaultPermissions().Count);
        Assert.Throws<DataNotFoundException>(() => _svc.GetByKey("missing"));
    }

    [Fact]
    public void TestUserPermissions()
    {
        Seed(ctx =>
        {
            var user = AccessFixtures.NewUser(1);
            user.Permissions.Add(AccessFixtures.NewPermission(1, "do_x"));
            ctx.Users.Add(user);
        });

        Assert.Single(_svc.GetUserPermissionsById(1));
        Assert.True(_svc.UserHasPermission(AccessFixtures.NewUser(1), "do_x"));
        Assert.False(_svc.UserHasPermission(AccessFixtures.NewUser(1), "do_y"));
        Assert.Throws<DataNotFoundException>(() => _svc.GetUserPermissionsById(999));
    }

    [Fact]
    public void TestUserPermissionsIncludesRolePermissions()
    {
        Seed(ctx =>
        {
            var role = new Role { Value = 5, Name = "R", Admin = false };
            role.Permissions.Add(AccessFixtures.NewPermission(1, "role_perm"));
            ctx.Roles.Add(role);
            ctx.Users.Add(AccessFixtures.NewUser(1, roleId: 5));
        });

        var permissions = _svc.GetUserPermissions(AccessFixtures.NewUser(1, roleId: 5));

        Assert.Contains("role_perm", permissions);
    }

    [Fact]
    public async Task TestSaveUserPermissions()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(AccessFixtures.NewUser(1));
            ctx.Permissions.Add(AccessFixtures.NewPermission(1, "p1"));
            ctx.Permissions.Add(AccessFixtures.NewPermission(2, "p2"));
        });

        await _svc.SaveUserPermissionsByIdAsync(1, new List<int> { 1, 2 });

        Assert.Equal(2, _svc.GetUserPermissionsById(1).Count);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveUserPermissionsByIdAsync(999, new List<int>()));
    }
}

public class MessagesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IMessagesService _svc;
    public MessagesServiceInMemoryTest() => _svc = GetService<IMessagesService>();

    [Fact]
    public async Task TestSendAndGetMessage()
    {
        await _svc.SendMessageAsync("hello", userId: 1);

        using var ctx = OpenContext();
        var id = ctx.Messages.First().Id;

        var msg = await _svc.GetMessageAsync(id);
        Assert.Equal("hello", msg.Message1);
        await Assert.ThrowsAsync<Exception>(() => _svc.GetMessageAsync(9999));
    }

    [Fact]
    public async Task TestGetAllAndUnread()
    {
        await _svc.SendMessageAsync("m1", userId: 1, chatId: 7);
        await _svc.SendMessageAsync("m2", userId: 1, chatId: 7);
        await _svc.SendMessageAsync("other", userId: 2);

        Assert.Equal(2, (await _svc.GetAllAsync(1, null)).Count);
        Assert.Equal(2, (await _svc.GetAllAsync(1, new List<int?> { 7 })).Count);
        Assert.True(await _svc.HasUnreadMessagesAsync(1));
        Assert.False(await _svc.HasUnreadMessagesAsync(99));
    }

    [Fact]
    public async Task TestSaveAndDeleteMessage()
    {
        await _svc.SendMessageAsync("orig", userId: 1);
        int id;
        using (var ctx = OpenContext()) id = ctx.Messages.First().Id;

        var msg = await _svc.GetMessageAsync(id);
        msg.Message1 = "edited";
        await _svc.SaveMessageAsync(msg);
        Assert.Equal("edited", (await _svc.GetMessageAsync(id)).Message1);

        await _svc.DeleteMessageAsync(id);
        await Assert.ThrowsAsync<Exception>(() => _svc.GetMessageAsync(id));
        await Assert.ThrowsAsync<Exception>(() => _svc.SaveMessageAsync(msg));
    }
}

public class ClientRegistrationServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IClientRegistrationService _svc;
    public ClientRegistrationServiceInMemoryTest() => _svc = GetService<IClientRegistrationService>();

    private static ClientRegistration NewReg(int id, string ext = "ext", string status = "requested") => new()
    {
        Id = id, ExternalId = ext, Status = status,
        RegistrationDate = new DateTime(2026, 1, 1), LastVerificationDate = new DateTime(2026, 1, 1)
    };

    [Fact]
    public void TestAddAndGetAll()
    {
        Assert.Equal(0, _svc.Add(NewReg(0, "c1")));
        Assert.Equal(1, _svc.Add(NewReg(0, "c1")));   // duplicate external id

        Assert.Single(_svc.GetAll());
        Assert.Single(_svc.GetRequested());
    }

    [Fact]
    public void TestApproveRejectFlow()
    {
        Seed(ctx => ctx.ClientRegistrations.Add(NewReg(1, "c1")));

        Assert.Equal(0, _svc.Approve(1));
        Assert.Equal(2, _svc.Approve(1));   // already approved
        Assert.Equal(1, _svc.Approve(999)); // not found

        Assert.Equal(1, _svc.IsAccepted("c1"));  // approved → authorized
        Assert.Equal(-1, _svc.IsAccepted("nope"));
    }

    [Fact]
    public void TestRejectFlow()
    {
        Seed(ctx => ctx.ClientRegistrations.Add(NewReg(1, "c1")));

        Assert.Equal(0, _svc.Reject(1));
        Assert.Equal(2, _svc.Reject(1));    // already rejected
        Assert.Equal(1, _svc.Reject(999));
        Assert.Equal(0, _svc.IsAccepted("c1")); // not approved → not authorized
    }

    [Fact]
    public async Task TestGetByIdUpdateDeleteAndFindApproved()
    {
        Seed(ctx => ctx.ClientRegistrations.Add(NewReg(1, "c1", "approved")));

        Assert.NotNull(_svc.GetRegistrationById(1));
        Assert.NotNull(await _svc.FindApprovedRegistrationAsync("c1"));

        var reg = _svc.GetRegistrationById(1)!;
        reg.Status = "requested";
        Assert.Equal(0, _svc.Update(reg));

        Assert.Equal(0, _svc.DeleteById(1));
        Assert.Equal(1, _svc.DeleteById(1));   // already gone
    }

    [Fact]
    public void TestDeleteEntity()
    {
        Seed(ctx => ctx.ClientRegistrations.Add(NewReg(1, "c1")));

        var reg = _svc.GetRegistrationById(1)!;
        Assert.Equal(0, _svc.Delete(reg));
        Assert.Empty(_svc.GetAll());
    }
}
