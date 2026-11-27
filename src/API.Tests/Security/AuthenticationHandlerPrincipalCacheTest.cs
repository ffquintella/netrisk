using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using API.Security;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// The JWT and Basic handlers build their principal from one live read of the user plus the principal cache. These tests
/// run the real handlers over an in-memory store and hold them to three things: the claims are exactly those the handlers
/// built before (compared with that computation, reproduced here over the same rows); the cache is used and dropped by
/// every write made through the DAL; and nothing the cache holds can keep a disabled, deleted or revoked session alive.
/// </summary>
[TestSubject(typeof(JwtAuthenticationHandler))]
public class AuthenticationHandlerPrincipalCacheTest
{
    private const int AdminId = 1, AnalystId = 2, NoRoleId = 3, AnalystRole = 5, ReaderRole = 6;
    private const string Issuer = "test-issuer", Audience = "test-audience", ClientId = "desktop-1";

    private static readonly byte[] SigningKey = Encoding.UTF8.GetBytes("a-test-signing-key-of-sufficient-length-for-hs256!");

    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>Writes the cache is never told about: how a hit, and the live user read, are observed.</summary>
    private readonly InMemoryDalService _sideDoor;

    private readonly ITokenRevocationService _revocation = Substitute.For<ITokenRevocationService>();

