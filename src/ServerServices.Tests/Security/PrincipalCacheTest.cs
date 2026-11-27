using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// The principal cache the authentication handlers build claims from: a hit, expiry at the time to live, invalidation, the
/// per-user and per-role key, the bypass at 0, and that a load racing an invalidation is not kept.
/// </summary>
[TestSubject(typeof(PrincipalCache))]
public class PrincipalCacheTest
{
    private DateTime _now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private PrincipalCache Cache(int seconds = 30) => new(TimeSpan.FromSeconds(seconds), () => _now);

    private static PrincipalSnapshot Snapshot(int userId = 1, int roleId = 3) =>
        new(userId, roleId, "analyst", ["riskmanagement"], [7]);

    [Fact]
    public void TestAStoredSnapshotIsAHit()
    {
        var cache = Cache();
        var snapshot = Snapshot();

        cache.Store(snapshot, cache.Generation);

        Assert.True(cache.TryGet(1, 3, out var hit));
        Assert.Same(snapshot, hit);
    }

    [Fact]
    public void TestAnEntryExpiresAtTheTimeToLive()
    {
        var cache = Cache(30);
        cache.Store(Snapshot(), cache.Generation);

        _now = _now.AddSeconds(29);
        Assert.True(cache.TryGet(1, 3, out _));

        _now = _now.AddSeconds(1);
        Assert.False(cache.TryGet(1, 3, out var expired));
        Assert.Null(expired);
    }

    [Fact]
    public void TestInvalidateDropsEveryEntry()
    {
        var cache = Cache();
        cache.Store(Snapshot(1), cache.Generation);
        cache.Store(Snapshot(2), cache.Generation);

        cache.Invalidate();

        Assert.False(cache.TryGet(1, 3, out _));
        Assert.False(cache.TryGet(2, 3, out _));
    }

    [Fact]
    public void TestTheKeyIsTheUserAndNeverServesAnotherUser()
    {
        var cache = Cache();
        cache.Store(Snapshot(1), cache.Generation);

        Assert.False(cache.TryGet(2, 3, out _));
    }

    [Fact]
    public void TestASnapshotBuiltForAnotherRoleIsAMiss()
    {
        var cache = Cache();
        cache.Store(Snapshot(1, roleId: 3), cache.Generation);

        // The live user row names role 4 now: role 3's name and permissions must not be served.
        Assert.False(cache.TryGet(1, 4, out _));
    }

    [Fact]
    public void TestALoadThatRacedAnInvalidationIsNotKept()
    {
        var cache = Cache();
        var generationAtLoad = cache.Generation;

        cache.Invalidate();
        cache.Store(Snapshot(), generationAtLoad);

        Assert.False(cache.TryGet(1, 3, out _));
    }

    [Fact]
    public void TestZeroSecondsTurnsTheCacheOff()
    {
        var cache = Cache(0);
        cache.Store(Snapshot(), cache.Generation);

        Assert.Equal(TimeSpan.Zero, cache.TimeToLive);
        Assert.False(cache.TryGet(1, 3, out _));
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("45", 45)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("thirty", 0)]
    [InlineData("1.5", 0)]
    public void TestTheConfiguredTimeToLive(string? configured, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PrincipalCache.TimeToLiveFrom(configured));
    }

    [Fact]
    public void TestATimeToLiveAboveTheMaximumIsLowered()
    {
        Assert.Equal(PrincipalCache.MaximumTimeToLive, new PrincipalCache(TimeSpan.FromHours(1)).TimeToLive);
    }

    [Fact]
    public void TestTheConfigurationKeyIsRead()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PrincipalCache.ConfigurationKey] = "0" })
            .Build();

        Assert.Equal(TimeSpan.Zero, PrincipalCache.FromConfiguration(configuration).TimeToLive);
    }
}

