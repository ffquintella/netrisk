using System;

namespace ClientServices.Services;

/// <summary>
/// What to do about a token refresh that has just failed: how long to wait before trying again, and
/// whether this failure is worth a log line.
/// </summary>
/// <param name="ShouldReport">
/// True when the caller should log at Error level: the failure is new, or it is the same one but the
/// last loud report was <see cref="TokenRefreshBackoff.ReportInterval"/> ago. False means "log this
/// quietly, the operator has already been told".
/// </param>
/// <param name="ConsecutiveFailures">How many refreshes in a row have failed, successes resetting it.</param>
/// <param name="RetryAfter">How long the next refresh will be suppressed for.</param>
public sealed record TokenRefreshFailure(bool ShouldReport, int ConsecutiveFailures, TimeSpan RetryAfter);

/// <summary>
/// The rate limiter on token refreshes.
///
/// <c>NavigationBarViewModel</c> runs a 10-second timer, every tick reaches the REST layer, and
/// <see cref="RestService.GetClient"/> refreshes the token whenever it is inside its renewal window.
/// While the refresh keeps failing — a reverse proxy answering with an HTML page, the API down, the
/// server refusing the token — nothing in that chain ever backed off: every tick sent another
/// refresh and logged another Error. nr-gui20260909.log holds 4,537 of them, twelve hours of one
/// failure repeated every ten seconds, which is both useless to read and a request per tick against
/// a server that is already unwell.
///
/// So a failed refresh buys silence: <see cref="FirstDelay"/> after the first, doubling up to
/// <see cref="MaxDelay"/>, reset by the first success. A refresh attempted inside that window is not
/// performed at all and reports the previous result, which is the same answer it would have got.
/// Twelve hours of a persistent failure costs about 75 attempts instead of 4,320, and one Error line
/// per <see cref="ReportInterval"/> instead of one per attempt.
///
/// The clock is injectable because the alternative is a test that sleeps for thirty seconds. Every
/// member locks: the refresh is reached from the notification timer's thread pool thread as well as
/// from the UI thread.
/// </summary>
public sealed class TokenRefreshBackoff
{
    /// <summary>The wait after a single failure — three notification ticks.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);

    /// <summary>The ceiling on the doubling. Long enough to stop mattering, short enough that a server coming back is noticed.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    /// <summary>How often an unchanged, still-failing refresh earns another Error line.</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromHours(1);

    private readonly Func<DateTime> _now;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTime _nextAttempt = DateTime.MinValue;
    private string? _lastKey;
    private DateTime _lastReport = DateTime.MinValue;
    private int _lastResult = -1;

    /// <param name="now">The clock, defaulting to UTC wall time.</param>
    public TokenRefreshBackoff(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>
    /// The result the last attempted refresh returned, which is what a suppressed attempt reports.
    ///
    /// Starts at <c>-1</c> — "failed for an unknown reason" — so a suppressed refresh can never be
    /// mistaken for a success, whatever order the calls arrive in.
    /// </summary>
    public int LastResult
    {
        get { lock (_gate) return _lastResult; }
    }

    /// <summary>Consecutive failures, for a caller that wants to describe the state.</summary>
    public int ConsecutiveFailures
    {
        get { lock (_gate) return _consecutiveFailures; }
    }

    /// <summary>How much of the current suppression window is left; zero when a refresh may go out.</summary>
    public TimeSpan RetryAfter
    {
        get
        {
            lock (_gate)
            {
                var remaining = _nextAttempt - _now();
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    /// <summary>True when a refresh may be attempted now.</summary>
    public bool ShouldAttempt()
    {
        lock (_gate) return _now() >= _nextAttempt;
    }

    /// <summary>Clears the backoff. The next failure starts again at <see cref="FirstDelay"/>.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _nextAttempt = DateTime.MinValue;
            _lastKey = null;
            _lastReport = DateTime.MinValue;
            _lastResult = 0;
        }
    }

    /// <summary>
    /// Records a failed refresh and returns how to handle it.
    /// </summary>
    /// <param name="key">
    /// The kind of failure, from <see cref="Http.ServerResponseProblem.Key"/>. Only used to decide
    /// whether the log line is a repeat — the delay escalates on any consecutive failure, because a
    /// server that fails a different way each time is not a server to keep polling.
    /// </param>
    /// <param name="result">What the refresh returned, replayed by suppressed attempts.</param>
    public TokenRefreshFailure RecordFailure(string key, int result)
    {
        lock (_gate)
        {
            var now = _now();
            var isRepeat = key == _lastKey;

            _consecutiveFailures++;
            _lastKey = key;
            _lastResult = result;
            _nextAttempt = now + DelayAfter(_consecutiveFailures);

            var shouldReport = !isRepeat || now - _lastReport >= ReportInterval;
            if (shouldReport) _lastReport = now;

            return new TokenRefreshFailure(shouldReport, _consecutiveFailures, _nextAttempt - now);
        }
    }

    /// <summary>
    /// The wait owed after <paramref name="consecutiveFailures"/> failures in a row:
    /// 30s, 1m, 2m, 4m, 8m, then <see cref="MaxDelay"/> forever.
    /// </summary>
    public static TimeSpan DelayAfter(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;

        // Capped before the shift, not after: 2^n on a long overflows into a negative delay somewhere
        // around the thirty-second consecutive failure, and a persistent failure reaches that in a day.
        var doublings = Math.Min(consecutiveFailures - 1, 20);
        var delay = FirstDelay * Math.Pow(2, doublings);

        return delay > MaxDelay ? MaxDelay : delay;
    }
}