    public AuthenticationHandlerPrincipalCacheTest()
    {
        _sideDoor = new InMemoryDalService(_database);

        using var db = _sideDoor.GetContext();
        var perms = new[] { "riskmanagement", "hosts", "audit", "reports", "" }
            .Select((key, i) => new Permission { Id = i + 1, Key = key, Name = key, Description = key, Order = i + 1 })
            .ToArray();
        db.Permissions.AddRange(perms);
        // Role permissions added out of id order: the claims must keep the order the legacy queries returned them in.
        db.Roles.Add(new Role { Value = AnalystRole, Name = "analyst", Permissions = { perms[1], perms[0] } });
        db.Roles.Add(new Role { Value = ReaderRole, Name = "reader", Permissions = { perms[3] } });
        db.Users.AddRange(
            NewUser(AdminId, "root", AnalystRole, admin: true, perms[2]),
            NewUser(AnalystId, "ana", AnalystRole, admin: false, perms[1], perms[2], perms[4]),
            NewUser(NoRoleId, "nora", 0, admin: false, perms[3]));
        db.Entities.AddRange(NewEntity(7), NewEntity(8), NewEntity(9));
        db.UserEntityRoles.AddRange(
            new UserEntityRole { Id = 1, UserId = AnalystId, EntityId = 8, RoleId = AnalystRole, CreatedAt = DateTime.UtcNow },
            new UserEntityRole { Id = 2, UserId = AnalystId, EntityId = 7, RoleId = AnalystRole, CreatedAt = DateTime.UtcNow },
            new UserEntityRole { Id = 3, UserId = AnalystId, EntityId = 9, RoleId = AnalystRole, CreatedAt = DateTime.UtcNow,
                RevokedAt = DateTime.UtcNow });
        db.ClientRegistrations.Add(new ClientRegistration
        {
            Id = 1, ExternalId = ClientId, Name = "Desktop", Status = "approved", Hostname = "h", LoggedAccount = "l",
            RegistrationDate = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static User NewUser(int id, string login, int roleId, bool admin, params Permission[] own)
    {
        var user = new User
        {
            Value = id, Login = login, Name = login, Email = $"{login}@x.test", Type = "local", Enabled = true,
            RoleId = roleId, Admin = admin, Lang = "en", Salt = "s",
            Password = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword("pw", 4))
        };
        foreach (var p in own) user.Permissions.Add(p);
        return user;
    }

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "unit", DefinitionVersion = "1", Status = "active", Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow
    };

    /// <summary>The production DalService on the in-memory store, wired to <paramref name="cache"/> as in the API.</summary>
    private sealed class HookedDalService(string database, IPrincipalCache cache) : DalService(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:ConnectionString"] = "server=localhost" }).Build(),
        new HttpContextAccessor(), null, cache)
    {
        protected override DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder() =>
            // The options InMemoryDalService uses: the in-memory store is per options configuration.
            new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(database).EnableSensitiveDataLogging();
    }

    private sealed record Host(PrincipalCache Cache, DalService Dal, IUsersService Users, IPermissionsService Permissions,
        IRolesService Roles, IPrincipalResolver Resolver);

    private Host NewHost(int cacheSeconds = 30)
    {
        var cache = new PrincipalCache(TimeSpan.FromSeconds(cacheSeconds));
        var dal = new HookedDalService(_database, cache);
        var roles = new RolesService(Serilog.Core.Logger.None, dal);
        var permissions = new PermissionsService(dal, roles);
        var users = new UsersService(dal, NullLoggerFactory.Instance, roles, permissions);
        return new Host(cache, dal, users, permissions, roles, new PrincipalResolver(dal, cache));
    }

    private static string Token(string login) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.Name, login),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())]),
            Issuer = Issuer, Audience = Audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1), NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(SigningKey), SecurityAlgorithms.HmacSha256)
        }));

    private async Task<AuthenticateResult> JwtAsync(Host host, string login)
    {
        var options = Substitute.For<IOptionsMonitor<JwtBearerOptions>>();
        options.Get(Arg.Any<string>()).Returns(new JwtBearerOptions
        {
            RequireHttpsMetadata = false,
            TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
                ValidIssuer = Issuer, ValidAudience = Audience, ValidateLifetime = true
            }
        });

        var clients = Substitute.For<IClientRegistrationService>();
        clients.FindApprovedRegistrationAsync(ClientId).Returns(new ClientRegistration { Name = "Desktop" });

        var handler = new JwtAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default, clients,
            Substitute.For<IEnvironmentService>(), host.Users, Substitute.For<IFaceIDService>(), host.Resolver, _revocation);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + Token(login);
        context.Request.Headers["ClientId"] = ClientId;
        await handler.InitializeAsync(new AuthenticationScheme("Bearer", null, typeof(JwtAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private async Task<AuthenticateResult> BasicAsync(Host host, string login)
    {
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());

        var handler = new BasicAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default,
            Substitute.For<IEnvironmentService>(), host.Users, host.Resolver, Substitute.For<ILoginAttemptTracker>(),
            host.Dal);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(login + ":pw"));
        context.Request.Headers["ClientId"] = ClientId;
        await handler.InitializeAsync(
            new AuthenticationScheme("BasicAuthentication", null, typeof(BasicAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private static (string, string)[] Claims(AuthenticateResult result)
    {
        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.Principal!.Claims.Select(c => (c.Type, c.Value)).ToArray();
    }

    /// <summary>
    /// The claims the JWT handler built before this change, step by step, through the services it called — the oracle the
    /// new handler is compared with. <paramref name="basic"/> is the one difference between the two handlers: JWT kept an
    /// empty permission key, Basic dropped it.
    /// </summary>
    private async Task<(string, string)[]> LegacyClaimsAsync(Host host, string login, bool basic)
    {
        var user = (await host.Users.GetUserAsync(login))!;
        var permissions = await host.Users.GetUserPermissionsAsync(user.Value);
        var claims = new List<(string, string)> { (ClaimTypes.Name, login) };
        if (user.Admin) claims.Add((ClaimTypes.Role, "Admin"));
        claims.Add((ClaimTypes.Role, user.RoleId == 0 ? "user" : (await host.Roles.GetRoleAsync(user.RoleId))!.Name));
        claims.AddRange(permissions.Where(p => basic ? !string.IsNullOrEmpty(p) : p != null).Select(p => ("Permission", p)));
        claims.Add((ClaimTypes.Sid, user.Value.ToString()));
        await using var db = _sideDoor.GetContext();
        claims.AddRange((await db.UserEntityRoles.Where(u => u.UserId == user.Value && u.RevokedAt == null).ToListAsync())
            .Select(u => ("entity_id", u.EntityId.ToString())));
        if (user.Admin) claims.Add(("scope", "global"));
        return claims.ToArray();
    }

    [Theory]
    [InlineData("root")]
    [InlineData("ana")]
    [InlineData("nora")]
    public async Task TestJwtClaimsAreTheOnesTheHandlerBuiltBefore(string login)
    {
        var host = NewHost();

        Assert.Equal(await LegacyClaimsAsync(host, login, basic: false), Claims(await JwtAsync(host, login)));
        // And the same from the cache.
        Assert.Equal(await LegacyClaimsAsync(host, login, basic: false), Claims(await JwtAsync(host, login)));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("ana")]
    [InlineData("nora")]
    public async Task TestBasicClaimsAreTheOnesTheHandlerBuiltBefore(string login)
    {
        var host = NewHost();

        Assert.Equal(await LegacyClaimsAsync(host, login, basic: true), Claims(await BasicAsync(host, login)));
    }

    [Fact]
    public async Task TestASecondRequestIsAnsweredFromTheCache()
    {
        var host = NewHost();
        await JwtAsync(host, "ana");

        await using (var db = _sideDoor.GetContext())
        {
            (await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Value == AnalystRole)).Permissions.Clear();
            await db.SaveChangesAsync();
        }

        Assert.Contains(("Permission", "riskmanagement"), Claims(await JwtAsync(host, "ana")));
    }

    [Fact]
    public async Task TestEachUserIsAnsweredWithTheirOwnPrincipal()
    {
        var host = NewHost();
        await JwtAsync(host, "ana");

        var nora = Claims(await JwtAsync(host, "nora"));

        Assert.Contains((ClaimTypes.Sid, NoRoleId.ToString()), nora);
        Assert.DoesNotContain(("Permission", "riskmanagement"), nora);
        Assert.DoesNotContain(nora, c => c.Item1 == "entity_id");
    }

    [Fact]
    public async Task TestAPermissionRevokedThroughTheServicesIsGoneOnTheNextRequest()
    {
        var host = NewHost();
        Assert.Contains(("Permission", "riskmanagement"), Claims(await JwtAsync(host, "ana")));

        await host.Roles.UpdatePermissionsAsync(AnalystRole, ["hosts"]);

        Assert.DoesNotContain(("Permission", "riskmanagement"), Claims(await JwtAsync(host, "ana")));
    }

    [Fact]
    public async Task TestAnEntityAssignmentRevokedThroughTheDalIsGoneOnTheNextRequest()
    {
        var host = NewHost();
        Assert.Contains(("entity_id", "7"), Claims(await JwtAsync(host, "ana")));

        await using (var db = host.Dal.GetContext())
        {
            (await db.UserEntityRoles.SingleAsync(a => a.Id == 2)).RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain(("entity_id", "7"), Claims(await JwtAsync(host, "ana")));
    }

    /// <summary>Disabled behind the cache's back — no invalidation at all — and still refused: the user row is live.</summary>
    [Fact]
    public async Task TestADisabledUserIsRefusedWhileTheCacheStillHoldsThem()
    {
        var host = NewHost();
        Claims(await JwtAsync(host, "ana"));

        await using (var db = _sideDoor.GetContext())
        {
            (await db.Users.SingleAsync(u => u.Value == AnalystId)).Enabled = false;
            await db.SaveChangesAsync();
        }

        Assert.True(host.Cache.TryGet(AnalystId, AnalystRole, out _));
        Assert.False((await JwtAsync(host, "ana")).Succeeded);
        Assert.False((await BasicAsync(host, "ana")).Succeeded);
    }

    [Fact]
    public async Task TestADisabledUserIsRefusedAfterADisableThroughTheServices()
    {
        var host = NewHost();
        Claims(await JwtAsync(host, "ana"));

        var user = host.Users.GetUserById(AnalystId)!;
        user.Enabled = false;
        host.Users.SaveUser(user);

        Assert.False(host.Cache.TryGet(AnalystId, AnalystRole, out _));
        Assert.False((await JwtAsync(host, "ana")).Succeeded);
    }

    [Fact]
    public async Task TestADeletedUserIsRefusedWhileTheCacheStillHoldsThem()
    {
        var host = NewHost();
        Claims(await JwtAsync(host, "nora"));

        await using (var db = _sideDoor.GetContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Value == NoRoleId));
            await db.SaveChangesAsync();
        }

        Assert.False((await JwtAsync(host, "nora")).Succeeded);
    }

    /// <summary>A move to another role made behind the cache's back is still seen: the entry is keyed to the live role.</summary>
    [Fact]
    public async Task TestARoleMoveIsSeenWithoutAnInvalidation()
    {
        var host = NewHost();
        Claims(await JwtAsync(host, "ana"));

        await using (var db = _sideDoor.GetContext())
        {
            (await db.Users.SingleAsync(u => u.Value == AnalystId)).RoleId = ReaderRole;
            await db.SaveChangesAsync();
        }

        var claims = Claims(await JwtAsync(host, "ana"));
        Assert.Contains((ClaimTypes.Role, "reader"), claims);
        Assert.DoesNotContain(("Permission", "riskmanagement"), claims);
    }

    [Fact]
    public async Task TestARevokedTokenIsRefusedWhileTheCacheHoldsItsUser()
    {
        var host = NewHost();
        Claims(await JwtAsync(host, "ana"));

        _revocation.IsRevokedAsync(Arg.Any<string>()).Returns(true);

        Assert.False((await JwtAsync(host, "ana")).Succeeded);
    }

    [Fact]
    public async Task TestWithTheCacheOffEveryRequestReadsTheDatabase()
    {
        var host = NewHost(cacheSeconds: 0);
        Claims(await JwtAsync(host, "ana"));

        await using (var db = _sideDoor.GetContext())
        {
            (await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Value == AnalystRole)).Permissions.Clear();
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain(("Permission", "riskmanagement"), Claims(await JwtAsync(host, "ana")));
    }

    /// <summary>
    /// The API's own composition root: one principal cache, read from <c>Security:PrincipalCacheSeconds</c>, and it is the
    /// one the DAL service hands to every context it opens — otherwise no write would ever drop it.
    /// </summary>
    [Fact]
    public void TestTheApiWiresItsPrincipalCacheIntoTheDalService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["email:from"] = "no-reply@netrisk.test",
                ["email:smtp:server"] = "localhost",
                ["email:smtp:port"] = "25",
                ["languages:availableLocales:0"] = "en-US",
                ["languages:defaultLocale"] = "en-US",
                ["Database:ConnectionString"] = "server=localhost",
                [PrincipalCache.ConfigurationKey] = "45"
            })
            .Build();
        var services = new ServiceCollection();
        ServicesBootstrapper.RegisterServices(services, config);
        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IPrincipalCache>();
        var dal = Assert.IsType<DalService>(provider.GetRequiredService<IDalService>());

        Assert.Equal(TimeSpan.FromSeconds(45), cache.TimeToLive);
        Assert.Same(cache, typeof(DalService)
            .GetField("_principalCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dal));
        Assert.IsType<PrincipalResolver>(provider.GetRequiredService<IPrincipalResolver>());
    }

    [Fact]
    public async Task TestAUserWhoseRoleIsMissingIsRefusedAndNothingIsCached()
    {
        var host = NewHost();
        await using (var db = _sideDoor.GetContext())
        {
            (await db.Users.SingleAsync(u => u.Value == AnalystId)).RoleId = 99;
            await db.SaveChangesAsync();
        }

        var result = await JwtAsync(host, "ana");

        Assert.False(result.Succeeded);
        Assert.False(host.Cache.TryGet(AnalystId, 99, out _));
    }
}
