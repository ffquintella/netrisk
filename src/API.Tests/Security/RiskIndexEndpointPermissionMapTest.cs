using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using API.Controllers;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Model.RiskIndex;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// M53, T249/T250 (S40 §11.2, §12.3 <c>RiskIndexEndpointPermissionMapTest</c>). <c>ControllerAuthorizationInventoryTest</c>
/// sees only that some attribute exists, and the controller tests call the actions directly: a wrong attribute — the
/// approval under <c>risk_index_configure</c> — would pass both. This pins the exact set of permissions (class ∪ action) of
/// every action of the two index controllers against the table of S40 §8.2 and §8.4, and that none is anonymous.
/// </summary>
[TestSubject(typeof(RiskIndexProfilesController))]
public class RiskIndexEndpointPermissionMapTest
{
    private const string View = RiskIndexPermissions.View, Configure = RiskIndexPermissions.Configure,
        Approve = RiskIndexPermissions.Approve;

    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["RiskIndexController.GetLatest"] = [View],
        ["RiskIndexController.GetSnapshotDates"] = [View],
        ["RiskIndexController.GetOverview"] = [View],
        ["RiskIndexController.GetCategories"] = [View],
        ["RiskIndexController.GetTrend"] = [View],
        ["RiskIndexController.GetNodes"] = [View],
        ["RiskIndexController.GetObjects"] = [View],
        ["RiskIndexController.GetObject"] = [View],
        ["RiskIndexController.GetInventory"] = [View],
        ["RiskIndexController.GetChanges"] = [View],
        ["RiskIndexController.GetContributions"] = [View],
        ["RiskIndexController.GetGovernanceIndicators"] = [View],
        ["RiskIndexController.GetPublication"] = [View],
        ["RiskIndexController.SetPublication"] = [View, Approve],

        ["RiskIndexProfilesController.List"] = [View],
        ["RiskIndexProfilesController.Get"] = [View],
        ["RiskIndexProfilesController.GetActive"] = [View],
        ["RiskIndexProfilesController.Diff"] = [View],
        ["RiskIndexProfilesController.Schema"] = [View],
        ["RiskIndexProfilesController.Presets"] = [Configure],
        ["RiskIndexProfilesController.Create"] = [Configure],
        ["RiskIndexProfilesController.Update"] = [Configure],
        ["RiskIndexProfilesController.Delete"] = [Configure],
        ["RiskIndexProfilesController.Validate"] = [Configure],
        ["RiskIndexProfilesController.RequestPreview"] = [Configure],
        ["RiskIndexProfilesController.GetPreview"] = [View],
        ["RiskIndexProfilesController.Submit"] = [Configure],
        ["RiskIndexProfilesController.ApprovalEligibility"] = [Approve],
        ["RiskIndexProfilesController.Approve"] = [Approve],
        ["RiskIndexProfilesController.Reject"] = [Approve],
        ["RiskIndexProfilesController.Retire"] = [Approve],
        ["RiskIndexProfilesController.Attach"] = [Approve],
        ["RiskIndexProfilesController.Attachments"] = [View],
        ["RiskIndexProfilesController.Bridge"] = [View],
        ["RiskIndexProfilesController.History"] = [View]
    };

    private static readonly Type[] Controllers = [typeof(RiskIndexController), typeof(RiskIndexProfilesController)];

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() => Controllers
        .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Select(m => (c, m)));

    private static IEnumerable<AuthorizeAttribute> Authorizations(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Concat(action.GetCustomAttributes<AuthorizeAttribute>());

    [Fact]
    public void TestEveryActionCarriesExactlyItsPermissions()
    {
        var actions = Actions().ToList();
        Assert.Equal(Expected.Keys.Order(), actions.Select(a => $"{a.Controller.Name}.{a.Action.Name}").Order());

        foreach (var (controller, action) in actions)
        {
            var key = $"{controller.Name}.{action.Name}";
            var attributes = Authorizations(controller, action).ToList();

            // Only permission attributes: no action leans on RequireValidUser or another policy alone (S40 §11.2).
            Assert.All(attributes, a => Assert.IsType<PermissionAuthorizeAttribute>(a));
            Assert.Equal(Expected[key].Order(), attributes.Cast<PermissionAuthorizeAttribute>().Select(a => a.Permission).Distinct().Order());
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Concat(action.GetCustomAttributes<AllowAnonymousAttribute>()));
        }
    }

    /// <summary>The approval is never under configure: the decision and the draft are different people (S39 §13.1).</summary>
    [Fact]
    public void TestNoDecisionIsUnderTheDraftPermission()
    {
        foreach (var name in new[] { "Approve", "Reject", "Retire", "ApprovalEligibility", "Attach" })
        {
            var action = typeof(RiskIndexProfilesController).GetMethod(name)!;
            Assert.DoesNotContain(Configure, Authorizations(typeof(RiskIndexProfilesController), action)
                .Cast<PermissionAuthorizeAttribute>().Select(a => a.Permission));
        }
    }

    /// <summary>
    /// S39 §12.5, S40 §12.3 (<c>RiskIndexSnapshotTablesHaveNoWriteEndpointsTest</c>): no PUT, PATCH or DELETE takes or
    /// returns a snapshot, an object row or an attribution — the history is written by the job alone.
    /// </summary>
    [Fact]
    public void TestNoWriteEndpointTouchesTheHistoryTables()
    {
        Type[] history = [typeof(RiskIndexSnapshot), typeof(RiskIndexObjectDay), typeof(RiskIndexObjectAttribution), typeof(RiskIndexBridge)];
        var writes = typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Any(v => v is "PUT" or "PATCH" or "DELETE")));

        foreach (var method in writes)
        {
            var types = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType).SelectMany(Flatten);
            Assert.DoesNotContain(types, history.Contains);
        }

        static IEnumerable<Type> Flatten(Type type) =>
            new[] { type }.Concat(type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten) : []);
    }

    /// <summary>S40 §12.3 (<c>RiskIndexObjectSortMapTest</c>): the object list declares no sort by sensitivity or by Δ.</summary>
    [Fact]
    public void TestTheObjectListDeclaresNoSortBySensitivityOrDelta()
    {
        Assert.Equal(new[] { "contribution", "criticality", "label", "lastReading", "r" },
            RiskIndexReadService.ObjectSortKeys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(RiskIndexReadService.ObjectSortKeys, k =>
            k.Contains("sensitiv", StringComparison.OrdinalIgnoreCase) || k.Contains("delta", StringComparison.OrdinalIgnoreCase));
    }
}
