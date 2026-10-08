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
/// Stage 9.8 (S49 §6, D11) — who may read the KRIs, the reassessment triggers and the metrics panel, who may define a
/// KRI (its tolerance is the Phase 0 limit Gate B enforces), and who may record readings, link risks and declare events.
///
/// No new permission and no new policy. The reads and the metrics panel are reads of the register
/// (<c>RequireRiskmanagement</c>); defining, changing or retiring a KRI is a governance decision
/// (<c>RequireAdminOnly</c>, as the appetite's tail tolerances are); every other write is editing a risk
/// (<c>RequireSubmitRisk</c>). Each attribute is pinned here by reflection; the policies themselves are evaluated by
/// their own tests.
/// </summary>
[TestSubject(typeof(MonitoringController))]
public class MonitoringAuthorizationTest
{
    private const string Read = "RequireRiskmanagement";
    private const string Define = "RequireAdminOnly";
    private const string Write = "RequireSubmitRisk";

    private static readonly Dictionary<string, string> Expected = new()
    {
        [nameof(MonitoringController.GetKris)] = Read,
        [nameof(MonitoringController.GetKri)] = Read,
        [nameof(MonitoringController.GetEvents)] = Read,
        [nameof(MonitoringController.GetTriggers)] = Read,
        [nameof(MonitoringController.GetMetrics)] = Read,
        [nameof(MonitoringController.CreateKri)] = Define,
        [nameof(MonitoringController.UpdateKri)] = Define,
        [nameof(MonitoringController.RetireKri)] = Define,
        [nameof(MonitoringController.RecordReading)] = Write,
        [nameof(MonitoringController.VoidReading)] = Write,
        [nameof(MonitoringController.LinkRisk)] = Write,
        [nameof(MonitoringController.UnlinkRisk)] = Write,
        [nameof(MonitoringController.DeclareEvent)] = Write,
        [nameof(MonitoringController.AddEventRisks)] = Write
    };

    private static readonly string[] DefineActions =
    [
        nameof(MonitoringController.CreateKri),
        nameof(MonitoringController.UpdateKri),
        nameof(MonitoringController.RetireKri)
    ];

    private static List<MethodInfo> Actions() => typeof(MonitoringController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .ToList();

    private static bool IsWrite(MethodInfo action) => action.GetCustomAttributes<HttpMethodAttribute>()
        .Any(h => h.HttpMethods.Any(m => m is "POST" or "PUT" or "DELETE"));

    [Fact]
    public void TestThereAreFourteenActionsEachWithExactlyOneAttribute()
    {
        var actions = Actions();

        Assert.Equal(14, actions.Count);
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

    /// <summary>Only the three KRI definition actions are admin-only; every other write is the audience that edits a risk.</summary>
    [Fact]
    public void TestTheDefinitionIsAdminOnlyAndEveryOtherWriteEditsARisk()
    {
        var writes = Actions().Where(IsWrite).ToList();

        Assert.Equal(9, writes.Count);
        foreach (var write in writes)
        {
            var policy = write.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy;
            Assert.Equal(DefineActions.Contains(write.Name) ? Define : Write, policy);
        }

        Assert.Equal(DefineActions.OrderBy(n => n),
            Actions().Where(a => a.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy == Define)
                .Select(a => a.Name).OrderBy(n => n));
    }

    /// <summary>The reads are the register's audience, and no write carries it: the read-only audience must never edit.</summary>
    [Fact]
    public void TestNoWriteCarriesTheRegisterPolicyAndEveryReadDoes()
    {
        var actions = Actions();

        Assert.All(actions.Where(IsWrite), w => Assert.NotEqual(Read,
            w.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));

        var reads = actions.Where(a => !IsWrite(a)).ToList();
        Assert.Equal(5, reads.Count);
        Assert.All(reads, r => Assert.Equal(Read, r.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy));
    }

    [Fact]
    public void TestTheClassRequiresAValidUserAndAllowsNoAnonymous()
    {
        var policies = typeof(MonitoringController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Equal("RequireValidUser", Assert.Single(policies).Policy);
        Assert.Empty(typeof(MonitoringController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }
}
