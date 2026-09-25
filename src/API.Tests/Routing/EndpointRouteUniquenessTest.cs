using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace API.Tests.Routing;

/// <summary>
/// Two actions may not answer the same HTTP method on the same route template.
///
/// This exists because that is exactly what shipped: Track 3 added
/// <c>PUT Vulnerabilities/{id}/status</c> (the finding lifecycle) beside the register's existing
/// <c>PUT Vulnerabilities/{id}/Status</c> (the IntStatus workflow column). Route templates are
/// matched case-insensitively, so endpoint routing could not choose between them and every call to
/// <em>either</em> threw <c>AmbiguousMatchException</c> in the routing middleware — before
/// authentication, before the controller, and therefore with nothing in any controller's catch
/// block. The desktop client saw a bare 500 and the server log a stack trace naming neither
/// endpoint as the culprit.
///
/// Nothing else catches this: it compiles, it passes every controller test (which call action
/// methods directly and never route), and it only fails against a running server.
/// </summary>
public class EndpointRouteUniquenessTest
{
    [Fact]
    public void NoTwoActionsShareAnHttpMethodAndRouteTemplate()
    {
        var byRoute = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (controller, action) in AllActions())
        foreach (var route in RoutesOf(controller, action))
        {
            if (!byRoute.TryGetValue(route.Key, out var owners))
                byRoute[route.Key] = owners = new List<string>();

            owners.Add($"{controller.Name}.{action.Name}");
        }

        var clashes = byRoute
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => $"{pair.Key}\n      {string.Join("\n      ", pair.Value.OrderBy(o => o, StringComparer.Ordinal))}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(clashes.Count == 0,
            "These route templates are claimed by more than one action. Route matching is "
            + "case-insensitive, so every request to one of them fails with an "
            + "AmbiguousMatchException (HTTP 500) before reaching any controller:\n  "
            + string.Join("\n  ", clashes));
    }

    /// <summary>
    /// The regression assertion for the collision above, named so a failure says which endpoint
    /// moved rather than appearing in a list.
    /// </summary>
    [Fact]
    public void TheVulnerabilityWorkflowStatusAndLifecycleStatusRoutesAreDistinct()
    {
        var workflow = Single(nameof(VulnerabilitiesController.UpdateWorkflowStatus));
        var lifecycle = Single(nameof(VulnerabilitiesController.UpdateLifecycleStatus));

        Assert.Equal("PUT vulnerabilities/{}/workflowstatus", workflow);
        Assert.Equal("PUT vulnerabilities/{}/status", lifecycle);

        string Single(string actionName)
        {
            var action = typeof(VulnerabilitiesController).GetMethod(actionName);
            Assert.NotNull(action);

            return Assert.Single(RoutesOf(typeof(VulnerabilitiesController), action!)).Key;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<(Type Controller, MethodInfo Action)> AllActions() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                .Select(m => (t, m)));

    /// <summary>
    /// Every (method, template) pair an action answers, normalised the way routing compares them:
    /// lower-cased, and with parameter names dropped so that <c>{id}</c> and <c>{vulnerabilityId}</c>
    /// — which match precisely the same requests — collapse to one key. A constraint is kept, since
    /// <c>{id:int}</c> and <c>{name}</c> are different matchers with different precedence.
    /// </summary>
    private static IEnumerable<(string Key, string Template)> RoutesOf(Type controller, MethodInfo action)
    {
        var prefixes = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Select(r => r.Template)
            .DefaultIfEmpty("")
            .Select(t => Expand(t, controller, action))
            .ToList();

        var verbs = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();

        var actionTemplates = action.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Select(r => r.Template)
            .Concat(verbs.Select(v => v.Template).Where(t => t != null)!)
            .Distinct()
            .DefaultIfEmpty("")
            .ToList();

        var methods = verbs.SelectMany(v => v.HttpMethods).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var method in methods)
        foreach (var suffix in actionTemplates)
        foreach (var prefix in prefixes)
        {
            var template = Combine(prefix, Expand(suffix!, controller, action));

            yield return ($"{method.ToUpperInvariant()} {Normalize(template)}", template);
        }
    }

    private static string Expand(string template, Type controller, MethodInfo action) =>
        template
            .Replace("[controller]", controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name, StringComparison.Ordinal)
            .Replace("[action]", action.Name, StringComparison.Ordinal);

    private static string Combine(string prefix, string suffix)
    {
        // An action template rooted with / or ~/ ignores the controller's prefix, as routing does.
        if (suffix.StartsWith("~/", StringComparison.Ordinal)) return suffix[2..];
        if (suffix.StartsWith('/')) return suffix[1..];

        return string.Join('/', new[] { prefix, suffix }.Where(s => !string.IsNullOrEmpty(s)))
            .Trim('/');
    }

    private static string Normalize(string template) =>
        ParameterSegment.Replace(template.ToLowerInvariant(), match =>
        {
            var body = match.Groups[1].Value;
            var colon = body.IndexOf(':');

            // {id} -> {}, {id:int} -> {:int}, {*rest} -> {*}, {id?} -> {?}
            if (colon >= 0) return "{" + body[colon..] + "}";
            if (body.StartsWith('*')) return "{*}";

            return body.EndsWith('?') ? "{?}" : "{}";
        });

    private static readonly Regex ParameterSegment = new(@"\{([^}]*)\}", RegexOptions.Compiled);
}
