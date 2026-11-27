using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Saving a user's or a role's permissions changes only what changed: an unchanged grant writes nothing, so the grant record
/// (<c>PermissionGrantRecorder</c>) holds the real grants and revocations only. And the desktop's save of a user with their
/// permissions is one save — one transaction — that keeps every effect of the two saves it replaces.
/// </summary>
[TestSubject(typeof(PermissionsService))]
public class PermissionDiffInMemoryTest : InMemoryServiceTestBase
{
    private const int UserId = 10, RoleId = 3, OtherRoleId = 4;
    private const int Approve = 1, Configure = 2, Plain = 3, View = 4;

    private readonly IPermissionsService _permissions;
    private readonly IRolesService _roles;
    private readonly IUsersService _users;

    public PermissionDiffInMemoryTest()
    {
        _permissions = GetService<IPermissionsService>();
        _roles = GetService<IRolesService>();
        _users = GetService<IUsersService>();

        Seed(db =>
        {
            var approve = Permission(Approve, RiskIndexPermissions.Approve);
            var configure = Permission(Configure, RiskIndexPermissions.Configure);
            var plain = Permission(Plain, "riskmanagement");
            var view = Permission(View, RiskIndexPermissions.View);
            db.Permissions.AddRange(approve, configure, plain, view);
            db.Roles.Add(new Role { Value = RoleId, Name = "approvers", Permissions = { approve, plain } });
            db.Roles.Add(new Role { Value = OtherRoleId, Name = "viewers", Permissions = { view } });

            var user = new User
            {
                Value = UserId, Login = "ana", Name = "Ana", Email = "ana@x.test", Type = "local", Enabled = true,
                RoleId = RoleId, Password = [9, 9], Salt = "salt", Lang = "en"
            };
            user.Permissions.Add(approve);
            user.Permissions.Add(plain);
            db.Users.Add(user);
        });

        // The seed's own grants are recorded too; each test looks only at what its action adds.
        using var context = OpenContext();
        _seededEvents = context.PermissionGrantEvents.Select(e => e.Id).DefaultIfEmpty().Max();
    }

    private readonly long _seededEvents;

