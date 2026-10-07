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
/// Stage 9.7 (S48 §6) — who may read the tail of a risk and run the portfolio aggregation, who may declare loss
/// components and correlations, and who may set the appetite's monetary tolerances.
///
/// No new permission and no new policy. The reads and the portfolio (computed on request, never stored) are reads of the
/// register (<c>RequireRiskmanagement</c>); declaring or removing loss components and correlations is editing a risk
/// (<c>RequireSubmitRisk</c>). The three <c>TailLimits</c> actions on <see cref="RiskAppetitesController"/> carry no
/// attribute of their own: they inherit the class's <c>RequireAdminOnly</c>, because the tolerance is what refuses an
/// acceptance, so raising it is a governance decision. Each attribute is pinned here by reflection; the policies
/// themselves are evaluated by their own tests.
/// </summary>
[TestSubject(typeof(TailRiskController))]
public class TailRiskAuthorizationTest
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        [nameof(TailRiskController.GetRisk)] = "RequireRiskmanagement",
        [nameof(TailRiskController.SaveLossComponents)] = "RequireSubmitRisk",
        [nameof(TailRiskController.DeleteLossComponents)] = "RequireSubmitRisk",
        [nameof(TailRiskController.GetCorrelations)] = "RequireRiskmanagement",
        [nameof(TailRiskController.SaveCorrelation)] = "RequireSubmitRisk",
        [nameof(TailRiskController.DeleteCorrelation)] = "RequireSubmitRisk",
        [nameof(TailRiskController.AggregatePortfolio)] = "RequireRiskmanagement"
    };

    private static readonly string[] TailLimitsActions =
    [
        nameof(RiskAppetitesController.GetTailLimits),
        nameof(RiskAppetitesController.SaveTailLimits),
        nameof(RiskAppetitesController.DeleteTailLimits)
    ];

    private static List<MethodInfo> Actions() => typeof(TailRiskController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    [Fact]
    public void TestThereAreSevenActionsEachWithExactlyOneAttribute()
    {
        var actions = Actions();

        Assert.Equal(7, actions.Count);
        Assert.Equal(Expected.Keys.OrderBy(n => n), actions.Select(a => a.Name).OrderBy(n => n));
    }

    /// <summary>Every action carries exactly its policy, as a plain <see cref="AuthorizeAttribute"/>, and no anonymous access.</summary>
    [Fact]
    public void TestEachActionCarriesExactlyItsPolicy()
    {
        foreach (var action in Actions())
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
            var only = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
            Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
            Assert.Equal(Expected[action.Name], only.Policy);
        }
    }

    /// <summary>The writes are the audience that edits a risk, never the read-only register audience.</summary>
    [Fact]
    public void TestNoWriteCarriesTheRegisterPolicy()
    {
        var writes = Actions().Where(a => a.GetCustomAttributes<HttpMethodAttribute>()
            .Any(h => h.HttpMethods.Any(m => m is "PUT" or "DELETE"))).ToList();

        Assert.Equal(4, writes.Count);
        Assert.All(writes, w => Assert.Equal("RequireSubmitRisk",
            w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));
    }

    [Fact]
    public void TestTheClassRequiresAValidUserAndAllowsNoAnonymous()
    {
        var policies = typeof(TailRiskController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Equal("RequireValidUser", Assert.Single(policies).Policy);
        Assert.Empty(typeof(TailRiskController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    /// <summary>
    /// The appetite's monetary tolerances are admin-only like the rest of the appetite (S48 §6): the three actions carry no
    /// attribute that could loosen or replace the class's policy.
    /// </summary>
    [Fact]
    public void TestTheTailLimitsInheritTheAdminOnlyPolicyOfTheAppetiteController()
    {
        var policies = typeof(RiskAppetitesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.Equal("RequireAdminOnly", Assert.Single(policies).Policy);
        Assert.Empty(typeof(RiskAppetitesController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));

        foreach (var name in TailLimitsActions)
        {
            var action = typeof(RiskAppetitesController).GetMethod(name)!;
            Assert.NotEmpty(action.GetCustomAttributes<HttpMethodAttribute>());
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }
    }
}
