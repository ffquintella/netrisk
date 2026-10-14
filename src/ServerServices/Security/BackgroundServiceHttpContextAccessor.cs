using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ServerServices.Security;

/// <summary>
/// The <see cref="IHttpContextAccessor"/> for hosts with no HTTP pipeline — the console client
/// and the Hangfire job host. Services below them resolve the acting user from the accessor
/// (<c>DalService.GetUserId</c> reads <see cref="ClaimTypes.Sid"/>), so those hosts must present
/// a principal rather than none. Replaces a Moq mock in shipped code; see CHANGELOG.md, [NEXT].
/// </summary>
public sealed class BackgroundServiceHttpContextAccessor : IHttpContextAccessor
{
    /// <summary>User id the background principal acts as. Matches the seeded admin row.</summary>
    public const string ServiceAccountSid = "1";

    /// <summary>Name the background principal is logged and audited under.</summary>
    public const string ServiceAccountName = "BackgroundServices";

    /// <summary>
    /// Any non-null authentication type makes <c>ClaimsIdentity.IsAuthenticated</c> true, which is
    /// what the services below actually test. Kept as the original literal so audit rows written
    /// before and after this change read the same.
    /// </summary>
    public const string AuthenticationType = "mock";

    /// <summary>
    /// The claim <c>DalService.GetCurrentEntityScope</c> already reads as "every entity" — the same one
    /// the API's authentication handlers issue to a global administrator, and only to one, from the
    /// user's row in the database. No new way to become unrestricted is introduced: the job host's
    /// principal carries exactly what an administrator's request does.
    /// </summary>
    public const string ScopeClaimType = "scope";

    /// <inheritdoc cref="ScopeClaimType"/>
    public const string GlobalScope = "global";

    /// <summary>
    /// The principal without an entity-scope claim. An authenticated principal with no scope claim and no
    /// <c>entity_id</c> reads <c>EntityScope.DenyAll</c>, so a host built on this sees no scoped row. The
    /// console client uses it; the job host uses <see cref="WithUnrestrictedEntityScope"/>.
    /// </summary>
    public BackgroundServiceHttpContextAccessor() : this(unrestrictedEntityScope: false)
    {
    }

    private BackgroundServiceHttpContextAccessor(bool unrestrictedEntityScope)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.Sid, ServiceAccountSid),
            new Claim(ClaimTypes.Name, ServiceAccountName),
        ];

        if (unrestrictedEntityScope) claims.Add(new Claim(ScopeClaimType, GlobalScope));

        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)),
        };
    }

    /// <summary>
    /// The Hangfire job host's principal: the background identity plus the global entity-scope claim
    /// (T237, S39 D-01).
    ///
    /// Without it every job read <c>EntityScope.DenyAll</c> — risks, hosts, incidents, findings,
    /// assessments, appetites and campaigns are all scoped — so the residual pass, the review cadence,
    /// the campaigns and the scheduled posture syncs saw zero rows, and a sync's insert tripped the write
    /// guard. A per-call <c>bypassEntityScope</c> would not have done: every service a job reuses opens
    /// its own context through the accessor and would still get <c>DenyAll</c>.
    ///
    /// Only the job host calls this. The API, the website and the risk portal register the request
    /// accessor; <c>API.Tests/Security/BackgroundScopeContainmentTest</c> fails if the API's composition
    /// root registers any other, if an HTTP host's source names this type, or if any production source
    /// outside the job host names this method.
    /// </summary>
    public static BackgroundServiceHttpContextAccessor WithUnrestrictedEntityScope() =>
        new(unrestrictedEntityScope: true);

    /// <summary>
    /// The synthetic context, from construction. Settable because the interface says so; no host
    /// assigns it, and assigning null would leave <c>GetUserId</c> resolving nobody.
    /// </summary>
    public HttpContext? HttpContext { get; set; }
}
