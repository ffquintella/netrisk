using System.Collections.Concurrent;

namespace ServerServices.Auth;

/// <summary>
/// The short-lived state of in-flight federated sign-ins (Track 4 milestone 4.3.1).
///
/// In memory on purpose. These entries live for a couple of minutes, are useless once redeemed, and
/// must not survive a restart — a PKCE verifier persisted to a database is a credential at rest for
/// no benefit. The cost is that a multi-instance deployment must pin the browser round trip to one
/// instance; that is documented in <c>docs/features/enterprise-authentication.md</c> rather than
/// worked around with sticky database state.
///
/// A single-use redemption is enforced by <see cref="TryRedeem"/> removing the entry, which is what
/// makes an authorization code replayed with the same state fail.
/// </summary>
public class PendingFederatedSignIns
{
    /// <summary>How long a browser round trip may take. Two minutes is generous for a redirect chain.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingSignIn> _pending = new();

    public void Add(PendingSignIn entry)
    {
        Prune();
        _pending[entry.State] = entry;
    }

    /// <summary>
    /// Removes and returns the entry for <paramref name="state"/>, or null when it is unknown,
    /// already redeemed, or expired.
    /// </summary>
    public PendingSignIn? TryRedeem(string state, DateTime nowUtc)
    {
        Prune();

        if (!_pending.TryRemove(state, out var entry)) return null;

        return entry.ExpiresAt <= nowUtc ? null : entry;
    }

    public int Count => _pending.Count;

    private void Prune()
    {
        var now = DateTime.UtcNow;

        foreach (var (state, entry) in _pending)
            if (entry.ExpiresAt <= now) _pending.TryRemove(state, out _);
    }
}

/// <summary>One in-flight sign-in.</summary>
public class PendingSignIn
{
    public required string State { get; init; }

    public int ProviderId { get; init; }

    /// <summary>PKCE code verifier for OIDC; null for SAML.</summary>
    public string? CodeVerifier { get; init; }

    /// <summary>The redirect URI sent to the IdP. Must be echoed identically on token exchange.</summary>
    public string? RedirectUri { get; init; }

    /// <summary>SAML <c>AuthnRequest</c> id, matched against the response's <c>InResponseTo</c>.</summary>
    public string? RequestId { get; init; }

    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.Add(PendingFederatedSignIns.Lifetime);
}
