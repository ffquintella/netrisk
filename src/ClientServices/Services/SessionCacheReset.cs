using System;
using ClientServices.Interfaces;

namespace ClientServices.Services;

/// <summary>
/// Clears the client's process-wide read caches whenever the signed-in session changes — on every sign-in and when a
/// session expires — so data one user read is never served to the next user of the same process. Found by the M62
/// security review: the agent tools (and the GUI) could otherwise answer from a previous session's hosts or entities.
/// </summary>
public sealed class SessionCacheReset
{
    private readonly IMemoryCacheService _cache;
    private readonly IHostsService _hosts;
    private bool _attached;

    public SessionCacheReset(IMemoryCacheService cache, IHostsService hosts)
    {
        _cache = cache;
        _hosts = hosts;
    }

    /// <summary>Subscribes to <paramref name="authentication"/>'s session events. Idempotent.</summary>
    public void Attach(IAuthenticationService authentication)
    {
        if (_attached) return;
        _attached = true;
        authentication.AuthenticationSucceeded += (_, _) => Reset();
        authentication.SessionExpired += (_, _) => Reset();
    }

    public void Reset()
    {
        _cache.Clear();
        _hosts.ClearCache();
    }
}