/// <summary>
/// <see cref="PrincipalResolver"/> over the in-memory store: what it loads, that it agrees with the permission computation
/// the handlers used before, that a hit reads nothing, and that a failure is never cached.
/// </summary>
[TestSubject(typeof(PrincipalResolver))]
public class PrincipalResolverTest
{
    // No PrincipalInputsChanged wiring: a write here is one the cache is never told about, which is how a hit is observed.
    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());

    public PrincipalResolverTest()
    {
        using var db = _dal.GetContext();
        var view = Permission(1, "riskmanagement");
        var hosts = Permission(2, "hosts");
        var audit = Permission(3, "audit");
        db.Permissions.AddRange(view, hosts, audit);
        db.Roles.Add(new Role { Value = 3, Name = "analyst", Permissions = { view, hosts } });
        db.Users.Add(User(10, roleId: 3, hosts, audit));
        db.Users.Add(User(11, roleId: 0, audit));
        db.UserEntityRoles.AddRange(
            new UserEntityRole { Id = 1, UserId = 10, EntityId = 7, RoleId = 3, CreatedAt = DateTime.UtcNow },
            new UserEntityRole { Id = 2, UserId = 10, EntityId = 8, RoleId = 3, CreatedAt = DateTime.UtcNow,
                RevokedAt = DateTime.UtcNow },
            new UserEntityRole { Id = 3, UserId = 11, EntityId = 9, RoleId = 3, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
    }

    private static Permission Permission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = key, Order = id };

    private static User User(int id, int roleId, params Permission[] own)
    {
        var user = new User
        {
            Value = id, Login = $"u{id}", Name = $"User {id}", Email = $"u{id}@x.test", Type = "local", Enabled = true,
            RoleId = roleId, Password = [1]
        };
        foreach (var p in own) user.Permissions.Add(p);
        return user;
    }

    private User Row(int id)
    {
        using var db = _dal.GetContext();
        return db.Users.Single(u => u.Value == id);
    }

    private PrincipalResolver Resolver(int seconds = 30) =>
        new(_dal, new PrincipalCache(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task TestTheSnapshotHoldsTheRoleItsPermissionsThenTheUsersAndTheActiveAssignments()
    {
        var snapshot = await Resolver().ResolveAsync(Row(10));

        Assert.Equal("analyst", snapshot.RoleName);
        Assert.Equal(new[] { "riskmanagement", "hosts", "audit" }, snapshot.PermissionKeys);
        Assert.Equal(new[] { 7 }, snapshot.EntityIds);
    }

    [Fact]
    public async Task TestAUserWithoutARoleHasNoRoleNameAndOnlyTheirOwnPermissions()
    {
        var snapshot = await Resolver().ResolveAsync(Row(11));

        Assert.Null(snapshot.RoleName);
        Assert.Equal(new[] { "audit" }, snapshot.PermissionKeys);
        Assert.Equal(new[] { 9 }, snapshot.EntityIds);
    }

    /// <summary>The handlers' claims must not change: the snapshot's keys are exactly what PermissionsService computes.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public async Task TestThePermissionsAgreeWithThePermissionsService(int userId)
    {
        var roles = new RolesService(Serilog.Core.Logger.None, _dal);
        var legacy = await new PermissionsService(_dal, roles).GetUserPermissionsAsync(Row(userId));

        var snapshot = await Resolver().ResolveAsync(Row(userId));

        Assert.Equal(legacy, snapshot.PermissionKeys);
    }

    [Fact]
    public async Task TestASecondResolveIsServedFromTheCache()
    {
        var resolver = Resolver();
        await resolver.ResolveAsync(Row(10));

        await using (var db = _dal.GetContext())
        {
            db.Roles.Single(r => r.Value == 3).Name = "renamed behind the cache's back";
            await db.SaveChangesAsync();
        }

        Assert.Equal("analyst", (await resolver.ResolveAsync(Row(10))).RoleName);
    }

    [Fact]
    public async Task TestWithTheCacheOffEveryResolveReadsTheDatabase()
    {
        var resolver = Resolver(0);
        await resolver.ResolveAsync(Row(10));

        await using (var db = _dal.GetContext())
        {
            db.Roles.Single(r => r.Value == 3).Name = "renamed";
            await db.SaveChangesAsync();
        }

        Assert.Equal("renamed", (await resolver.ResolveAsync(Row(10))).RoleName);
    }

    [Fact]
    public async Task TestAMissingRoleThrowsAndIsNotCached()
    {
        var cache = new PrincipalCache(TimeSpan.FromSeconds(30));
        var resolver = new PrincipalResolver(_dal, cache);
        var row = Row(10);
        row.RoleId = 99;

        await Assert.ThrowsAsync<DataNotFoundException>(() => resolver.ResolveAsync(row));

        Assert.False(cache.TryGet(10, 99, out _));
    }

    [Fact]
    public async Task TestAMissingUserThrowsAndIsNotCached()
    {
        var cache = new PrincipalCache(TimeSpan.FromSeconds(30));
        var resolver = new PrincipalResolver(_dal, cache);
        var ghost = Row(10);
        ghost.Value = 404;

        await Assert.ThrowsAsync<UserNotFoundException>(() => resolver.ResolveAsync(ghost));

        Assert.False(cache.TryGet(404, 3, out _));
    }
}
