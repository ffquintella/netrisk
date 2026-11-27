using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Model.Authentication.Scim;
using Model.DecisionCycle;
using ServerServices.Auth;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.ContextTests;

/// <summary>
/// Every write that can change what an authentication handler puts in a principal drops the principal cache, synchronously,
/// whichever service makes it: the hook is <see cref="AuditableContext.PrincipalInputsChanged"/>, wired by the production
/// <see cref="DalService"/> (here with the in-memory provider swapped in). Each test primes the cache for user 10, makes
/// one write through the real service, and requires the entry gone and the next resolve to read the new state.
/// </summary>
[TestSubject(typeof(AuditableContext))]
public class PrincipalCacheInvalidationTest
{
    private const int UserId = 10, OtherUserId = 11, RoleId = 3, SpareRoleId = 4;

    private readonly PrincipalCache _cache = new(TimeSpan.FromMinutes(5));
    private readonly PrincipalDalService _dal;
    private readonly PrincipalResolver _resolver;
    private readonly UsersService _users;
    private readonly PermissionsService _permissions;
    private readonly RolesService _roles;

    public PrincipalCacheInvalidationTest()
    {
        _dal = new PrincipalDalService(Guid.NewGuid().ToString(), new FixedHttpContextAccessor(), _cache);
        _resolver = new PrincipalResolver(_dal, _cache);
        _roles = new RolesService(Serilog.Core.Logger.None, _dal);
        _permissions = new PermissionsService(_dal, _roles);
        _users = new UsersService(_dal, NullLoggerFactory.Instance, _roles, _permissions);

        using var db = _dal.GetContext();
        var view = Permission(1, "riskmanagement");
        var hosts = Permission(2, "hosts");
        var marker = Permission(3, ThirdLineAssurance.PermissionKey);
        db.Permissions.AddRange(view, hosts, marker);
        db.Roles.AddRange(new Role { Value = RoleId, Name = "analyst", Permissions = { view, hosts } },
            new Role { Value = SpareRoleId, Name = "spare", Default = true });
        db.Users.AddRange(NewUser(UserId, RoleId, hosts), NewUser(OtherUserId, RoleId));
        db.Entities.Add(NewEntity(7));
        db.UserEntityRoles.Add(new UserEntityRole
            { Id = 1, UserId = UserId, EntityId = 7, RoleId = RoleId, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
    }

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "unit", DefinitionVersion = "1", Status = "active", Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow
    };

    private static Permission Permission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = key, Order = id };

    private static User NewUser(int id, int roleId, params Permission[] own)
    {
        var user = new User
        {
            Value = id, Login = $"u{id}", Name = $"User {id}", Email = $"u{id}@x.test", Type = "local", Enabled = true,
            RoleId = roleId, Password = [1], Salt = "s", Lang = "en"
        };
        foreach (var p in own) user.Permissions.Add(p);
        return user;
    }

    private User Row(int id = UserId)
    {
        using var db = _dal.GetContext();
        return db.Users.AsNoTracking().Single(u => u.Value == id);
    }

    /// <summary>Resolves user 10 so the cache holds them, and proves it does.</summary>
    private async Task PrimeAsync()
    {
        await _resolver.ResolveAsync(Row());
        Assert.True(_cache.TryGet(UserId, RoleId, out _));
    }

    private void AssertDropped(int roleId = RoleId) => Assert.False(_cache.TryGet(UserId, roleId, out _));

    private async Task<PrincipalSnapshot> ResolveAgainAsync() => await _resolver.ResolveAsync(Row());

    [Fact]
    public async Task TestSavingAUserDropsTheCache()
    {
        await PrimeAsync();

        var user = Row();
        user.Name = "Renamed";
        _users.SaveUser(user);

        AssertDropped();
    }

    [Fact]
    public async Task TestDisablingAUserDropsTheCache()
    {
        await PrimeAsync();

        var user = Row();
        user.Enabled = false;
        _users.SaveUser(user);

        AssertDropped();
    }

    [Fact]
    public async Task TestMovingAUserToAnotherRoleDropsTheCache()
    {
        await PrimeAsync();

        var user = Row();
        user.RoleId = SpareRoleId;
        _users.SaveUser(user);

        AssertDropped();
        Assert.Equal("spare", (await ResolveAgainAsync()).RoleName);
    }

    [Fact]
    public async Task TestCreatingAUserDropsTheCache()
    {
        await PrimeAsync();

        _users.CreateUser(NewUser(0, RoleId));

        AssertDropped();
    }

    [Fact]
    public async Task TestDeletingAUserDropsTheCache()
    {
        await PrimeAsync();

        _users.DeleteUser(OtherUserId);

        AssertDropped();
    }

    [Fact]
    public async Task TestSavingAUsersPermissionsDropsTheCache()
    {
        await PrimeAsync();

        await _permissions.SaveUserPermissionsByIdAsync(UserId, []);

        AssertDropped();
        Assert.Equal(new[] { "riskmanagement", "hosts" }, (await ResolveAgainAsync()).PermissionKeys);
    }

