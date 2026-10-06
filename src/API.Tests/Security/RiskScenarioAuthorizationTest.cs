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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.2 (S42 §6, D8; §8) — who may register a hypothesis and who may ask for scenario duplicates.
///
/// Two decisions are pinned. Registering a standalone hypothesis has the audience of promoting and
/// dismissing one (<c>RequireSubmitRisk</c>). The duplicate check answers with risk subjects, so it has
/// the audience of listing risks (<c>RequireRiskmanagement</c>, the policy on <c>GET /Risks</c>) — a
/// caller who may submit but not list risks must not enumerate their subjects by guessing scenarios.
/// The negative cases evaluate the real policies rather than trusting their names.
/// </summary>
[TestSubject(typeof(RiskGovernanceController))]
public class RiskScenarioAuthorizationTest
{
    private static MethodInfo Action(Type controller, string name) =>
        controller.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;

    private static string OnlyPolicyOf(MethodInfo action)
    {
        Assert.Empty(action.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: false));
        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));

        var only = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(typeof(AuthorizeAttribute), only.GetType());
        return only.Policy!;
    }

    [Fact]
    public void TestRegisteringAHypothesisHasTheAudienceOfTriagingOne()
    {
        var create = OnlyPolicyOf(Action(typeof(RiskGovernanceController), nameof(RiskGovernanceController.CreateHypothesis)));

        Assert.Equal("RequireSubmitRisk", create);
        Assert.Equal(create, OnlyPolicyOf(Action(typeof(RiskGovernanceController), nameof(RiskGovernanceController.PromotePending))));
        Assert.Equal(create, OnlyPolicyOf(Action(typeof(RiskGovernanceController), nameof(RiskGovernanceController.DismissPending))));
    }

    [Fact]
    public void TestTheDuplicateCheckHasTheAudienceOfListingRisks()
    {
        var duplicates = OnlyPolicyOf(Action(typeof(RiskGovernanceController),
            nameof(RiskGovernanceController.FindScenarioDuplicates)));

        Assert.Equal("RequireRiskmanagement", duplicates);
        Assert.Equal(duplicates, OnlyPolicyOf(Action(typeof(RisksController), nameof(RisksController.GetAllAsync))));
    }

    [Fact]
    public void TestTheControllerNeitherOpensNorNarrowsAtClassLevel()
    {
        Assert.Empty(typeof(RiskGovernanceController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Equal("RequireValidUser",
            Assert.Single(typeof(RiskGovernanceController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
    }

    // --- the two policies, evaluated -------------------------------------------------------------

    private static async Task<bool> PolicyAllows(string policyName, params Claim[] claims)
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

        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "Bearer"));

        return (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded;
    }

    [Fact]
    public async Task TestAValidUserWithoutSubmitRisksCannotRegisterAHypothesis()
    {
        Assert.False(await PolicyAllows("RequireSubmitRisk"));
        Assert.False(await PolicyAllows("RequireSubmitRisk", new Claim("Permission", "riskmanagement")));
        Assert.True(await PolicyAllows("RequireSubmitRisk", new Claim("Permission", "submit_risks")));
    }

    /// <summary>
    /// The case D8 is about: a user who may submit risks, and so reaches the risk editor's create flow
    /// through the API, does not get the subjects of existing risks back from the duplicate check.
    /// </summary>
    [Fact]
    public async Task TestASubmitterWithoutRiskManagementCannotAskForDuplicates()
    {
        Assert.False(await PolicyAllows("RequireRiskmanagement", new Claim("Permission", "submit_risks")));
        Assert.True(await PolicyAllows("RequireRiskmanagement", new Claim("Permission", "riskmanagement")));
    }
}
