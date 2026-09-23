using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ServerServices.Security;
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

        Assert.NotSame(first.HttpContext, second.HttpContext);
    }
}
