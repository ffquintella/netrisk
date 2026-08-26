using System.Collections.Concurrent;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Security;

/// <summary>
/// Security finding NR-2026-028 — per-<c>jti</c> token revocation.
///
/// The read path is what makes this affordable: <see cref="IsRevokedAsync"/> runs on every
/// authenticated request, so a negative answer is cached for <see cref="NegativeCacheWindow"/> and a
/// positive one for the token's remaining lifetime. The window bounds how long a revoked token can
/// still be accepted by an instance that has not seen the revocation yet — seconds, against a token
/// lifetime of an hour — and that trade is stated rather than hidden, because the alternative is an
/// uncached database read on every single request.
/// </summary>
public class TokenRevocationService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), ITokenRevocationService
{
    /// <summary>
    /// How long a "not revoked" answer is trusted. Short enough that a sign-out takes effect
    /// promptly across instances, long enough that the lookup is not per-request.
    /// </summary>
    internal static readonly TimeSpan NegativeCacheWindow = TimeSpan.FromSeconds(10);

    /// <summary>Cap on the cache, so a flood of distinct jti values cannot grow it without bound.</summary>
    internal const int MaxCachedEntries = 50_000;

    private static readonly ConcurrentDictionary<string, (bool Revoked, DateTimeOffset Until)> Cache =
        new(StringComparer.Ordinal);

    private readonly TimeProvider _time = TimeProvider.System;

    public async Task RevokeAsync(string jti, int? userId, DateTime expiresAtUtc, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(jti))
            throw new InvalidParameterException(nameof(jti),
                "A token with no jti claim cannot be revoked individually. Change the account's password " +
                "to revoke every session it has.");

        jti = jti.Trim();
        if (jti.Length > 64) jti = jti[..64];

        await using var db = DalService.GetContext();

        var existing = await db.RevokedTokens.FirstOrDefaultAsync(t => t.Jti == jti);
        if (existing is null)
        {
            db.RevokedTokens.Add(new RevokedToken
            {
                Jti = jti,
                UserId = userId,
                RevokedAt = DateTime.UtcNow,
                ExpiresAt = expiresAtUtc,
                Reason = reason
            });

            await db.SaveChangesAsync();
        }

        // The positive answer is cached until the token would have expired anyway: after that the
        // row is pruned and the token is rejected on its own expiry, so there is nothing to re-read.
        Cache[jti] = (true, expiresAtUtc);

        Logger.Information("Session token {Jti} revoked for user {User}{Reason}", jti,
            userId?.ToString() ?? "(unknown)",
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason}");
    }

    public async Task<bool> IsRevokedAsync(string jti)
    {
        // A token with no jti cannot be individually revoked, so there is nothing to look up. It is
        // still subject to the mass-revocation checks (password change, account disabled).
        if (string.IsNullOrWhiteSpace(jti)) return false;

        jti = jti.Trim();
        var now = _time.GetUtcNow();

        if (Cache.TryGetValue(jti, out var cached) && cached.Until > now) return cached.Revoked;

        await using var db = DalService.GetContext();
        var revoked = await db.RevokedTokens.AnyAsync(t => t.Jti == jti);

        Trim();

        Cache[jti] = revoked
            ? (true, now.Add(TimeSpan.FromHours(24)))
            : (false, now.Add(NegativeCacheWindow));

        return revoked;
    }

    public async Task<int> PruneExpiredAsync(DateTime asOfUtc)
    {
        await using var db = DalService.GetContext();

        var deleted = await db.RevokedTokens.Where(t => t.ExpiresAt < asOfUtc).ExecuteDeleteAsync();

        if (deleted > 0)
            Logger.Information("Pruned {Count} revoked-token rows whose tokens had expired", deleted);

        return deleted;
    }

    /// <summary>
    /// Clears the process cache. Only for tests — production has no reason to, and the negative
    /// window is what bounds staleness.
    /// </summary>
    internal static void ClearCache() => Cache.Clear();

    private void Trim()
    {
        if (Cache.Count < MaxCachedEntries) return;

        var now = _time.GetUtcNow();
        foreach (var (key, value) in Cache)
            if (value.Until <= now) Cache.TryRemove(key, out _);

        // Still full: drop everything rather than picking arbitrary victims. A cold cache costs one
        // indexed read per token and never grants access to a revoked one, which is the direction a
        // failure here has to fall.
        if (Cache.Count >= MaxCachedEntries) Cache.Clear();
    }
}
