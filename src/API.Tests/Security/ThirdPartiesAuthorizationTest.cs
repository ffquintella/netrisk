using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Model.DecisionCycle;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.10 (S51 §6, §8 TA1–TA6) — who reads and who writes the third-party register. Reads take the new
/// <c>RequireThirdPartyRead</c> policy — the risk register's audience or whoever manages the register —, every write the
/// one new permission <c>third_party_manage</c>; both policies are evaluated here, accepted and denied, and the third line
/// reads and is refused every write even holding the permission.
/// </summary>
[TestSubject(typeof(ThirdPartiesController))]
public class ThirdPartiesAuthorizationTest
{
    private const string Read = "RequireThirdPartyRead";
    private const string Manage = "Permissionthird_party_manage";

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["GetThirdParties"] = Read,
        ["GetThirdParty"] = Read,
        ["GetConcentration"] = Read,
        ["GetByEntity"] = Read,
        ["GetAssessment"] = Read,
        ["GetSbom"] = Read,
        ["GetHistory"] = Read,
        ["Create"] = Manage,
        ["Update"] = Manage,
        ["Delete"] = Manage,
        ["Link"] = Manage,
        ["Unlink"] = Manage,
        ["SetSubprocessors"] = Manage,
        ["SetDataLocations"] = Manage,
        ["RecordAssessment"] = Manage,
        ["ReplaceAnswers"] = Manage,
        ["VoidAssessment"] = Manage,
        ["ImportSbom"] = Manage,
        ["DeleteSbom"] = Manage
    };

    private static List<MethodInfo> Actions() => typeof(ThirdPartiesController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    private static bool IsWrite(MethodInfo action) => action.GetCustomAttributes<HttpMethodAttribute>()
        .Any(h => h.HttpMethods.Any(m => m is "POST" or "PUT" or "DELETE"));

    /// <summary>TA1 — nineteen actions, each with exactly its policy, none anonymous; the writes through the permission attribute.</summary>
    [Fact]
    public void TestTA1_EachActionCarriesExactlyItsPolicy()
    {
        var actions = Actions();
        Assert.Equal(Expected.Keys.OrderBy(k => k), actions.Select(a => a.Name).OrderBy(k => k));

        foreach (var action in actions)
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
            var only = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
            Assert.Equal(Expected[action.Name], only.Policy);
            Assert.Equal(IsWrite(action) ? typeof(PermissionAuthorizeAttribute) : typeof(AuthorizeAttribute), only.GetType());
        }
    }

    /// <summary>TA2 — twelve writes, none with the read policy, and every read is a GET.</summary>
    [Fact]
    public void TestTA2_NoWriteCarriesTheReadPolicy()
    {
        var actions = Actions();
        Assert.Equal(12, actions.Count(IsWrite));
        Assert.All(actions.Where(IsWrite), w => Assert.Equal(Manage, w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));
        Assert.All(actions.Where(a => !IsWrite(a)), r =>
            Assert.Equal(new[] { "GET" }, r.GetCustomAttributes<HttpMethodAttribute>().SelectMany(h => h.HttpMethods)));
    }

    /// <summary>TA3 — the class requires a session and allows no anonymous access.</summary>
    [Fact]
    public void TestTA3_TheClassRequiresAValidUser()
    {
        Assert.Equal("RequireValidUser",
            Assert.Single(typeof(ThirdPartiesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
        Assert.Empty(typeof(ThirdPartiesController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    // --- the policies, evaluated --------------------------------------------------------------------------------------

    private static async Task<bool> Allows(string policyName, string method, params Claim[] claims)
    {
        var dal = new InMemoryDalService(Guid.NewGuid().ToString());
        using (var db = dal.GetContext())
        {
            db.Users.Add(new User
            {
                Value = 1, Enabled = true, Name = "someone", Login = "someone", Email = "s@x.test", Type = "local",
                Password = "secret"u8.ToArray()
            });
            db.SaveChanges();
        }

        var providerType = typeof(ApiBaseController).Assembly.GetType("API.Security.PermissionPolicyProvider")!;
        var provider = (IAuthorizationPolicyProvider)Activator.CreateInstance(providerType,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            Substitute.For<IFaceIDService>(), Substitute.For<IPluginsService>())!;
        var policy = await provider.GetPolicyAsync(policyName);
        Assert.NotNull(policy);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(dal));
        services.AddSingleton<IAuthorizationHandler>(
            new PermissionAuthorizationHandler(NullLogger<PermissionAuthorizationHandler>.Instance));
        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Prepend(new Claim(ClaimTypes.Name, "someone")), "Bearer"));

        return (await authorization.AuthorizeAsync(principal, http, policy!)).Succeeded;
    }

    private static Claim Permission(string key) => new("Permission", key);

    private static Claim Role(string name) => new(ClaimTypes.Role, name);

    /// <summary>TA4 — the read policy: the register's audience and the register's managers; nobody else.</summary>
    [Fact]
    public async Task TestTA4_TheReadPolicyAdmitsTheRegisterAudienceAndItsManagers()
    {
        Assert.True(await Allows(Read, "GET", Role("Admin")));
        Assert.True(await Allows(Read, "GET", Role("Administrator")));
        Assert.True(await Allows(Read, "GET", Permission("riskmanagement")));
        Assert.True(await Allows(Read, "GET", Permission("third_party_manage")));

        Assert.False(await Allows(Read, "GET"));
        Assert.False(await Allows(Read, "GET", Permission("reports")));
        Assert.False(await Allows(Read, "GET", Permission("governance")));
        Assert.False(await Allows(Read, "GET", Role("ThirdLineAuditor")));
    }

    /// <summary>TA5 — the write permission: an administrator or its holder; the register's read audience does not write.</summary>
    [Fact]
    public async Task TestTA5_TheWritePermissionAdmitsOnlyItsHolders()
    {
        Assert.True(await Allows(Manage, "POST", Permission("third_party_manage")));
        Assert.True(await Allows(Manage, "PUT", Role("Admin")));

        Assert.False(await Allows(Manage, "POST"));
        Assert.False(await Allows(Manage, "POST", Permission("riskmanagement")));
        Assert.False(await Allows(Manage, "DELETE", Role("Administrator")));
        Assert.False(await Allows(Manage, "PUT", Permission("bia_manage")));
    }

    /// <summary>TA6 — the seeded third line reads the register and is refused every write, holding the permission or the Admin role.</summary>
    [Fact]
    public async Task TestTA6_TheThirdLineReadsAndCannotWrite()
    {
        var auditor = ThirdLineAssurance.ReadPermissions.Select(Permission)
            .Append(Permission(ThirdLineAssurance.PermissionKey)).ToArray();

        Assert.True(await Allows(Read, "GET", auditor));

        Assert.False(await Allows(Manage, "POST", auditor.Append(Permission("third_party_manage")).ToArray()));
        Assert.False(await Allows(Manage, "PUT", auditor.Append(Role("Admin")).ToArray()));
        Assert.False(await Allows(Manage, "DELETE", auditor.Append(Permission("third_party_manage")).ToArray()));
        Assert.False(await Allows(Read, "POST", auditor));
    }
}
