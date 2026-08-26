using System.Collections.Concurrent;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Security;

/// <summary>
/// In-process progressive lockout for credential authentication (Track 7 milestone 7.3.2,
/// finding NR-2026-008).
///
/// The policy: the first few consecutive failures for an identity cost nothing, and each failure
/// after that locks it out for a doubling interval, capped at <see cref="MaxLockout"/>. Counters
/// decay after <see cref="FailureWindow"/> of quiet, so a user who mistypes twice today and twice
/// next month is never locked out.
///
/// Doubling rather than a flat "five strikes and you are out for fifteen minutes" because a flat
/// lockout is a denial-of-service primitive: anyone who knows a colleague's login can keep them out
/// indefinitely. Doubling from a short base slows a guessing attack to uselessness within a dozen
/// attempts while a legitimate user who has just remembered their password waits seconds.
///
/// <para><b>The two keys have very different budgets, and that asymmetry is the point.</b> An
/// attempt counts against the *account* and against the *source address*, because throttling only
/// one is either bypassable (distributed guessing) or abusable (locking a colleague out on purpose).
/// But a source address is not one person: NetRisk is normally deployed behind a reverse proxy,
/// where <c>RemoteIpAddress</c> is the proxy for every client in the organisation. With one shared
/// budget, five colleagues fumbling their passwords would lock out everybody — including users with
/// no failures of their own. So the address budget is <see cref="FreeAttemptsPerAddress"/>, an order
/// of magnitude above the per-account one: still far below what a guessing run needs, and far above
/// what an office produces in half an hour.</para>
///
/// <para><b>Deliberate limitation.</b> The state is per process and in memory, so it resets on
/// restart and is not shared between API instances behind a load balancer. That is recorded in the
/// findings register as NR-2026-008b: closing it needs a persisted counter, which is a schema change.
/// It is still a large improvement on the previous state, which was no throttle at all.</para>
/// </summary>
public class LoginAttemptTracker : ILoginAttemptTracker
{
    /// <summary>Failures allowed against one *account* before any delay is imposed.</summary>
    internal const int FreeAttempts = 4;

    /// <summary>
    /// Failures allowed from one *source address* before any delay is imposed.
    ///
    /// Deliberately much larger than <see cref="FreeAttempts"/>. Behind a reverse proxy every client
    /// shares an address, so this budget is spent by a whole organisation; four would mean two
    /// colleagues mistyping their passwords locks out everyone else. Fifty in a thirty-minute window
    /// is well above normal human error at any realistic scale and still far below what guessing a
    /// password needs — and the per-account budget of four is what actually stops the guessing.
    ///
    /// An installation that terminates TLS at a proxy should also configure forwarded headers with a
    /// known-proxy list so this partitions by the real client; see docs/security/DATA_PROTECTION.md.
    /// </summary>
    internal const int FreeAttemptsPerAddress = 50;

    /// <summary>Lockout imposed on the first failure past <see cref="FreeAttempts"/>.</summary>
    internal static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(5);

    /// <summary>Ceiling on the doubling, so an account is never permanently unreachable.</summary>
    internal static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

    /// <summary>How long a quiet period has to be before the counters are forgotten.</summary>
    internal static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Cap on tracked identities. An attacker cycling usernames would otherwise turn this into an
    /// unbounded dictionary; when the cap is hit the expired entries are swept and, if that is not
    /// enough, the oldest are dropped. Dropping an entry only forgives failures, never grants
    /// access, so the failure mode is a weaker throttle rather than an open door.
    /// </summary>
    internal const int MaxTrackedIdentities = 20_000;

    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset LastFailure;
        public DateTimeOffset LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public LoginAttemptTracker(ILogger logger) : this(logger, TimeProvider.System) { }

    /// <summary>Test seam: drive the clock explicitly rather than sleeping in a test.</summary>
    public LoginAttemptTracker(ILogger logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _time = timeProvider;
    }

    public LoginAttemptState Check(string? userName, string? ipAddress)
    {
        var now = _time.GetUtcNow();
        var worst = new LoginAttemptState(false, TimeSpan.Zero, 0);

        foreach (var (key, _) in KeysFor(userName, ipAddress))
        {
            if (!_counters.TryGetValue(key, out var counter)) continue;

            if (now - counter.LastFailure > FailureWindow)
            {
                _counters.TryRemove(key, out _);
                continue;
            }

            var remaining = counter.LockedUntil - now;
            if (remaining > worst.RetryAfter)
                worst = new LoginAttemptState(true, remaining, counter.Failures);
            else if (counter.Failures > worst.FailureCount)
                worst = worst with { FailureCount = counter.Failures };
        }

        return worst;
    }

