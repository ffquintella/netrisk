using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The principal cache on a real MariaDB, where the in-memory provider cannot speak for the SQL: the snapshot's permission
/// keys come back in exactly the order the handlers' old queries returned them, the desktop's single save of a user with
/// their permissions commits both in one save, and a save inside an explicit transaction drops the cache again once its
/// context is disposed after the commit. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class PrincipalCacheIntegrationTests(MariaDbContainerFixture fixture)
{
    /// <summary>Counts invalidations, so a test can tell a drop at save time from a second one at dispose.</summary>
    private sealed class CountingCache : IPrincipalCache
    {
        private readonly PrincipalCache _inner = new(TimeSpan.FromMinutes(5));
        private int _invalidations;
        public int Invalidations => Volatile.Read(ref _invalidations);
        public TimeSpan TimeToLive => _inner.TimeToLive;
        public long Generation => _inner.Generation;
        public bool TryGet(int userId, int roleId, out PrincipalSnapshot? snapshot) => _inner.TryGet(userId, roleId, out snapshot);
        public void Store(PrincipalSnapshot snapshot, long generationAtLoad) => _inner.Store(snapshot, generationAtLoad);

        public void Invalidate()
        {
            Interlocked.Increment(ref _invalidations);
            _inner.Invalidate();
        }
    }

    private sealed class NoRequest : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private DalService Dal(IPrincipalCache cache)
    {
        var connectionString = fixture.ConnectionString.Contains("ConvertZeroDateTime", StringComparison.OrdinalIgnoreCase)
            ? fixture.ConnectionString
            : fixture.ConnectionString + "ConvertZeroDateTime=True;";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString })
            .Build();
        return new DalService(configuration, new NoRequest(), null, cache);
    }

    /// <summary>A role and a user, each granted seeded permissions out of id order, plus two assignments, one revoked.</summary>
    private async Task<(User User, int[] PermissionIds)> SeedAsync(DalService dal)
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var db = dal.GetContext(withIdentity: false, bypassEntityScope: true);
        var seeded = await db.Permissions.OrderByDescending(p => p.Id).Take(5).ToListAsync();
        Assert.Equal(5, seeded.Count);

        var role = new Role { Name = "principal-cache-role", Permissions = { seeded[0], seeded[2], seeded[1] } };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            Login = "principal-cache-user", Name = "Principal Cache", Email = "pc@example.invalid", Type = "local",
            Enabled = true, Lockout = 0, RoleId = role.Value, Lang = "en", Salt = "s", Password = [1, 2, 3],
            LastPasswordChangeDate = DateTime.UtcNow, LastLogin = DateTime.UtcNow,
            Permissions = { seeded[4], seeded[1], seeded[3] }
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entity = new Entity
        {
            DefinitionName = "organization", DefinitionVersion = "2.6", Status = "active", Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow, CreatedBy = user.Value, UpdatedBy = user.Value
        };
        db.Entities.Add(entity);
        await db.SaveChangesAsync();
        db.UserEntityRoles.AddRange(
            new UserEntityRole { UserId = user.Value, EntityId = entity.Id, RoleId = role.Value, CreatedAt = DateTime.UtcNow },
            new UserEntityRole { UserId = user.Value, EntityId = entity.Id, RoleId = role.Value, CreatedAt = DateTime.UtcNow,
                RevokedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        return (await db.Users.AsNoTracking().SingleAsync(u => u.Value == user.Value), seeded.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task TestTheSnapshotMatchesThePermissionsTheHandlersComputedBefore()
    {
        var dal = Dal(new CountingCache());
        var (user, _) = await SeedAsync(dal);
        var roles = new RolesService(Serilog.Core.Logger.None, dal);

        var legacyPermissions = await new PermissionsService(dal, roles).GetUserPermissionsAsync(user);
        var legacyRole = (await roles.GetRoleAsync(user.RoleId))!.Name;
        var snapshot = await new PrincipalResolver(dal, new PrincipalCache(TimeSpan.Zero)).LoadAsync(user);

        Assert.Equal(legacyPermissions, snapshot.PermissionKeys);
        Assert.Equal(legacyRole, snapshot.RoleName);
        Assert.Single(snapshot.EntityIds);
    }

    [Fact]
    public async Task TestAUserAndTheirPermissionsAreSavedInOneSaveAndDropTheCache()
    {
        var cache = new CountingCache();
        var dal = Dal(cache);
        var (user, ids) = await SeedAsync(dal);
        var roles = new RolesService(Serilog.Core.Logger.None, dal);
        var users = new UsersService(dal, NullLoggerFactory.Instance, roles, new PermissionsService(dal, roles));
        var before = cache.Invalidations;

        user.Name = "Renamed";
        await users.SaveUserWithPermissionsAsync(user, [ids[0], ids[4]]);

        Assert.Equal(before + 1, cache.Invalidations);
        await using var db = dal.GetContext(withIdentity: false, bypassEntityScope: true);
        var saved = await db.Users.AsNoTracking().Include(u => u.Permissions).SingleAsync(u => u.Value == user.Value);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(new[] { ids[0], ids[4] }.Order(), saved.Permissions.Select(p => p.Id).Order());
    }

    [Fact]
    public async Task TestASaveInsideAnExplicitTransactionDropsTheCacheAgainAfterTheCommit()
    {
        var cache = new CountingCache();
        var dal = Dal(cache);
        var (user, _) = await SeedAsync(dal);
        var before = cache.Invalidations;

        var db = dal.GetContext(withIdentity: false, bypassEntityScope: true);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.Users.SingleAsync(u => u.Value == user.Value)).Name = "In a transaction";
            await db.SaveChangesAsync();
            Assert.Equal(before + 1, cache.Invalidations);
            await transaction.CommitAsync();
        });
        await db.DisposeAsync();

        Assert.Equal(before + 2, cache.Invalidations);
    }
}
