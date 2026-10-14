using System.Collections.Generic;
using System.Security.Claims;
using DAL.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// The background principal the console client and the Hangfire host run under. Every assertion
/// here is a property <c>DalService.GetUserId</c> reads in order — a null context, a null
/// identity, a null <c>Identity.Name</c> or a missing Sid each make it resolve user 0, which is
/// how a job silently writes audit rows attributed to nobody.
/// </summary>
public class BackgroundServiceHttpContextAccessorTest
{
    [Fact]
    public void Exposes_a_context()
    {
        IHttpContextAccessor accessor = new BackgroundServiceHttpContextAccessor();

        Assert.NotNull(accessor.HttpContext);
    }

    [Fact]
    public void Principal_carries_a_named_identity()
    {
        var context = new BackgroundServiceHttpContextAccessor().HttpContext!;

        Assert.NotNull(context.User.Identity);
        Assert.Equal(BackgroundServiceHttpContextAccessor.ServiceAccountName, context.User.Identity!.Name);
    }

    [Fact]
    public void Principal_is_authenticated()
    {
        var context = new BackgroundServiceHttpContextAccessor().HttpContext!;

        Assert.True(context.User.Identity!.IsAuthenticated);
        Assert.Equal(BackgroundServiceHttpContextAccessor.AuthenticationType,
            context.User.Identity.AuthenticationType);
    }

    /// <summary>
    /// The claim <c>GetUserId</c> actually returns, and it must parse as an int.
    /// </summary>
    [Fact]
    public void Principal_carries_a_numeric_sid()
    {
        var context = new BackgroundServiceHttpContextAccessor().HttpContext!;

        var sid = Assert.Single(context.User.Claims, c => c.Type == ClaimTypes.Sid);
        Assert.Equal(BackgroundServiceHttpContextAccessor.ServiceAccountSid, sid.Value);
        Assert.True(int.TryParse(sid.Value, out var userId), "GetUserId parses this value directly.");
        Assert.Equal(1, userId);
    }

    /// <summary>
    /// Each accessor builds its own context, so one host cannot mutate another's principal.
    /// </summary>
    [Fact]
    public void Instances_do_not_share_a_context()
    {
        var first = new BackgroundServiceHttpContextAccessor();
        var second = new BackgroundServiceHttpContextAccessor();
        var jobHost = BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope();

        Assert.NotSame(first.HttpContext, second.HttpContext);
        Assert.NotSame(first.HttpContext, jobHost.HttpContext);
    }

    // --- T237 (S39 D-01): the entity scope each background principal reads ---------------------------

    /// <summary>
    /// The real <see cref="DalService"/>. <see cref="DalService.GetCurrentEntityScope"/> reads only the
    /// accessor's principal and opens no connection, so no database is needed to ask it.
    /// </summary>
    private static DalService DalOver(IHttpContextAccessor accessor) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
            })
            .Build(), accessor);

    /// <summary>The job host's principal reads every entity — the fix. Before it, this was <c>DenyAll</c>.</summary>
    [Fact]
    public void The_job_host_principal_reads_unrestricted_scope()
    {
        var accessor = BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope();
        var user = accessor.HttpContext!.User;

        Assert.True(user.HasClaim(BackgroundServiceHttpContextAccessor.ScopeClaimType,
            BackgroundServiceHttpContextAccessor.GlobalScope));
        // The same identity as the default principal: the grant adds a scope, never a different account.
        Assert.Equal(BackgroundServiceHttpContextAccessor.ServiceAccountSid, user.FindFirst(ClaimTypes.Sid)!.Value);
        Assert.Equal(BackgroundServiceHttpContextAccessor.ServiceAccountName, user.Identity!.Name);

        Assert.True(DalOver(accessor).GetCurrentEntityScope().IsUnrestricted);
    }

    /// <summary>
    /// The default principal — the console client's — is unchanged: no scope claim, so <c>DenyAll</c>. The
    /// grant is opt-in by name, which is what lets <c>BackgroundScopeContainmentTest</c> find every caller.
    /// </summary>
    [Fact]
    public void The_default_background_principal_carries_no_scope_and_reads_deny_all()
    {
        var accessor = new BackgroundServiceHttpContextAccessor();

        Assert.DoesNotContain(accessor.HttpContext!.User.Claims,
            c => c.Type == BackgroundServiceHttpContextAccessor.ScopeClaimType);
        Assert.Same(EntityScope.DenyAll, DalOver(accessor).GetCurrentEntityScope());
    }

    /// <summary>
    /// The grant is the claim and nothing else. A principal that copies the background identity — its sid,
    /// its name, its authentication type — without the claim reads <c>DenyAll</c>, so presenting as the job
    /// host from a request obtains nothing. The API's handlers build every principal from the user's row and
    /// add <c>scope=global</c> only for an administrator (<c>JwtAuthenticationHandler</c>,
    /// <c>BasicAuthenticationHandler</c>, <c>ApiTokenAuthenticationHandler</c>).
    /// </summary>
    [Fact]
    public void A_principal_impersonating_the_background_identity_without_the_claim_reads_deny_all()
    {
        var impostor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, BackgroundServiceHttpContextAccessor.ServiceAccountSid),
                    new Claim(ClaimTypes.Name, BackgroundServiceHttpContextAccessor.ServiceAccountName),
                    new Claim("scope", "background"),
                ], BackgroundServiceHttpContextAccessor.AuthenticationType))
            }
        };

        Assert.Same(EntityScope.DenyAll, DalOver(impostor).GetCurrentEntityScope());
    }
}
