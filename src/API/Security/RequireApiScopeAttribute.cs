using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Security;

/// <summary>
/// Requires a granted scope when the caller authenticated with a CI API token
/// (Track 3 milestone 3.5.1).
///
/// A filter rather than an authorization policy because of the conditional: a request authenticated
/// as a person carries no scope claims and must not be refused for lacking them — the permission
/// attribute already governs that path. Only token-authenticated requests are scope-checked, and for
/// those the check is mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireApiScopeAttribute(params string[] scopes) : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>Any one of these is enough. Empty means the endpoint is closed to tokens entirely.</summary>
    public IReadOnlyList<string> Scopes { get; } = scopes;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        var isTokenAuthenticated = user.Identity?.AuthenticationType == ApiTokenAuthenticationHandler.SchemeName;

        // A human request is not scope-checked: PermissionAuthorize governs it, and demanding a
        // scope claim here would lock every interactive user out of the endpoint.
        if (!isTokenAuthenticated) return Task.CompletedTask;

        if (Scopes.Count == 0)
        {
            context.Result = new ObjectResult(new
            {
                error = "insufficient_scope",
                message = "This endpoint is not available to API tokens."
            }) { StatusCode = 403 };
            return Task.CompletedTask;
        }

        var granted = user.Claims
            .Where(c => c.Type == ApiTokenAuthenticationHandler.ScopeClaimType)
            .Select(c => c.Value)
            .ToList();

        if (Scopes.Any(required => granted.Contains(required, StringComparer.OrdinalIgnoreCase)))
            return Task.CompletedTask;

        // The required scope is named in the response. A CI operator debugging a 403 needs to know
        // which scope to add, and making them read the source to find out is a waste of their day.
        context.Result = new ObjectResult(new
        {
            error = "insufficient_scope",
            required = Scopes,
            granted,
            message = $"This API token needs one of the following scopes: {string.Join(", ", Scopes)}."
        }) { StatusCode = 403 };

        return Task.CompletedTask;
    }
}
