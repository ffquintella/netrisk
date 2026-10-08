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
/// Stage 9.11 (S52 §6, DA1–DA6) — who reads and who writes the LGPD data catalogue. Reads take the new
/// <c>RequireDataCatalogueRead</c> policy — the risk register's audience or whoever maintains the catalogue —, the
/// catalogue, requirement and RIPD writes the one new permission <c>data_catalogue_manage</c>, and a risk's requirement links
/// <c>RequireRiskmanagement</c>, the policy of the linkage chain; the policies are evaluated here, accepted and denied, and
/// the third line reads and is refused every write even holding the permission.
/// </summary>
[TestSubject(typeof(DataCatalogueController))]
public class DataCatalogueAuthorizationTest
{
    private const string Read = "RequireDataCatalogueRead";
    private const string Manage = "Permissiondata_catalogue_manage";
    private const string RiskPolicy = "RequireRiskmanagement";

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["GetRecords"] = Read,
        ["GetRecord"] = Read,
        ["GetRecordHistory"] = Read,
        ["GetRequirements"] = Read,
        ["GetRequirementHistory"] = Read,
        ["GetDpias"] = Read,
        ["GetDpia"] = Read,
        ["GetDpiaHistory"] = Read,
        ["SaveRecord"] = Manage,
        ["CreateRequirement"] = Manage,
        ["UpdateRequirement"] = Manage,
        ["DeleteRequirement"] = Manage,
        ["CreateDpia"] = Manage,
        ["UpdateDpia"] = Manage,
        ["LinkDpia"] = Manage,
        ["UnlinkDpia"] = Manage,
        ["ApproveDpia"] = Manage,
        ["RetireDpia"] = Manage,
        ["GetRiskCompliance"] = RiskPolicy,
        ["LinkRiskRequirement"] = RiskPolicy,
        ["UnlinkRiskRequirement"] = RiskPolicy
    };

    private static List<MethodInfo> Actions() => typeof(DataCatalogueController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    private static bool IsWrite(MethodInfo action) => action.GetCustomAttributes<HttpMethodAttribute>()
        .Any(h => h.HttpMethods.Any(m => m is "POST" or "PUT" or "DELETE"));

    private static bool IsRiskLink(MethodInfo action) =>
        action.Name is nameof(DataCatalogueController.LinkRiskRequirement) or nameof(DataCatalogueController.UnlinkRiskRequirement);

    /// <summary>
    /// DA1 — twenty-one actions, each with exactly its policy, none anonymous; the reads and a risk's requirements through the
    /// plain attribute, the catalogue's writes through the permission attribute.
    /// </summary>
    [Fact]
    public void TestDA1_EachActionCarriesExactlyItsPolicy()
    {
        var actions = Actions();
        Assert.Equal(21, actions.Count);
        Assert.Equal(Expected.Keys.OrderBy(k => k), actions.Select(a => a.Name).OrderBy(k => k));

        foreach (var action in actions)
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
            var only = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
            Assert.Equal(Expected[action.Name], only.Policy);
            Assert.Equal(IsWrite(action) && !IsRiskLink(action) ? typeof(PermissionAuthorizeAttribute) : typeof(AuthorizeAttribute),
                only.GetType());
        }
    }

    /// <summary>DA2 — twelve writes, none with the read policy, ten on the manage permission and two on the risk policy; every read is a GET.</summary>
    [Fact]
    public void TestDA2_NoWriteCarriesTheReadPolicy()
    {
        var actions = Actions();
        var writes = actions.Where(IsWrite).ToList();
        Assert.Equal(12, writes.Count);
        Assert.All(writes, w => Assert.NotEqual(Read, w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));

        var manage = writes.Where(w => !IsRiskLink(w)).ToList();
        Assert.Equal(10, manage.Count);
        Assert.All(manage, w =>
        {
            var only = Assert.Single(w.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
            Assert.IsType<PermissionAuthorizeAttribute>(only);
            Assert.Equal(Manage, only.Policy);
        });

        var risk = writes.Where(IsRiskLink).ToList();
        Assert.Equal(2, risk.Count);
        Assert.All(risk, w => Assert.Equal(RiskPolicy, w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));

        Assert.All(actions.Where(a => !IsWrite(a)), r =>
            Assert.Equal(new[] { "GET" }, r.GetCustomAttributes<HttpMethodAttribute>().SelectMany(h => h.HttpMethods)));
    }

    /// <summary>DA3 — the class requires a session and allows no anonymous access.</summary>
    [Fact]
    public void TestDA3_TheClassRequiresAValidUser()
    {
        Assert.Equal("RequireValidUser",
            Assert.Single(typeof(DataCatalogueController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
        Assert.Empty(typeof(DataCatalogueController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
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

    /// <summary>DA4 — the read policy: the risk register's audience and the catalogue's maintainers; nobody else.</summary>
    [Fact]
    public async Task TestDA4_TheReadPolicyAdmitsTheRegisterAudienceAndItsMaintainers()
    {
        Assert.True(await Allows(Read, "GET", Role("Admin")));
        Assert.True(await Allows(Read, "GET", Role("Administrator")));
        Assert.True(await Allows(Read, "GET", Permission("riskmanagement")));
        Assert.True(await Allows(Read, "GET", Permission("data_catalogue_manage")));

        Assert.False(await Allows(Read, "GET"));
        Assert.False(await Allows(Read, "GET", Permission("reports")));
        Assert.False(await Allows(Read, "GET", Permission("governance")));
        Assert.False(await Allows(Read, "GET", Permission("third_party_manage")));
        Assert.False(await Allows(Read, "GET", Role("ThirdLineAuditor")));
    }

    /// <summary>DA5 — the write permission: an administrator or its holder; the register's read audience does not write.</summary>
    [Fact]
    public async Task TestDA5_TheWritePermissionAdmitsOnlyItsHolders()
    {
        Assert.True(await Allows(Manage, "POST", Permission("data_catalogue_manage")));
        Assert.True(await Allows(Manage, "PUT", Role("Admin")));

        Assert.False(await Allows(Manage, "POST"));
        Assert.False(await Allows(Manage, "POST", Permission("riskmanagement")));
        Assert.False(await Allows(Manage, "DELETE", Role("Administrator")));
        Assert.False(await Allows(Manage, "PUT", Permission("third_party_manage")));
    }

    /// <summary>
    /// DA6 — the seeded third line reads the catalogue and is refused every write: the manage permission, even holding it or the
    /// Admin role, and the risk policy's three write methods.
    /// </summary>
    [Fact]
    public async Task TestDA6_TheThirdLineReadsAndCannotWrite()
    {
        var auditor = ThirdLineAssurance.ReadPermissions.Select(Permission)
            .Append(Permission(ThirdLineAssurance.PermissionKey)).ToArray();

        Assert.True(await Allows(Read, "GET", auditor));

        Assert.False(await Allows(Manage, "POST", auditor.Append(Permission("data_catalogue_manage")).ToArray()));
        Assert.False(await Allows(Manage, "PUT", auditor.Append(Role("Admin")).ToArray()));
        Assert.False(await Allows(Manage, "DELETE", auditor.Append(Permission("data_catalogue_manage")).ToArray()));
        Assert.False(await Allows(Read, "POST", auditor));

        Assert.False(await Allows(RiskPolicy, "POST", auditor));
        Assert.False(await Allows(RiskPolicy, "PUT", auditor));
        Assert.False(await Allows(RiskPolicy, "DELETE", auditor));
    }
}
