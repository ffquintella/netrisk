using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Entities;
using Model.RiskIndex;
using ServerServices.RiskIndex.Profiles;
using Xunit;

namespace ServerServices.Tests.ContextTests;

/// <summary>
/// The permission-grant record the approval's <c>sod_self_granted_approver</c> reads (S40 §6.4, §11.4, §11.7): written by the
/// context itself in the same save as the change, whatever service made it, attributed to the save's user, and insert-only.
/// </summary>
[TestSubject(typeof(PermissionGrantRecorder))]
public class PermissionGrantRecorderTest
{
    private readonly string _database = Guid.NewGuid().ToString();

    private AuditableContext Context(int user = 0) =>
        new(new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(_database).Options)
        {
            UserId = user, AuditActor = user > 0 ? "u" + user : AuditableContext.SystemActor
        };

    public PermissionGrantRecorderTest()
    {
        using var db = Context();
        db.Permissions.AddRange(
            new Permission { Id = 1, Key = RiskIndexPermissions.Approve, Name = "a", Description = "" },
            new Permission { Id = 2, Key = "riskmanagement", Name = "r", Description = "" });
        db.Roles.AddRange(new Role { Value = 10, Name = "RiskManager" }, new Role { Value = 11, Name = "Plain" });
        db.Users.Add(NewUser(100, 11));
        db.SaveChanges();
    }

    private static User NewUser(int id, int role) => new()
    {
        Value = id, Name = $"user {id}", Login = $"u{id}", Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"u{id}@x.test", RoleId = role
    };

    private PermissionGrantEvent[] Events(PermissionGrantKind kind)
    {
        using var db = Context();
        return db.PermissionGrantEvents.AsNoTracking().Where(e => e.Kind == kind).OrderBy(e => e.Id).ToArray();
    }

    /// <summary>The watched list is the spec's (S40 §6.4), spelled where DAL can read it; the two copies cannot drift.</summary>
    [Fact]
    public void TestTheWatchedPermissionsAndTheDefaultApproverRolesAgreeWithTheModel()
    {
        Assert.Equal(new[]
        {
            RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve, EntityPermissions.RiskContext,
            RiskIndexProfilesService.BusinessRiskReview
        }.Order(), PermissionGrantRecorder.WatchedPermissions.Order());
        Assert.Equal(RiskIndexRoles.DefaultApproverRoles, RiskIndexSettingKeys.DefaultApproverRoles);
    }

    [Fact]
    public async Task TestARoleGrantAndRevocationOfAWatchedPermissionAreRecordedWithTheirAuthor()
    {
        await using (var db = Context(7))
        {
            var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Value == 10);
            foreach (var p in await db.Permissions.ToListAsync()) role.Permissions.Add(p);
            await db.SaveChangesAsync();
        }

        await using (var db = Context(8))
        {
            (await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Value == 10)).Permissions.Clear();
            await db.SaveChangesAsync();
        }

        var events = Events(PermissionGrantKind.RolePermission);
        Assert.Equal(new (PermissionGrantAction, int?, int?, string?)[]
        {
            (PermissionGrantAction.Grant, 10, 7, RiskIndexPermissions.Approve),
            (PermissionGrantAction.Revoke, 10, 8, RiskIndexPermissions.Approve)
        }, events.Select(e => (e.Action, e.RoleId, e.ActorId, e.PermissionKey)).ToArray());
        Assert.Equal("u7", events[0].Actor);
    }

    [Fact]
    public async Task TestADirectGrantAMembershipChangeAndANewUserAreRecorded()
    {
        await using (var db = Context(7))
        {
            var user = await db.Users.Include(u => u.Permissions).SingleAsync(u => u.Value == 100);
            user.Permissions.Add(await db.Permissions.SingleAsync(p => p.Id == 1));
            user.RoleId = 10;
            db.Users.Add(NewUser(101, 10));
            await db.SaveChangesAsync();
        }

        var direct = Assert.Single(Events(PermissionGrantKind.UserPermission));
        Assert.Equal((PermissionGrantAction.Grant, (int?)100, (int?)7), (direct.Action, direct.UserId, direct.ActorId));
        Assert.Equal(new (PermissionGrantAction, int?, int?)[]
        {
            (PermissionGrantAction.Grant, 101, 10), // after the system's seed of user 100 into Plain: user 7's save
            (PermissionGrantAction.Revoke, 100, 11),
            (PermissionGrantAction.Grant, 100, 10)
        }.Order(), Events(PermissionGrantKind.RoleMembership).Skip(1).Select(e => (e.Action, e.UserId, e.RoleId)).Order());
        Assert.All(Events(PermissionGrantKind.RoleMembership).Skip(1), e => Assert.Equal(7, e.ActorId));
    }

    [Fact]
    public async Task TestAnApproverRolesEditRecordsEachNameItAddsOrRemoves()
    {
        await using (var db = Context(7))
        {
            db.Settings.Add(new Setting { Name = RiskIndexSettingKeys.ApproverRoles, Value = "RiskManager,Committee" });
            await db.SaveChangesAsync();
        }

        await using (var db = Context(8))
        {
            db.Settings.Remove(await db.Settings.SingleAsync(s => s.Name == RiskIndexSettingKeys.ApproverRoles));
            await db.SaveChangesAsync();
        }

        Assert.Equal(new (PermissionGrantAction, string?, int?)[]
        {
            (PermissionGrantAction.Grant, "Committee", 7), // the missing row meant the default list
            (PermissionGrantAction.Revoke, "RiskAdministrator", 7),
            (PermissionGrantAction.Grant, "RiskAdministrator", 8), // the deleted row falls back to the default
            (PermissionGrantAction.Revoke, "Committee", 8)
        }, Events(PermissionGrantKind.ApproverRoles).Select(e => (e.Action, e.RoleName, e.ActorId)).ToArray());
    }

    [Fact]
    public async Task TestARoleCreatedOrRenamedIsRecordedByName()
    {
        await using (var db = Context(7))
        {
            db.Roles.Add(new Role { Value = 12, Name = "Committee", Permissions = [await db.Permissions.SingleAsync(p => p.Id == 1)] });
            (await db.Roles.SingleAsync(r => r.Value == 11)).Name = "RiskAdministrator";
            await db.SaveChangesAsync();
        }

        Assert.Contains(Events(PermissionGrantKind.RoleName), e => e is { RoleId: 12, OldValue: null, NewValue: "Committee", ActorId: 7 });
        Assert.Contains(Events(PermissionGrantKind.RoleName),
            e => e is { RoleId: 11, OldValue: "Plain", NewValue: "RiskAdministrator", ActorId: 7 });
        Assert.Contains(Events(PermissionGrantKind.RolePermission), e => e is { RoleId: 12, ActorId: 7, Action: PermissionGrantAction.Grant });
    }

    /// <summary>The record is evidence: the context refuses to change or delete it, and the refused save writes nothing.</summary>
    [Fact]
    public async Task TestTheRecordIsInsertOnly()
    {
        await using (var db = Context(7))
        {
            (await db.Users.SingleAsync(u => u.Value == 100)).RoleId = 10;
            await db.SaveChangesAsync();
        }

        await using (var db = Context(7))
        {
            var e = await db.PermissionGrantEvents.FirstAsync();
            e.ActorId = 999;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var db = Context(7))
        {
            db.PermissionGrantEvents.Remove(await db.PermissionGrantEvents.FirstAsync());
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        Assert.DoesNotContain(Events(PermissionGrantKind.RoleMembership), e => e.ActorId == 999);
        Assert.Equal(3, Events(PermissionGrantKind.RoleMembership).Length);
    }
}
