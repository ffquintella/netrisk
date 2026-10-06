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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.1 (S41 §6, D9; §8) — who may use the linkage chain.
///
/// The decision under test is that the chain's audience is exactly the audience of the legacy
/// "Entity" field it coexists with: the <c>RequireRiskmanagement</c> policy, which accepts the
/// <c>Administrator</c> role or the <c>riskmanagement</c> claim. <c>[PermissionAuthorize("riskmanagement")]</c>
/// looks equivalent and is not — it accepts the <c>Admin</c> role instead — so the reflection tests pin
/// the attribute, and the negative tests evaluate the real policy rather than trusting its name.
/// </summary>
[TestSubject(typeof(RiskChainController))]
public class RiskChainAuthorizationTest
{
    private const string Policy = "RequireRiskmanagement";

    private static IEnumerable<MethodInfo> Actions(Type controller) => controller
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void TestEveryActionCarriesExactlyTheRiskManagementPolicy()
    {
        var actions = Actions(typeof(RiskChainController)).ToList();

        Assert.Equal(6, actions.Count);

        foreach (var action in actions)
        {
            var authorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();

            var only = Assert.Single(authorize);
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal(Policy, only.Policy);

            Assert.Empty(action.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: false));
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
        }

        Assert.Empty(typeof(RiskChainController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Empty(typeof(RiskChainController).GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true));
    }

    /// <summary>The same policy, at action and controller level, as the three legacy
    /// <c>/Risks/{id}/Entity</c> actions.</summary>
    [Fact]
    public void TestThePolicyIsTheOneTheLegacyEntityLinkUses()
    {
        var legacy = new[]
        {
            nameof(RisksController.GetRiskEntity), nameof(RisksController.AssociateRiskEntity),
            nameof(RisksController.DeleteRiskEntity)
        };

        foreach (var name in legacy)
        {
            var action = typeof(RisksController).GetMethod(name)!;
            Assert.Equal(Policy, Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false)).Policy);
        }

        static string[] ClassPolicies(Type t) => t.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy ?? "").OrderBy(p => p).ToArray();

        Assert.Equal(ClassPolicies(typeof(RisksController)), ClassPolicies(typeof(RiskChainController)));
    }

    // --- the policy itself, evaluated --------------------------------------------------------------

    private static async Task<bool> PolicyAllows(params Claim[] claims)
    {
        var dal = new InMemoryDalService(Guid.NewGuid().ToString());
        using (var db = dal.GetContext())
        {
            db.Users.Add(new User
            {
                Value = 1, Enabled = true, Name = "analyst", Login = "analyst", Email = "a@x.test", Type = "local",
                Password = "secret"u8.ToArray(), RoleId = 1
            });
            db.SaveChanges();
        }

        // Internal to the API; instantiated as the host's DI would, with the collaborators it never
        // touches for a legacy policy name substituted.
        var providerType = typeof(ApiBaseController).Assembly.GetType("API.Security.PermissionPolicyProvider")!;
        var provider = (IAuthorizationPolicyProvider)Activator.CreateInstance(providerType,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            Substitute.For<IFaceIDService>(), Substitute.For<IPluginsService>())!;

        var policy = await provider.GetPolicyAsync(Policy);
        Assert.NotNull(policy);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(dal));

        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "Bearer"));

        return (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded;
    }

    [Fact]
    public async Task TestAValidUserWithoutTheClaimOrTheRoleIsDenied()
    {
        Assert.False(await PolicyAllows());
        Assert.False(await PolicyAllows(new Claim("Permission", "hosts"), new Claim(ClaimTypes.Role, "user")));
    }

    /// <summary>
    /// The <c>Admin</c> role — what <c>[PermissionAuthorize]</c> would have accepted — is not this
    /// policy's administrator. Pinned so the difference D9 is about cannot be "fixed" by accident.
    /// </summary>
    [Fact]
    public async Task TestTheAdminRoleAloneIsNotTheAdministratorThisPolicyAccepts()
    {
        Assert.False(await PolicyAllows(new Claim(ClaimTypes.Role, "Admin")));
    }

    [Fact]
    public async Task TestTheRiskManagementClaimIsAccepted()
    {
        Assert.True(await PolicyAllows(new Claim("Permission", "riskmanagement")));
    }

    [Fact]
    public async Task TestTheAdministratorRoleIsAccepted()
    {
        Assert.True(await PolicyAllows(new Claim(ClaimTypes.Role, "Administrator")));
    }

    // --- HostAccess: the second gate on host targets ---------------------------------------------

    private static ClaimsPrincipal With(params Claim[] claims) =>
        new(new ClaimsIdentity(claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "Bearer"));

    [Fact]
    public void TestHostAccessDeniesWithoutHostsOrAdmin()
    {
        Assert.False(HostAccess.CanRead(With()));
        Assert.False(HostAccess.CanRead(With(new Claim("Permission", "riskmanagement"))));
        Assert.False(HostAccess.CanRead(With(new Claim(ClaimTypes.Role, "Administrator"))));
        Assert.False(HostAccess.CanRead(With(new Claim("Permission", "hosts_admin"))));

        // A caller with no principal at all — a job, the console — is not unrestricted.
        Assert.False(HostAccess.CanRead(null));
    }

    [Fact]
    public void TestHostAccessAcceptsTheHostsClaimOrTheAdminRole()
    {
        Assert.True(HostAccess.CanRead(With(new Claim("Permission", "hosts"))));
        Assert.True(HostAccess.CanRead(With(new Claim(ClaimTypes.Role, "Admin"))));
    }
}
