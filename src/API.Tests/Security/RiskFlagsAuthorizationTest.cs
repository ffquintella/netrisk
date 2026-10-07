using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.5 (S46 §6, D15) — who may read and declare flags, and who may withdraw a declaration or record a
/// decision.
///
/// No new permission and no new policy. Reading, declaring and re-deriving carry the policy of the risk
/// register (<c>RequireRiskmanagement</c>); withdrawing a declaration and recording a decision carry the policy
/// of the people who accept risk (<c>RequireMgmtReviewAccess</c>), because both change what may be accepted.
/// Each attribute is pinned here by reflection; the two policies themselves are evaluated by their own tests.
/// </summary>
[TestSubject(typeof(RiskFlagsController))]
public class RiskFlagsAuthorizationTest
{
    private static readonly string[] RegisterActions =
    [
        nameof(RiskFlagsController.GetCatalogue), nameof(RiskFlagsController.GetRiskFlags),
        nameof(RiskFlagsController.Refresh), nameof(RiskFlagsController.Declare),
        nameof(RiskFlagsController.GetDecisions), nameof(RiskFlagsController.GetFlagged),
        nameof(RiskFlagsController.GetTopRisks)
    ];

    private static readonly string[] DecisionActions =
    [
        nameof(RiskFlagsController.Withdraw), nameof(RiskFlagsController.RecordDecision)
    ];

    private static List<MethodInfo> Actions() => typeof(RiskFlagsController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    private static AuthorizeAttribute OnlyAuthorize(MethodInfo action)
    {
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
        return Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
    }

    [Fact]
    public void TestThereAreNineActionsEachWithExactlyOneAttribute()
    {
        var actions = Actions();

        Assert.Equal(9, actions.Count);
        Assert.Equal(actions.Select(a => a.Name).OrderBy(n => n),
            RegisterActions.Concat(DecisionActions).OrderBy(n => n));
    }

    [Fact]
    public void TestRegisterActionsCarryExactlyTheRiskManagementPolicy()
    {
        foreach (var action in Actions().Where(a => RegisterActions.Contains(a.Name)))
        {
            var only = OnlyAuthorize(action);
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal("RequireRiskmanagement", only.Policy);
        }
    }

    /// <summary>Withdrawing a Gate A declaration is what makes a risk acceptable again: the acceptors' audience only.</summary>
    [Fact]
    public void TestWithdrawalAndDecisionCarryExactlyTheManagementReviewPolicy()
    {
        foreach (var action in Actions().Where(a => DecisionActions.Contains(a.Name)))
        {
            var only = OnlyAuthorize(action);
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal("RequireMgmtReviewAccess", only.Policy);
        }
    }

    [Fact]
    public void TestTheClassRequiresAValidUserAndAllowsNoAnonymous()
    {
        var policies = typeof(RiskFlagsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Equal("RequireValidUser", Assert.Single(policies).Policy);
        Assert.Empty(typeof(RiskFlagsController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }
}