    public LoginAttemptState RegisterFailure(string? userName, string? ipAddress)
    {
        var now = _time.GetUtcNow();
        var result = new LoginAttemptState(false, TimeSpan.Zero, 0);

        Trim(now);

        foreach (var (key, freeAttempts) in KeysFor(userName, ipAddress))
        {
            var counter = _counters.AddOrUpdate(key,
                _ => new Counter { Failures = 1, LastFailure = now },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        // A long quiet period resets rather than accumulating: the alternative
                        // punishes a user for a typo they made last month.
                        if (now - existing.LastFailure > FailureWindow) existing.Failures = 0;
                        existing.Failures++;
                        existing.LastFailure = now;
                    }

                    return existing;
                });

            TimeSpan lockout;
            int failures;
            lock (counter)
            {
                failures = counter.Failures;
                lockout = LockoutFor(failures, freeAttempts);
                if (lockout > TimeSpan.Zero) counter.LockedUntil = now + lockout;
            }

            if (lockout > result.RetryAfter)
                result = new LoginAttemptState(lockout > TimeSpan.Zero, lockout, failures);
            else if (failures > result.FailureCount)
                result = result with { FailureCount = failures };
        }

        if (result.IsLockedOut)
            _logger.Warning(
                "Throttling authentication for {User} from {Ip}: {Failures} consecutive failures, "
                + "next attempt allowed in {Seconds}s",
                Redact(userName), ipAddress ?? "(unknown)", result.FailureCount,
                (int)result.RetryAfter.TotalSeconds);
        else
            _logger.Information("Failed authentication for {User} from {Ip} (attempt {Failures})",
                Redact(userName), ipAddress ?? "(unknown)", result.FailureCount);

        return result;
    }

    public void RegisterSuccess(string? userName, string? ipAddress)
    {
        foreach (var (key, _) in KeysFor(userName, ipAddress))
            _counters.TryRemove(key, out _);
    }

    /// <summary>
    /// The lockout for a given consecutive-failure count: nothing for the first
    /// <paramref name="freeAttempts"/>, then <see cref="BaseLockout"/> doubling per further failure
    /// up to <see cref="MaxLockout"/>.
    /// </summary>
    internal static TimeSpan LockoutFor(int failures, int freeAttempts = FreeAttempts)
    {
        if (failures <= freeAttempts) return TimeSpan.Zero;

        var steps = Math.Min(failures - freeAttempts - 1, 20);
        var seconds = BaseLockout.TotalSeconds * Math.Pow(2, steps);

        return seconds >= MaxLockout.TotalSeconds ? MaxLockout : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// The keys an attempt counts against, each with its own free-attempt budget: the account
    /// (<see cref="FreeAttempts"/>) and the source address (<see cref="FreeAttemptsPerAddress"/>).
    /// See the class summary for why the two budgets differ by an order of magnitude.
    /// </summary>
    private static IEnumerable<(string Key, int FreeAttempts)> KeysFor(string? userName, string? ipAddress)
    {
        if (!string.IsNullOrWhiteSpace(userName))
            yield return ("u:" + userName.Trim().ToLowerInvariant(), LoginAttemptTracker.FreeAttempts);

        if (!string.IsNullOrWhiteSpace(ipAddress))
            yield return ("i:" + ipAddress.Trim(), FreeAttemptsPerAddress);
    }

    /// <summary>Keeps the dictionary bounded. See <see cref="MaxTrackedIdentities"/>.</summary>
    private void Trim(DateTimeOffset now)
    {
        if (_counters.Count < MaxTrackedIdentities) return;

        foreach (var (key, counter) in _counters)
            if (now - counter.LastFailure > FailureWindow)
                _counters.TryRemove(key, out _);

        if (_counters.Count < MaxTrackedIdentities) return;

        foreach (var (key, _) in _counters.OrderBy(pair => pair.Value.LastFailure)
                     .Take(_counters.Count - MaxTrackedIdentities + 1))
            _counters.TryRemove(key, out _);
    }

    /// <summary>
    /// A login is not a secret, but it is personal data and it ends up in a log that is often shipped
    /// off-host. The first two characters are enough for an operator correlating an attack.
    /// </summary>
    private static string Redact(string? userName) =>
        string.IsNullOrEmpty(userName)
            ? "(none)"
            : userName.Length <= 2 ? userName + "…" : userName[..2] + "…";
}
