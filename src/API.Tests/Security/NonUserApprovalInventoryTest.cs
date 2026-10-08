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
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.12 (S53 §8, NU1–NU5; T215) — the MIGR-TI/IA Phase 6 prohibitions still hold after the AI inventory exists: no
/// path lets a principal that is not a user accept residual risk, approve an exception, close a material finding or decide
/// anything else.
///
/// The guarantee is "by construction" (S27, the coverage analysis §8): every policy the API builds carries
/// <see cref="ValidUserRequirement"/>, which <see cref="ValidUserRequirementHandler"/> satisfies only for an existing user.
/// This test does not trust a list of "approval endpoints": it enumerates every action of every controller and every
/// method it answers, builds the action's real combined policy through the API's own <c>PermissionPolicyProvider</c>, and
/// evaluates it for a principal that holds every permission any endpoint names and both administrator roles, but is not a
/// user — one named after an inventoried AI model, and the background actor <c>system</c>. Every action must refuse it, for
/// the valid-user reason; the same claims under an existing user's name pass (NU2), so the refusal is the guard and not a
/// permission the test forgot. A new endpoint is covered the day it is written, the Stage 9.12 ones included (NU4).
/// </summary>
[TestSubject(typeof(ValidUserRequirementHandler))]
public class NonUserApprovalInventoryTest
{
    private const string UserName = "approver";
    private const string UserId = "9";

    /// <summary>
    /// The Phase 6 decision acts this enumeration must reach, so it can never pass by evaluating nothing that matters:
    /// accepting and renewing residual risk, counter-signing a review, voting on and withdrawing a committee decision,
    /// recording a management review, withdrawing a Gate A flag, closing a risk, transitioning a finding, adding findings to
    /// an exception, approving a RIPD.
    /// </summary>
    private static readonly string[] DecisionActs =
    [
        "RiskGovernanceController.CreateAcceptance", "RiskGovernanceController.RenewAcceptance",
        "RiskGovernanceController.Countersign", "RiskCommitteesController.Vote", "RiskCommitteesController.Withdraw",
        "MgmtReviewsController.Create", "RiskFlagsController.Withdraw", "RisksController.CloseRisk",
        "VulnerabilitiesController.UpdateLifecycleStatus", "RiskAcceptancesController.AddFindings",
        "DataCatalogueController.ApproveDpia"
    ];

    /// <summary>Authorized by the SCIM role alone, outside every policy — reviewed in <c>ThirdLineReadOnlyInventoryTest</c> (TL-API9).</summary>
    private static readonly string[] RoleOnlyControllers = ["ScimController"];

    private static IEnumerable<Type> Controllers() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        Controllers().SelectMany(c => c
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Select(m => (c, m)));

    private static string Key(Type controller, MethodInfo action) => $"{controller.Name}.{action.Name}";

    private static IEnumerable<string> Verbs(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(h => h.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsAnonymous(Type controller, MethodInfo action) =>
        action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
        || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static IEnumerable<(Type Controller, MethodInfo Action)> Evaluated() =>
        Actions().Where(a => !IsAnonymous(a.Controller, a.Action) && !RoleOnlyControllers.Contains(a.Controller.Name));

    private static IEnumerable<string> EveryPermissionKey() =>
        Actions().SelectMany(a => a.Action.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true)
                .Concat(a.Controller.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true)))
            .Select(p => p.Permission)
            .Concat([
                "riskmanagement", "governance", "assessments", "submit_risks", "delete_risk", "close_risks",
                "review_insignificant", "review_low", "review_medium", "review_high", "review_veryhigh", "plan_mitigations",
                "accept_mitigation", "modify_risks", "compliance", "reports"
            ])
            .Distinct(StringComparer.Ordinal);