    private static Permission Permission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = key, Order = id };

    private PermissionGrantEvent[] Events(PermissionGrantKind kind)
    {
        using var db = OpenContext();
        return db.PermissionGrantEvents.AsNoTracking().Where(e => e.Kind == kind && e.Id > _seededEvents)
            .OrderBy(e => e.Id).ToArray();
    }

    private int[] UserGrants()
    {
        using var db = OpenContext();
        return db.Users.AsNoTracking().Include(u => u.Permissions).Single(u => u.Value == UserId)
            .Permissions.Select(p => p.Id).Order().ToArray();
    }

    private int[] RoleGrants(int roleId = RoleId)
    {
        using var db = OpenContext();
        return db.Roles.AsNoTracking().Include(r => r.Permissions).Single(r => r.Value == roleId)
            .Permissions.Select(p => p.Id).Order().ToArray();
    }

    private User Row()
    {
        using var db = OpenContext();
        return db.Users.AsNoTracking().Single(u => u.Value == UserId);
    }

    [Fact]
    public async Task TestResavingTheSameUserPermissionsRecordsNothing()
    {
        await _permissions.SaveUserPermissionsByIdAsync(UserId, [Approve, Plain]);

        Assert.Equal(new[] { Approve, Plain }, UserGrants());
        Assert.Empty(Events(PermissionGrantKind.UserPermission));
    }

    [Fact]
    public async Task TestAUserPermissionSaveRecordsOnlyWhatChanged()
    {
        // Approve kept, Plain dropped, Configure added: one grant, no revoke-and-regrant of Approve.
        await _permissions.SaveUserPermissionsByIdAsync(UserId, [Approve, Configure]);

        Assert.Equal(new[] { Approve, Configure }, UserGrants());
        var events = Events(PermissionGrantKind.UserPermission);
        Assert.Equal(new (PermissionGrantAction, string?)[] { (PermissionGrantAction.Grant, RiskIndexPermissions.Configure) },
            events.Select(e => (e.Action, e.PermissionKey)).ToArray());
    }

    [Fact]
    public async Task TestAUserPermissionSaveRevokesWhatWasDropped()
    {
        await _permissions.SaveUserPermissionsByIdAsync(UserId, [Plain]);

        Assert.Equal(new[] { Plain }, UserGrants());
        Assert.Equal(new (PermissionGrantAction, string?)[] { (PermissionGrantAction.Revoke, RiskIndexPermissions.Approve) },
            Events(PermissionGrantKind.UserPermission).Select(e => (e.Action, e.PermissionKey)).ToArray());
    }

    [Fact]
    public async Task TestAUserPermissionSaveIgnoresUnknownIds()
    {
        await _permissions.SaveUserPermissionsByIdAsync(UserId, [Approve, Plain, 999]);

        Assert.Equal(new[] { Approve, Plain }, UserGrants());
    }

    [Fact]
    public async Task TestResavingTheSameRolePermissionsRecordsNothing()
    {
        await _roles.UpdatePermissionsAsync(RoleId, [RiskIndexPermissions.Approve, "riskmanagement"]);

        Assert.Equal(new[] { Approve, Plain }, RoleGrants());
        Assert.Empty(Events(PermissionGrantKind.RolePermission));
    }

    [Fact]
    public async Task TestARolePermissionSaveRecordsOnlyWhatChanged()
    {
        await _roles.UpdatePermissionsAsync(RoleId, [RiskIndexPermissions.Approve, RiskIndexPermissions.Configure]);

        Assert.Equal(new[] { Approve, Configure }, RoleGrants());
        Assert.Equal(new (PermissionGrantAction, string?)[] { (PermissionGrantAction.Grant, RiskIndexPermissions.Configure) },
            Events(PermissionGrantKind.RolePermission).Select(e => (e.Action, e.PermissionKey)).ToArray());
    }

    [Fact]
    public void TestTheDiffReportsWhetherAnythingChanged()
    {
        var a = Permission(1, "a");
        var b = Permission(2, "b");
        var current = new List<Permission> { a };

        Assert.False(PermissionsService.ApplyPermissionDiff(current, [a]));
        Assert.True(PermissionsService.ApplyPermissionDiff(current, [a, b]));
        Assert.True(PermissionsService.ApplyPermissionDiff(current, [b]));
        Assert.Equal(new[] { b }, current);
    }

    // ------------------------------------------------------------ SaveUserWithPermissionsAsync

    [Fact]
    public async Task TestSavingAUserWithPermissionsIsOneSave()
    {
        var user = Row();
        user.Name = "Ana Maria";
        var before = SaveChangesCount;

        await _users.SaveUserWithPermissionsAsync(user, [Approve, Configure]);

        Assert.Equal(1, SaveChangesCount - before);
        var saved = Row();
        Assert.Equal("Ana Maria", saved.Name);
        Assert.Equal(new byte[] { 9, 9 }, saved.Password);
        Assert.Equal("salt", saved.Salt);
        Assert.Equal(new[] { Approve, Configure }, UserGrants());
    }

    [Fact]
    public async Task TestSavingAUserWithPermissionsRecordsTheRoleMoveAndOnlyTheRealGrantsTogether()
    {
        var user = Row();
        user.RoleId = OtherRoleId;

        await _users.SaveUserWithPermissionsAsync(user, [Approve, Configure]);

        Assert.Equal(new (PermissionGrantAction, string?)[] { (PermissionGrantAction.Grant, RiskIndexPermissions.Configure) },
            Events(PermissionGrantKind.UserPermission).Select(e => (e.Action, e.PermissionKey)).ToArray());
        Assert.Equal(new[] { PermissionGrantAction.Revoke, PermissionGrantAction.Grant },
            Events(PermissionGrantKind.RoleMembership).Select(e => e.Action).ToArray());
        Assert.Equal(OtherRoleId, Row().RoleId);
    }

    [Fact]
    public async Task TestSavingAnUnchangedUserWithTheSamePermissionsRecordsNothing()
    {
        await _users.SaveUserWithPermissionsAsync(Row(), [Approve, Plain]);

        Assert.Equal(new[] { Approve, Plain }, UserGrants());
        Assert.Empty(Events(PermissionGrantKind.UserPermission));
        Assert.Empty(Events(PermissionGrantKind.RoleMembership));
    }

    [Fact]
    public async Task TestSavingAMissingUserWithPermissionsThrowsAndWritesNothing()
    {
        var ghost = Row();
        ghost.Value = 404;
        var before = SaveChangesCount;

        await Assert.ThrowsAsync<DataNotFoundException>(() => _users.SaveUserWithPermissionsAsync(ghost, [Approve]));

        Assert.Equal(before, SaveChangesCount);
        Assert.Equal(new[] { Approve, Plain }, UserGrants());
    }
}
