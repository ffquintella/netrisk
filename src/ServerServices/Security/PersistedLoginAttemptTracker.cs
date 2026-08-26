using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Security;

/// <summary>
/// Security finding NR-2026-008b — the brute-force counter, shared across API instances.
///
/// <see cref="LoginAttemptTracker"/> holds its state in a <c>ConcurrentDictionary</c>, so counters
/// reset on restart and every instance behind a load balancer has its own budget: an attacker who
/// spreads attempts across instances multiplies the allowance by the instance count. This decorator
/// keeps that in-process tracker — it is the fast path, and its progressive doubling policy is
/// unchanged — and adds a persisted counter behind it that every instance reads and writes.
///
/// <para><b>Why the write is not an amplification.</b> The obvious objection to a database write per
/// failed login is that it hands an attacker a way to make the server work harder on exactly the
/// request they are flooding. It does not, here: a failed credential check already costs a bcrypt
/// verify at work factor 15, which is roughly a second of CPU. One indexed UPSERT beside that is
/// noise. The rate limiter in <c>AuthRateLimiting</c> bounds the request rate before either happens.</para>
///
/// <para>Reads go through the in-process tracker first, so the shared counter is consulted only when
/// the local one would have allowed the attempt. That keeps the common case — a legitimate user
/// signing in — at zero extra queries.</para>
/// </summary>
public class PersistedLoginAttemptTracker(
    ILogger logger,
    IDalService dalService,
    LoginAttemptTracker local)
    : ServiceBase(logger, dalService), ILoginAttemptTracker
{
    /// <summary>Failures against one identity before the shared counter locks it out.</summary>
    internal const int DefaultMaxFailures = 5;

    /// <summary>How long a quiet period has to be before the shared counter resets.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How long a lockout lasts once the threshold is crossed.</summary>
    internal static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public LoginAttemptState Check(string? userName, string? ipAddress)
    {
        var localState = local.Check(userName, ipAddress);
        if (localState.IsLockedOut) return localState;

        var identity = Normalize(userName);
        if (identity is null) return localState;

        var source = NormalizeSource(ipAddress);

        try
        {
            using var db = DalService.GetContext(withIdentity: false);

            var row = db.LoginAttempts
                .AsNoTracking()
                .FirstOrDefault(a => a.Identity == identity && a.Source == source);

            if (row?.LockedUntil is null) return localState;

            var remaining = row.LockedUntil.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return localState;

            return new LoginAttemptState(true, remaining, row.FailureCount);
        }
        catch (Exception ex)
        {
            // A database that cannot be read must not lock everybody out, and must not open the door
            // either: the in-process tracker's answer stands, which is the pre-Track-8 behaviour.
            Logger.Warning("Could not read the shared login-attempt counter: {Message}", ex.Message);
            return localState;
        }
    }

    public LoginAttemptState RegisterFailure(string? userName, string? ipAddress)
    {
        var localState = local.RegisterFailure(userName, ipAddress);

        var identity = Normalize(userName);
        if (identity is null) return localState;

        var source = NormalizeSource(ipAddress);
        var now = DateTime.UtcNow;

        try
        {
            using var db = DalService.GetContext(withIdentity: false);

            var row = db.LoginAttempts.FirstOrDefault(a => a.Identity == identity && a.Source == source);

            if (row is null)
            {
                row = new LoginAttempt
                {
                    Identity = identity,
                    Source = source,
                    FailureCount = 1,
                    FirstFailureAt = now,
                    LastFailureAt = now
                };

                db.LoginAttempts.Add(row);
            }
            else
            {
                // A long quiet period resets rather than accumulating: the alternative punishes a
                // user for a typo they made an hour ago.
                if (now - row.LastFailureAt > Window)
                {
                    row.FailureCount = 1;
                    row.FirstFailureAt = now;
                    row.LockedUntil = null;
                }
                else
                {
                    row.FailureCount++;
                }

                row.LastFailureAt = now;
            }

            if (row.FailureCount >= DefaultMaxFailures) row.LockedUntil = now.Add(LockoutDuration);

            db.SaveChanges();

            if (row.LockedUntil is not null && row.LockedUntil > now)
            {
                var remaining = row.LockedUntil.Value - now;
                if (remaining > localState.RetryAfter)
                    return new LoginAttemptState(true, remaining, row.FailureCount);
            }

            return localState with
            {
                FailureCount = System.Math.Max(localState.FailureCount, row.FailureCount)
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not write the shared login-attempt counter: {Message}", ex.Message);
            return localState;
        }
    }

    public void RegisterSuccess(string? userName, string? ipAddress)
    {
        local.RegisterSuccess(userName, ipAddress);

        var identity = Normalize(userName);
        if (identity is null) return;

        var source = NormalizeSource(ipAddress);

        try
        {
            using var db = DalService.GetContext(withIdentity: false);

            // Deleting rather than zeroing is what keeps this table's steady-state size the number
            // of identities currently failing, instead of the number that ever have.
            var rows = db.LoginAttempts.Where(a => a.Identity == identity && a.Source == source).ToList();
            if (rows.Count == 0) return;

            db.LoginAttempts.RemoveRange(rows);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not clear the shared login-attempt counter: {Message}", ex.Message);
        }
    }

    /// <summary>The login, lower-cased and length-capped to the column. Null when there is none.</summary>
    private static string? Normalize(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;

        var identity = userName.Trim().ToLowerInvariant();
        return identity.Length > 255 ? identity[..255] : identity;
    }

    /// <summary>
    /// The source address, or the literal <c>-</c>. Never null: the unique index is on
    /// (identity, source), and MySQL treats every NULL as distinct, so a null source would create a
    /// new row per failure instead of incrementing one.
    /// </summary>
    private static string NormalizeSource(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return "-";

        var source = ipAddress.Trim();
        return source.Length > 64 ? source[..64] : source;
    }
}