    [Fact]
    public async Task TestGrantingTheThirdLineMarkerDropsTheCache()
    {
        await PrimeAsync();

        await _permissions.SaveUserPermissionsByIdAsync(UserId, [2, 3]);

        AssertDropped();
        Assert.Contains(ThirdLineAssurance.PermissionKey, (await ResolveAgainAsync()).PermissionKeys);
    }

    [Fact]
    public async Task TestSavingAUserWithPermissionsDropsTheCache()
    {
        await PrimeAsync();

        await _users.SaveUserWithPermissionsAsync(Row(), [1]);

        AssertDropped();
        Assert.Equal(new[] { "riskmanagement", "hosts" }, (await ResolveAgainAsync()).PermissionKeys);
    }

    [Fact]
    public async Task TestSavingARolesPermissionsDropsTheCache()
    {
        await PrimeAsync();

        await _roles.UpdatePermissionsAsync(RoleId, ["riskmanagement"]);

        AssertDropped();
        // hosts is still the user's own grant, after the role's remaining permission.
        Assert.Equal(new[] { "riskmanagement", "hosts" }, (await ResolveAgainAsync()).PermissionKeys);
    }

    [Fact]
    public async Task TestRevokingAPermissionFromTheRoleIsSeenOnTheNextResolve()
    {
        await PrimeAsync();

        await _roles.UpdatePermissionsAsync(RoleId, ["hosts"]);

        Assert.DoesNotContain("riskmanagement", (await ResolveAgainAsync()).PermissionKeys);
    }

    [Fact]
    public async Task TestCreatingARoleDropsTheCache()
    {
        await PrimeAsync();

        _roles.CreateRole(new Role { Name = "new role" });

        AssertDropped();
    }

    [Fact]
    public async Task TestDeletingARoleDropsTheCache()
    {
        await PrimeAsync();

        _roles.DeleteRole(SpareRoleId);

        AssertDropped();
    }

    [Fact]
    public async Task TestRenamingARoleDropsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            (await db.Roles.SingleAsync(r => r.Value == RoleId)).Name = "renamed";
            await db.SaveChangesAsync();
        }

        AssertDropped();
        Assert.Equal("renamed", (await ResolveAgainAsync()).RoleName);
    }

    [Fact]
    public async Task TestGrantingAnEntityAssignmentDropsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            db.Entities.Add(NewEntity(8));
            db.UserEntityRoles.Add(new UserEntityRole
                { Id = 2, UserId = UserId, EntityId = 8, RoleId = RoleId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        AssertDropped();
        Assert.Equal(new[] { 7, 8 }, (await ResolveAgainAsync()).EntityIds);
    }

    [Fact]
    public async Task TestRevokingAnEntityAssignmentDropsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            (await db.UserEntityRoles.SingleAsync(a => a.Id == 1)).RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        AssertDropped();
        Assert.Empty((await ResolveAgainAsync()).EntityIds);
    }

    [Fact]
    public async Task TestRenamingAPermissionKeyDropsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            (await db.Permissions.SingleAsync(p => p.Id == 2)).Key = "hosts_renamed";
            await db.SaveChangesAsync();
        }

        AssertDropped();
    }

    [Fact]
    public async Task TestDeletingAnEntityDropsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            db.UserEntityRoles.RemoveRange(db.UserEntityRoles);
            db.Entities.Remove(await db.Entities.SingleAsync(e => e.Id == 7));
            await db.SaveChangesAsync();
        }

        AssertDropped();
    }

    /// <summary>A new entity carries no assignment yet, so it changes no principal.</summary>
    [Fact]
    public async Task TestAddingAnEntityKeepsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            db.Entities.Add(NewEntity(8));
            await db.SaveChangesAsync();
        }

        Assert.True(_cache.TryGet(UserId, RoleId, out _));
    }

    [Fact]
    public async Task TestScimDeprovisioningDropsTheCache()
    {
        await PrimeAsync();

        await new ScimService(Serilog.Core.Logger.None, _dal).ReplaceUserAsync(UserId.ToString(),
            new ScimUser { UserName = $"u{UserId}", Active = false });

        AssertDropped();
    }

    [Fact]
    public async Task TestScimProvisioningANewUserDropsTheCache()
    {
        await PrimeAsync();

        await new ScimService(Serilog.Core.Logger.None, _dal).CreateUserAsync(
            new ScimUser { UserName = "provisioned", Active = true });

        AssertDropped();
    }

    /// <summary>Every basic sign-in writes the login time: that alone feeds no claim and must not empty the cache.</summary>
    [Fact]
    public async Task TestRecordingALoginKeepsTheCache()
    {
        await PrimeAsync();

        await _users.RegisterLoginAsync(UserId, "127.0.0.1");

        Assert.True(_cache.TryGet(UserId, RoleId, out _));
    }

    [Fact]
    public async Task TestAnUnrelatedSaveKeepsTheCache()
    {
        await PrimeAsync();

        await using (var db = _dal.GetContext())
        {
            db.Settings.Add(new Setting { Name = "unrelated", Value = "1" });
            await db.SaveChangesAsync();
        }

        Assert.True(_cache.TryGet(UserId, RoleId, out _));
    }
}
