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
/// Stage 9.9 (S50 §6, D11; §8 DA1–DA6) — who may read and write the archive, the backtests and the committees, and who
/// reads the governance evidence pack.
///
/// No new permission. Reads are the register's (<c>RequireRiskmanagement</c>); archiving and reopening are closing
/// (<c>RequireCloseRisk</c>); the archive's quarterly review and submitting to a committee are deciding
/// (<c>RequireMgmtReviewAccess</c>); a backtest is the 9.8 event audience (<c>RequireSubmitRisk</c>); constituting a
/// committee is a Phase 0 act (<c>RequireAdminOnly</c>); a vote needs a session and membership, which the service checks.
/// One new policy: <c>RequireAssuranceEvidence</c>, the evidence pack for administrators and the third line — evaluated
/// here, accepted and denied.
/// </summary>
[TestSubject(typeof(RiskCommitteesController))]
public class DecisionCycleAuthorizationTest
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["RiskArchiveController.GetArchives"] = "RequireRiskmanagement",
        ["RiskArchiveController.GetRiskArchives"] = "RequireRiskmanagement",
        ["RiskArchiveController.Archive"] = "RequireCloseRisk",
        ["RiskArchiveController.Reopen"] = "RequireCloseRisk",
        ["RiskArchiveController.Review"] = "RequireMgmtReviewAccess",

        ["BacktestingController.GetReport"] = "RequireRiskmanagement",
        ["BacktestingController.GetIncident"] = "RequireRiskmanagement",
        ["BacktestingController.Assess"] = "RequireSubmitRisk",

        ["RiskCommitteesController.GetCommittees"] = "RequireRiskmanagement",
        ["RiskCommitteesController.GetCommittee"] = "RequireRiskmanagement",
        ["RiskCommitteesController.GetDecisions"] = "RequireRiskmanagement",
        ["RiskCommitteesController.GetDecision"] = "RequireRiskmanagement",
        ["RiskCommitteesController.CreateCommittee"] = "RequireAdminOnly",
        ["RiskCommitteesController.UpdateCommittee"] = "RequireAdminOnly",
        ["RiskCommitteesController.RetireCommittee"] = "RequireAdminOnly",
        ["RiskCommitteesController.AddMember"] = "RequireAdminOnly",
        ["RiskCommitteesController.RemoveMember"] = "RequireAdminOnly",
        ["RiskCommitteesController.OpenDecision"] = "RequireMgmtReviewAccess",
        ["RiskCommitteesController.Withdraw"] = "RequireMgmtReviewAccess",
        ["RiskCommitteesController.Vote"] = "RequireValidUser"
    };

    private static readonly Type[] Controllers =
        [typeof(RiskArchiveController), typeof(BacktestingController), typeof(RiskCommitteesController)];

    private static List<(Type Controller, MethodInfo Action)> Actions() => Controllers
        .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Select(m => (c, m)))
        .ToList();

    private static bool IsWrite(MethodInfo action) => action.GetCustomAttributes<HttpMethodAttribute>()
        .Any(h => h.HttpMethods.Any(m => m is "POST" or "PUT" or "DELETE"));

    /// <summary>DA1 — twenty actions, each with exactly its policy as a plain <see cref="AuthorizeAttribute"/>, none anonymous.</summary>
    [Fact]
    public void TestDA1_EachActionCarriesExactlyItsPolicy()
    {
        var actions = Actions();
        Assert.Equal(Expected.Keys.OrderBy(k => k), actions.Select(a => $"{a.Controller.Name}.{a.Action.Name}").OrderBy(k => k));

        foreach (var (controller, action) in actions)
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
            var only = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal(Expected[$"{controller.Name}.{action.Name}"], only.Policy);
        }
    }

    /// <summary>DA2 — no write carries the register's read policy: the read audience never edits through it.</summary>
    [Fact]
    public void TestDA2_NoWriteCarriesTheReadPolicy()
    {
        var writes = Actions().Where(a => IsWrite(a.Action)).ToList();

        Assert.Equal(12, writes.Count);
        Assert.All(writes, w => Assert.NotEqual("RequireRiskmanagement",
            w.Action.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));
    }

    /// <summary>DA3 — each class requires a session and allows no anonymous access.</summary>
    [Fact]
    public void TestDA3_EachClassRequiresAValidUser()
    {
        foreach (var controller in Controllers)
        {
            Assert.Equal("RequireValidUser", Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }
    }

    /// <summary>DA4 — the evidence pack endpoints carry the new policy.</summary>
    [Theory]
    [InlineData(nameof(AuditTrailController.GetEvidence))]
    [InlineData(nameof(AuditTrailController.GetEvidenceReport))]
    public void TestDA4_TheEvidencePackCarriesTheAssurancePolicy(string actionName)
    {
        var action = typeof(AuditTrailController).GetMethod(actionName)!;

        Assert.Equal("RequireAssuranceEvidence",
            Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false)).Policy);
    }

    // --- the new policy, evaluated --------------------------------------------------------------------------------

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

    /// <summary>DA5 — the evidence pack: administrators and the third line read it; the register's audience does not.</summary>
    [Fact]
    public async Task TestDA5_TheEvidencePolicyAdmitsAdministratorsAndTheThirdLine()
    {
        Assert.True(await Allows("RequireAssuranceEvidence", "GET", Role("Admin")));
        Assert.True(await Allows("RequireAssuranceEvidence", "GET", Role("Administrator")));
        Assert.True(await Allows("RequireAssuranceEvidence", "GET", Permission(ThirdLineAssurance.PermissionKey)));

        Assert.False(await Allows("RequireAssuranceEvidence", "GET"));
        Assert.False(await Allows("RequireAssuranceEvidence", "GET", Permission("riskmanagement")));
        Assert.False(await Allows("RequireAssuranceEvidence", "GET", Permission("reports")));
        Assert.False(await Allows("RequireAssuranceEvidence", "GET", Role("ThirdLineAuditor")));
    }

    /// <summary>
    /// DA6 — the third line passes the register's read policy with the seeded read permissions, and every write policy of
    /// Stage 9.9 refuses it even holding the permission that policy names.
    /// </summary>
    [Fact]
    public async Task TestDA6_TheSeededRoleReadsAndCannotWrite()
    {
        var auditor = ThirdLineAssurance.ReadPermissions.Select(Permission)
            .Append(Permission(ThirdLineAssurance.PermissionKey)).ToArray();

        Assert.True(await Allows("RequireRiskmanagement", "GET", auditor));

        Assert.False(await Allows("RequireCloseRisk", "POST", auditor.Append(Permission("close_risks")).ToArray()));
        Assert.False(await Allows("RequireSubmitRisk", "PUT", auditor.Append(Permission("submit_risks")).ToArray()));
        Assert.False(await Allows("RequireMgmtReviewAccess", "POST", auditor.Append(Permission("review_high")).ToArray()));
        Assert.False(await Allows("RequireAdminOnly", "POST", auditor.Append(Role("Admin")).ToArray()));
        Assert.False(await Allows("RequireValidUser", "POST", auditor));
        Assert.False(await Allows("RequireRiskmanagement", "POST", auditor));
    }
}