    /// <summary>Every permission, both administrator roles and global scope, under <paramref name="name"/>.</summary>
    private static ClaimsPrincipal Omnipotent(string name, string sid)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name), new(ClaimTypes.Sid, sid),
            new(ClaimTypes.Role, "Admin"), new(ClaimTypes.Role, "Administrator"), new("scope", "global")
        };
        claims.AddRange(EveryPermissionKey().Select(k => new Claim("Permission", k)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    /// <summary>The API's store with one user — the only principal the valid-user requirement admits.</summary>
    private static InMemoryDalService DalWithOneUser()
    {
        var dal = new InMemoryDalService(Guid.NewGuid().ToString());
        using var db = dal.GetContext();
        db.Users.Add(new User
        {
            Value = int.Parse(UserId), Enabled = true, Name = UserName, Login = UserName, Email = "a@x.test", Type = "local",
            Password = "secret"u8.ToArray(), Admin = true
        });

        // An inventoried model in the same store: being in the inventory makes nothing a principal.
        db.AiModels.Add(new AiModel
        {
            Id = 77, Name = "ai-model:admissions-triage", Purpose = "Ranks applications.", Kind = DAL.Enums.AiModelKind.Classification,
            Source = DAL.Enums.AiModelSource.InHouse, Version = "1", Status = DAL.Enums.AiModelStatus.Production,
            MaxEvaluationAgeDays = 90, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return dal;
    }

    private static IAuthorizationPolicyProvider Provider()
    {
        var faceId = Substitute.For<IFaceIDService>();
        faceId.IsFaceIDPluginEnabled().Returns(false);

        var type = typeof(ApiBaseController).Assembly.GetType("API.Security.PermissionPolicyProvider")!;
        return (IAuthorizationPolicyProvider)Activator.CreateInstance(type,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            faceId, Substitute.For<IPluginsService>())!;
    }

    private static IAuthorizationService AuthorizationService(InMemoryDalService dal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(dal));
        services.AddSingleton<IAuthorizationHandler>(
            new PermissionAuthorizationHandler(NullLogger<PermissionAuthorizationHandler>.Instance));
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static async Task<AuthorizationResult> Evaluate(IAuthorizationService authorization,
        IAuthorizationPolicyProvider provider, ClaimsPrincipal user, Type controller, MethodInfo action, string verb)
    {
        var data = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Cast<IAuthorizeData>()
            .ToList();
        var policy = await AuthorizationPolicy.CombineAsync(provider, data);
        Assert.NotNull(policy);

        var http = new DefaultHttpContext();
        http.Request.Method = verb;
        http.Request.RouteValues["id"] = "2";
        return await authorization.AuthorizeAsync(user, http, policy!);
    }

    private static bool RefusedAsNotAUser(AuthorizationResult result) =>
        !result.Succeeded && result.Failure!.FailureReasons.Any(r => r.Handler is ValidUserRequirementHandler);

    /// <summary>
    /// NU1 (T215) — every action of the API, every method, refuses a principal that is not a user, for the valid-user reason,
    /// though it holds every permission and both administrator roles: an inventoried AI model's name, and the background
    /// actor <c>system</c>. No decision of Phase 6 — or anything else — is reachable without a person.
    /// </summary>
    [Theory]
    [InlineData("ai-model:admissions-triage", "77")]
    [InlineData("system", "0")]
    public async Task TestNU1_EveryActionRefusesAPrincipalThatIsNotAUser(string name, string sid)
    {
        var provider = Provider();
        var authorization = AuthorizationService(DalWithOneUser());
        var nonUser = Omnipotent(name, sid);

        var permitted = new List<string>();
        var otherReason = new List<string>();
        var evaluated = 0;

        foreach (var (controller, action) in Evaluated())
        foreach (var verb in Verbs(action))
        {
            evaluated++;
            var result = await Evaluate(authorization, provider, nonUser, controller, action, verb);
            if (result.Succeeded) permitted.Add($"{verb} {Key(controller, action)}");
            else if (!RefusedAsNotAUser(result)) otherReason.Add($"{verb} {Key(controller, action)}");
        }

        Assert.True(permitted.Count == 0, $"A principal that is not a user ('{name}') reaches:\n  " + string.Join("\n  ", permitted));
        Assert.True(otherReason.Count == 0,
            "Refused, but not by the valid-user requirement — the policy lacks it:\n  " + string.Join("\n  ", otherReason));

        // Not vacuous: the API has hundreds of actions.
        Assert.True(evaluated > 500, $"Only {evaluated} action(s) were evaluated — the enumeration is broken.");
    }

    /// <summary>
    /// NU2 — the control: the same claims under an existing user's name pass every one of those actions, so NU1's refusal is
    /// the valid-user requirement and not a permission or role the test forgot.
    /// </summary>
    [Fact]
    public async Task TestNU2_TheSameClaimsAsAUserPass()
    {
        var provider = Provider();
        var authorization = AuthorizationService(DalWithOneUser());
        var user = Omnipotent(UserName, UserId);

        var refused = new List<string>();
        foreach (var (controller, action) in Evaluated())
        foreach (var verb in Verbs(action))
            if (!(await Evaluate(authorization, provider, user, controller, action, verb)).Succeeded)
                refused.Add($"{verb} {Key(controller, action)}");

        Assert.True(refused.Count == 0, "Refused even to a user holding everything:\n  " + string.Join("\n  ", refused));
    }

    /// <summary>
    /// NU3 — the Phase 6 decision acts are in the enumeration and none is anonymous: accepting or renewing residual risk,
    /// counter-signing and recording a review, voting, withdrawing a Gate A flag, closing a risk, transitioning a finding,
    /// adding findings to an exception, approving a RIPD. A rename or a move that took one out of NU1 fails here.
    /// </summary>
    [Fact]
    public void TestNU3_ThePhase6DecisionActsAreEvaluated()
    {
        var evaluated = Evaluated().Select(a => Key(a.Controller, a.Action)).ToHashSet(StringComparer.Ordinal);

        Assert.All(DecisionActs, act => Assert.True(evaluated.Contains(act), $"{act} is not evaluated by NU1."));
    }

    /// <summary>
    /// NU4 — no Stage 9.12 route decides: the inventory's controller depends on the inventory service alone (no acceptance,
    /// review, committee, workflow, flag or finding service), every one of its actions is in NU1's enumeration, and none is
    /// named or routed as a decision. Governance, never use (S53 D1).
    /// </summary>
    [Fact]
    public void TestNU4_NoStage912RouteIsADecision()
    {
        var parameters = typeof(AiModelsController).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(IAiGovernanceService), parameters);
        Assert.DoesNotContain(parameters, t => t == typeof(IRiskAcceptancesService) || t == typeof(IMgmtReviewsService) ||
                                               t == typeof(IRiskCommitteesService) || t == typeof(IRiskWorkflowService) ||
                                               t == typeof(IRiskFlagsService) || t == typeof(IFindingLifecycleService) ||
                                               t == typeof(IRiskArchiveService) || t == typeof(IEntityRiskReviewersService));

        var actions = Actions().Where(a => a.Controller == typeof(AiModelsController)).ToList();
        Assert.Equal(16, actions.Count);
        Assert.All(actions, a => Assert.Contains(Evaluated(), e => e == a));

        string[] decisionWords = ["Approve", "Accept", "Review", "Vote", "Countersign", "Close", "Renew", "Decide", "Decision"];
        foreach (var (_, action) in actions)
        {
            var routes = action.GetCustomAttributes<RouteAttribute>().Select(r => r.Template ?? string.Empty).Append(action.Name);
            Assert.DoesNotContain(routes, r => decisionWords.Any(w => r.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>
    /// NU5 — on a decision act (accepting residual risk), a principal carrying every claim but no name is refused by the same
    /// requirement, and so is the user the store holds once its login no longer matches — the check is on the person in the
    /// store, not on the claims a token carries.
    /// </summary>
    [Fact]
    public async Task TestNU5_TheGuardReadsTheStoreNotTheClaims()
    {
        var provider = Provider();
        var dal = DalWithOneUser();
        var authorization = AuthorizationService(dal);
        var accept = typeof(RiskGovernanceController).GetMethod(nameof(RiskGovernanceController.CreateAcceptance))!;

        Assert.True((await Evaluate(authorization, provider, Omnipotent(UserName, UserId), typeof(RiskGovernanceController),
            accept, "POST")).Succeeded);

        var anonymousClaims = new ClaimsPrincipal(new ClaimsIdentity(Omnipotent(UserName, UserId).Claims
            .Where(c => c.Type != ClaimTypes.Name), "Bearer"));
        Assert.True(RefusedAsNotAUser(await Evaluate(authorization, provider, anonymousClaims,
            typeof(RiskGovernanceController), accept, "POST")));

        using (var db = dal.GetContext())
        {
            db.Users.Single(u => u.Value == int.Parse(UserId)).Login = "someone-else";
            db.SaveChanges();
        }

        Assert.True(RefusedAsNotAUser(await Evaluate(authorization, provider, Omnipotent(UserName, UserId),
            typeof(RiskGovernanceController), accept, "POST")));
    }
}
