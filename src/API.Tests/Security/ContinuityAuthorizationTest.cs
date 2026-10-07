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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.3 (S43 §6, D11; §8) — who may read and write continuity data.
///
/// Four audiences: reads under the new <c>RequireContinuityRead</c> policy (the union of the risk-register
/// audience and the two writer audiences), BIA and dependencies under <c>bia_manage</c>, restoration tests
/// under <c>restoration_test_record</c>, and the parameters under <c>RequireAdminOnly</c>. The reflection
/// tests pin each attribute; the policy tests evaluate the real policies, accepted and denied, rather
/// than trusting their names.
/// </summary>
[TestSubject(typeof(ContinuityController))]
public class ContinuityAuthorizationTest
{
    private static readonly string[] Reads =
    [
        nameof(ContinuityController.GetSubjects), nameof(ContinuityController.GetProfile),
        nameof(ContinuityController.GetRestorationTests), nameof(ContinuityController.GetRestorationVerificationMetric),
        nameof(ContinuityController.GetSettings)
    ];

    private static readonly string[] BiaWrites =
    [
        nameof(ContinuityController.SaveBia), nameof(ContinuityController.DeleteBia),
        nameof(ContinuityController.AddDependency), nameof(ContinuityController.DeleteDependency)
    ];

    private static readonly string[] TestWrites =
    [
        nameof(ContinuityController.RecordRestorationTest), nameof(ContinuityController.VoidRestorationTest)
    ];

    private static List<MethodInfo> Actions() => typeof(ContinuityController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    private static AuthorizeAttribute OnlyAuthorize(MethodInfo action)
    {
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
        return Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
    }

    [Fact]
    public void TestThereAreTwelveActionsEachWithExactlyOneAttribute()
    {
        var actions = Actions();

        Assert.Equal(12, actions.Count);
        Assert.Equal(actions.Select(a => a.Name).OrderBy(n => n),
            Reads.Concat(BiaWrites).Concat(TestWrites).Append(nameof(ContinuityController.SaveSettings)).OrderBy(n => n));
    }

    [Fact]
    public void TestReadsCarryExactlyTheContinuityReadPolicy()
    {
        foreach (var action in Actions().Where(a => Reads.Contains(a.Name)))
        {
            var only = OnlyAuthorize(action);
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal("RequireContinuityRead", only.Policy);
        }
    }

    [Fact]
    public void TestBiaAndDependencyWritesCarryExactlyBiaManage()
    {
        foreach (var action in Actions().Where(a => BiaWrites.Contains(a.Name)))
            Assert.Equal("bia_manage", Assert.IsType<PermissionAuthorizeAttribute>(OnlyAuthorize(action)).Permission);
    }

    [Fact]
    public void TestRestorationTestWritesCarryExactlyRestorationTestRecord()
    {
        foreach (var action in Actions().Where(a => TestWrites.Contains(a.Name)))
            Assert.Equal("restoration_test_record",
                Assert.IsType<PermissionAuthorizeAttribute>(OnlyAuthorize(action)).Permission);
    }

    [Fact]
    public void TestSavingTheParametersCarriesExactlyTheAdminOnlyPolicy()
    {
        var only = OnlyAuthorize(typeof(ContinuityController).GetMethod(nameof(ContinuityController.SaveSettings))!);

        Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
        Assert.Equal("RequireAdminOnly", only.Policy);
    }

    [Fact]
    public void TestTheClassRequiresAValidUserAndAllowsNoAnonymous()
    {
        var policies = typeof(ContinuityController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Equal("RequireValidUser", Assert.Single(policies).Policy);
        Assert.Empty(typeof(ContinuityController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    // --- the policies themselves, evaluated ------------------------------------------------------

    private static async Task<bool> Allows(string policyName, params Claim[] claims)
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
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "Bearer"));

        return (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded;
    }

    private static Claim Permission(string key) => new("Permission", key);

    private static Claim Role(string name) => new(ClaimTypes.Role, name);

    private const string Read = "RequireContinuityRead";
    private const string ManageBia = "Permissionbia_manage";
    private const string RecordTests = "Permissionrestoration_test_record";
    private const string AdminOnly = "RequireAdminOnly";

    /// <summary>The read policy is the union of the risk-register audience and both writer audiences.</summary>
    [Fact]
    public async Task TestTheReadPolicyAcceptsEveryAudienceItUnites()
    {
        Assert.True(await Allows(Read, Permission("riskmanagement")));
        Assert.True(await Allows(Read, Permission("bia_manage")));
        Assert.True(await Allows(Read, Permission("restoration_test_record")));
        Assert.True(await Allows(Read, Role("Admin")));
        Assert.True(await Allows(Read, Role("Administrator")));
    }

    [Fact]
    public async Task TestTheReadPolicyDeniesEveryoneElse()
    {
        Assert.False(await Allows(Read));
        Assert.False(await Allows(Read, Permission("submit_risks")));
        Assert.False(await Allows(Read, Permission("hosts")));
        Assert.False(await Allows(Read, Permission("business_risk_review")));
        Assert.False(await Allows(Read, Role("user")));
    }

    /// <summary>bia_manage: neither the risk manager, nor the tester, nor the legacy Administrator role
    /// without Admin writes a BIA.</summary>
    [Fact]
    public async Task TestBiaManageIsItsOwnAudience()
    {
        Assert.True(await Allows(ManageBia, Permission("bia_manage")));
        Assert.True(await Allows(ManageBia, Role("Admin")));

        Assert.False(await Allows(ManageBia, Permission("riskmanagement")));
        Assert.False(await Allows(ManageBia, Permission("restoration_test_record")));
        Assert.False(await Allows(ManageBia, Role("Administrator")));
    }

    [Fact]
    public async Task TestRestorationTestRecordIsItsOwnAudience()
    {
        Assert.True(await Allows(RecordTests, Permission("restoration_test_record")));
        Assert.True(await Allows(RecordTests, Role("Admin")));

        Assert.False(await Allows(RecordTests, Permission("bia_manage")));
        Assert.False(await Allows(RecordTests, Permission("riskmanagement")));
    }

    /// <summary>The parameters: administrators only — not the people whose work they judge.</summary>
    [Fact]
    public async Task TestTheParametersAreForAdministratorsOnly()
    {
        Assert.True(await Allows(AdminOnly, Role("Admin")));
        Assert.True(await Allows(AdminOnly, Role("Administrator")));

        Assert.False(await Allows(AdminOnly, Permission("bia_manage")));
        Assert.False(await Allows(AdminOnly, Permission("restoration_test_record")));
        Assert.False(await Allows(AdminOnly, Permission("riskmanagement")));
    }

    // --- ContinuityAccess: the flags the profile reports -------------------------------------------

    private static ClaimsPrincipal With(params Claim[] claims) =>
        new(new ClaimsIdentity(claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "Bearer"));

    [Fact]
    public void TestContinuityAccessMirrorsThePermissionAttribute()
    {
        Assert.True(ContinuityAccess.CanManageBia(With(Permission("bia_manage"))));
        Assert.True(ContinuityAccess.CanManageBia(With(Role("Admin"))));
        Assert.False(ContinuityAccess.CanManageBia(With(Role("Administrator"))));
        Assert.False(ContinuityAccess.CanManageBia(With(Permission("restoration_test_record"))));
        Assert.False(ContinuityAccess.CanManageBia(null));

        Assert.True(ContinuityAccess.CanRecordTests(With(Permission("restoration_test_record"))));
        Assert.False(ContinuityAccess.CanRecordTests(With(Permission("bia_manage"))));
        Assert.False(ContinuityAccess.CanRecordTests(null));
    }
}
