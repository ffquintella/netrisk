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
/// Stage 9.6 (S47 §6, D10) — who may read and declare treatment economics and the target level, and who may run the
/// portfolio selection.
///
/// No new permission and no new policy. Reading a mitigation's economics carries the policy of <c>/Mitigations</c>
/// (<c>RequireMitigation</c>); declaring the option, the cost, the prerequisites and the target carries the policy of
/// planning treatment (<c>RequirePlanMitigations</c>, the audience of the tasks); the risk view and the portfolio are
/// reads of the register (<c>RequireRiskmanagement</c>). Each attribute is pinned here by reflection; the policies
/// themselves are evaluated by their own tests.
/// </summary>
[TestSubject(typeof(TreatmentEconomicsController))]
public class TreatmentEconomicsAuthorizationTest
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        [nameof(TreatmentEconomicsController.GetMitigation)] = "RequireMitigation",
        [nameof(TreatmentEconomicsController.SaveMitigation)] = "RequirePlanMitigations",
        [nameof(TreatmentEconomicsController.GetRisk)] = "RequireRiskmanagement",
        [nameof(TreatmentEconomicsController.SaveTarget)] = "RequirePlanMitigations",
        [nameof(TreatmentEconomicsController.DeleteTarget)] = "RequirePlanMitigations",
        [nameof(TreatmentEconomicsController.SelectPortfolio)] = "RequireRiskmanagement"
    };

    private static List<MethodInfo> Actions() => typeof(TreatmentEconomicsController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    [Fact]
    public void TestThereAreSixActionsEachWithExactlyOneAttribute()
    {
        var actions = Actions();

        Assert.Equal(6, actions.Count);
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

    /// <summary>The writes are planning, never the register audience: a risk manager who cannot plan mitigations cannot declare a cost.</summary>
    [Fact]
    public void TestNoWriteCarriesTheRegisterPolicy()
    {
        var writes = Actions().Where(a => a.GetCustomAttributes<HttpMethodAttribute>()
            .Any(h => h.HttpMethods.Any(m => m is "PUT" or "DELETE")));

        Assert.All(writes, w => Assert.Equal("RequirePlanMitigations",
            w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));
    }

    [Fact]
    public void TestTheClassRequiresAValidUserAndAllowsNoAnonymous()
    {
        var policies = typeof(TreatmentEconomicsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Equal("RequireValidUser", Assert.Single(policies).Policy);
        Assert.Empty(typeof(TreatmentEconomicsController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }
}
