using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Model.DecisionCycle;

namespace API.Security;

/// <summary>
/// Stage 9.9 (S50 §4.7, D10) — the third line reads and never writes.
///
/// Part of <em>every</em> policy the API builds (<see cref="PermissionPolicyProvider"/>, <see cref="DefaultPolicyProvider"/>),
/// so it is the one decision point for the whole surface: a caller holding <see cref="ThirdLineAssurance.PermissionKey"/>
/// is refused any request that is not a safe read — whatever else the caller holds, the <c>Admin</c> role included —
/// and everybody else passes it untouched. It does not grant anything; the policy's own requirements still decide reads.
///
/// Why a requirement on every policy rather than one more permission per write: the permissions of this API mix reads and
/// writes (<c>riskmanagement</c> gates <c>POST /Risks/{id}/RequestReview</c> beside every register read), so a read-only
/// role cannot be built by choosing permissions. The auditor needs those reads; this is what keeps the writes beside them
/// closed (S50 D10).
///
/// The requirement is its own handler: ASP.NET Core evaluates a requirement that implements
/// <see cref="IAuthorizationHandler"/> through its built-in pass-through handler, so there is no registration to forget —
/// and a policy that carried the requirement without a handler would fail closed for everyone, not open for the auditor.
/// </summary>
public sealed class ThirdLineReadOnlyRequirement
    : AuthorizationHandler<ThirdLineReadOnlyRequirement>, IAuthorizationRequirement
{
    public static readonly ThirdLineReadOnlyRequirement Instance = new();

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ThirdLineReadOnlyRequirement requirement)
    {
        if (!ThirdLineReadOnly.IsThirdLine(context.User))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Endpoint routing passes the HttpContext as the resource. Anything else — an imperative check with no request —
        // cannot say whether it is a write, so the third line is refused (fail closed).
        if (context.Resource is HttpContext http && ThirdLineReadOnly.Permits(http, context.User))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var request = (context.Resource as HttpContext)?.Request;
        Serilog.Log.Warning("Refused {Method} {Path} to {User}: the third line (internal audit) is read-only",
            request?.Method ?? "(no request)", request?.Path.Value ?? string.Empty,
            context.User.Identity?.Name ?? "(anonymous)");

        context.Fail(new AuthorizationFailureReason(this, ThirdLineAssurance.ReadOnlyRule));
        return Task.CompletedTask;
    }
}

/// <summary>The decision itself, pure over the principal and the request, so it is tested directly (S50 §8).</summary>
public static class ThirdLineReadOnly
{
    /// <summary>The methods that read. Everything else writes.</summary>
    public static readonly IReadOnlySet<string> SafeMethods =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>Whether the caller is the third line: it holds the marker permission, through its role or directly.</summary>
    public static bool IsThirdLine(ClaimsPrincipal? user) =>
        user?.HasClaim(c => c.Type == "Permission" && c.Value == ThirdLineAssurance.PermissionKey) == true;

    /// <summary>
    /// A write: any method that is not a safe read, and any GET an endpoint declares state-changing
    /// (<see cref="StateChangingGetAttribute"/>).
    /// </summary>
    public static bool IsWrite(string method, Endpoint? endpoint) =>
        !SafeMethods.Contains(method) || endpoint?.Metadata.GetMetadata<StateChangingGetAttribute>() is not null;

    /// <summary>
    /// Whether the third line may make this request: a read; or a write the endpoint declares self-service
    /// (<see cref="ThirdLineSelfServiceAttribute"/>) — and, when it names a route key, only on the caller's own account.
    /// </summary>
    public static bool Permits(HttpContext http, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(http);

        var endpoint = http.GetEndpoint();
        if (!IsWrite(http.Request.Method, endpoint)) return true;

        var selfService = endpoint?.Metadata.GetMetadata<ThirdLineSelfServiceAttribute>();
        if (selfService is null) return false;
        if (selfService.RouteUserKey is null) return true;

        var own = user.FindFirst(ClaimTypes.Sid)?.Value;
        var target = http.GetRouteValue(selfService.RouteUserKey)?.ToString();
        return own is not null && target is not null && string.Equals(own, target, StringComparison.Ordinal);
    }
}

/// <summary>
/// A GET that writes (S50 §4.7). Read by the third-line guard as a write; every one in the API is listed by
/// <c>ThirdLineReadOnlyInventoryTest</c>, which fails if a known one loses this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class StateChangingGetAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>
/// A write the third line still needs, because it concerns only the caller's own session or credentials — signing in,
/// signing out, enrolling a hardware factor, changing one's own password (S50 §4.7). Each carries its reason, and the
/// set is pinned by <c>ThirdLineReadOnlyInventoryTest</c>: adding one is a reviewed act, like an anonymous endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ThirdLineSelfServiceAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;

    /// <summary>A route value that must equal the caller's own user id (<c>ClaimTypes.Sid</c>), when the route names a user.</summary>
    public string? RouteUserKey { get; init; }
}
