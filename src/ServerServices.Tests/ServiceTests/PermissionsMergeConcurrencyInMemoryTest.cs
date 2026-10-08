using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// <see cref="PermissionsService.GetUserPermissionsAsync"/> backs every permission check. It used to merge
/// the user's own permissions into a plain <c>List&lt;string&gt;</c> from inside <c>Parallel.ForEach</c>,
/// which loses entries or throws under concurrent <c>Add</c>.
/// </summary>
[TestSubject(typeof(PermissionsService))]
public class PermissionsMergeConcurrencyInMemoryTest : InMemoryServiceTestBase
{
    private const int UserPermissionCount = 2000;
    private const int RolePermissionCount = 50;

    private static User NewUser(int id, int roleId) => new()
    {
        Value = id, Name = $"u{id}", Login = $"u{id}", Enabled = true, RoleId = roleId,
        Type = "local", Salt = "s", Password = new byte[] { 1 }, Email = $"u{id}@x.test"
    };

    private static Permission NewPermission(int id, string key) =>
        new() { Id = id, Key = key, Name = key, Description = key, Order = id };

    private User SeedUser(int roleId)
    {
        Seed(ctx =>
        {
            var role = new Role { Value = 1, Name = "analyst" };
            // Role and user share the first 20 keys, so the de-duplication path is exercised too.
            for (var i = 0; i < RolePermissionCount; i++)
                role.Permissions.Add(NewPermission(i + 1, $"perm_{i}"));
            ctx.Roles.Add(role);

            var user = NewUser(1, roleId);
            foreach (var p in role.Permissions.Take(20)) user.Permissions.Add(p);
            for (var i = RolePermissionCount; i < UserPermissionCount; i++)
                user.Permissions.Add(NewPermission(i + 1, $"perm_{i}"));
            ctx.Users.Add(user);
        });
        return NewUser(1, roleId);
    }

    [Fact]
    public async Task TestMergedPermissionsAreCompleteAndDuplicateFreeUnderRepeatedCalls()
    {
        var user = SeedUser(roleId: 1);
        var service = GetService<IPermissionsService>();
        var expected = Enumerable.Range(0, UserPermissionCount).Select(i => $"perm_{i}").ToList();

        // The race is probabilistic: pre-fix, a lost Add or a torn resize shows up in some of these runs,
        // not in every one. 25 runs of 1,950 parallel adds make a clean pre-fix pass very unlikely.
        for (var run = 0; run < 25; run++)
        {
            var result = await service.GetUserPermissionsAsync(user);

            Assert.Equal(expected.Count, result.Count);
            Assert.Equal(expected.Count, result.Distinct().Count());
            Assert.Equal(expected.OrderBy(x => x), result.OrderBy(x => x));
        }
    }

    [Fact]
    public async Task TestRolePermissionsComeFirstThenTheUsersExtrasInOrder()
    {
        var user = SeedUser(roleId: 1);
        var result = await GetService<IPermissionsService>().GetUserPermissionsAsync(user);

        // Role permissions keep the role's order; the user's extras follow, in the user's order.
        Assert.Equal(RolePermissionCount, result.Take(RolePermissionCount).Intersect(
            Enumerable.Range(0, RolePermissionCount).Select(i => $"perm_{i}")).Count());
        Assert.Equal(
            Enumerable.Range(RolePermissionCount, UserPermissionCount - RolePermissionCount).Select(i => $"perm_{i}"),
            result.Skip(RolePermissionCount));
    }

    [Fact]
    public async Task TestUserWithoutRoleGetsOnlyOwnPermissions()
    {
        var user = SeedUser(roleId: 1);
        user.RoleId = 0;

        var result = await GetService<IPermissionsService>().GetUserPermissionsAsync(user);

        // The user holds 20 of the role's keys plus every key from RolePermissionCount up.
        Assert.Equal(20 + UserPermissionCount - RolePermissionCount, result.Count);
        Assert.Equal(result.Count, result.Distinct().Count());
    }

    [Fact]
    public async Task TestRolePermissionsAreCompleteAndInRoleOrderUnderRepeatedCalls()
    {
        // RolesService.GetRolePermissionsAsync had the same Parallel.ForEach-into-a-List race, one call
        // below the merge fixed above; it surfaced as a flaky "Source array was not long enough" here.
        // Asserting the order makes the pre-fix failure deterministic rather than probabilistic.
        Seed(ctx =>
        {
            var role = new Role { Value = 2, Name = "large" };
            for (var i = 0; i < UserPermissionCount; i++)
                role.Permissions.Add(NewPermission(i + 1, $"perm_{i}"));
            ctx.Roles.Add(role);
        });
        var service = GetService<IRolesService>();
        var expected = Enumerable.Range(0, UserPermissionCount).Select(i => $"perm_{i}").ToList();

        for (var run = 0; run < 25; run++)
            Assert.Equal(expected, await service.GetRolePermissionsAsync(2));
    }

    [Fact]
    public void TestUserHasPermissionFindsAnExtraAtTheEndOfALargeSet()
    {
        var user = SeedUser(roleId: 1);
        var service = GetService<IPermissionsService>();

        Assert.True(service.UserHasPermission(user, $"perm_{UserPermissionCount - 1}"));
        Assert.False(service.UserHasPermission(user, "perm_missing"));
    }
}
