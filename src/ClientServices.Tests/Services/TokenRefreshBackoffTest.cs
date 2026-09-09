using System;
using ClientServices.Services;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Cover for the rate-limiting half of the 2026-09-09 refresh loop. The desktop client's
/// notification timer ticks every 10 seconds; while the refresh keeps failing, nothing stopped it
/// from being retried on every tick and logged on every retry — 4,537 identical Error lines in
/// twelve hours.
/// </summary>
public class TokenRefreshBackoffTest
{
    /// <summary>A mutable clock, because the alternative is a test that sleeps for thirty seconds.</summary>
    private sealed class TestClock
    {
        public DateTime Now = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan by) => Now += by;
    }

    private const string Html = "non-json|/Authentication/GetToken|200|text/html";

    [Fact]
    public void AFreshBackoffAllowsTheFirstAttempt()
    {
        Assert.True(new TokenRefreshBackoff().ShouldAttempt());
    }

    /// <summary>The defect: the very next 10-second tick tried again.</summary>
    [Fact]
    public void TheTickAfterAFailureIsSuppressed()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure(Html, -1);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False(backoff.ShouldAttempt());
    }

    [Fact]
    public void AnAttemptIsAllowedAgainOnceTheFirstDelayHasElapsed()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure(Html, -1);
        clock.Advance(TokenRefreshBackoff.FirstDelay);

        Assert.True(backoff.ShouldAttempt());
    }

    /// <summary>Doubling, so a server that stays down is asked about progressively less often.</summary>
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    public void TheDelayDoublesWithEachConsecutiveFailure(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TokenRefreshBackoff.DelayAfter(failures));
    }

    [Fact]
    public void TheDelayIsCappedAndNeverGoesNegative()
    {
        Assert.Equal(TokenRefreshBackoff.MaxDelay, TokenRefreshBackoff.DelayAfter(6));

        // A client left open for a day reaches a four-figure failure count; 30s * 2^n on a
        // TimeSpan overflows long before that, and an overflowed delay is a delay in the past.
        foreach (var failures in new[] { 20, 40, 100, 5000, int.MaxValue })
            Assert.Equal(TokenRefreshBackoff.MaxDelay, TokenRefreshBackoff.DelayAfter(failures));

        Assert.Equal(TimeSpan.Zero, TokenRefreshBackoff.DelayAfter(0));
        Assert.Equal(TimeSpan.Zero, TokenRefreshBackoff.DelayAfter(-1));
    }

    /// <summary>Twelve hours of one failure has to cost tens of attempts, not thousands.</summary>
    [Fact]
    public void TwelveHoursOfOneFailureCostsFarFewerAttemptsThanTicks()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);
        var attempts = 0;
        var reports = 0;

        // The notification timer's own cadence, for twelve hours: 4,320 ticks.
        for (var tick = 0; tick < 12 * 60 * 6; tick++)
        {
            if (backoff.ShouldAttempt())
            {
                attempts++;
                if (backoff.RecordFailure(Html, -1).ShouldReport) reports++;
            }
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.InRange(attempts, 1, 100);
        Assert.InRange(reports, 1, 13);
    }

    /// <summary>The same failure repeating is logged once, not once per attempt.</summary>
    [Fact]
    public void OnlyTheFirstOccurrenceOfAFailureIsReported()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        Assert.True(backoff.RecordFailure(Html, -1).ShouldReport);

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TokenRefreshBackoff.MaxDelay);
            Assert.False(backoff.RecordFailure(Html, -1).ShouldReport);
        }
    }

    /// <summary>...but it is repeated at a low rate, so a stuck client is not silent forever.</summary>
    [Fact]
    public void APersistentFailureIsReportedAgainAfterTheReportInterval()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure(Html, -1);
        clock.Advance(TokenRefreshBackoff.ReportInterval - TimeSpan.FromMinutes(1));
        Assert.False(backoff.RecordFailure(Html, -1).ShouldReport);

        clock.Advance(TokenRefreshBackoff.ReportInterval);
        Assert.True(backoff.RecordFailure(Html, -1).ShouldReport);
    }

    /// <summary>A different failure is new information and is reported immediately.</summary>
    [Fact]
    public void ADifferentFailureIsReportedImmediately()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure(Html, -1);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.True(backoff.RecordFailure("status|/Authentication/GetToken|403", 1).ShouldReport);
    }

    /// <summary>
    /// A failure that keeps changing must still back off — a server failing a different way each
    /// time is not a server to poll every ten seconds.
    /// </summary>
    [Fact]
    public void TheDelayEscalatesEvenWhenTheFailureKeepsChanging()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure("a", -1);
        backoff.RecordFailure("b", -1);
        var third = backoff.RecordFailure("c", -1);

        Assert.Equal(3, third.ConsecutiveFailures);
        Assert.Equal(TokenRefreshBackoff.DelayAfter(3), third.RetryAfter);
    }

    /// <summary>A success clears everything: the next failure is loud again and waits 30s again.</summary>
    [Fact]
    public void ASuccessResetsTheBackoffAndTheReporting()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        for (var i = 0; i < 4; i++)
        {
            backoff.RecordFailure(Html, -1);
            clock.Advance(TokenRefreshBackoff.MaxDelay);
        }

        backoff.RecordSuccess();

        Assert.True(backoff.ShouldAttempt());
        Assert.Equal(0, backoff.ConsecutiveFailures);
        Assert.Equal(0, backoff.LastResult);
        Assert.Equal(TimeSpan.Zero, backoff.RetryAfter);

        var next = backoff.RecordFailure(Html, -1);
        Assert.True(next.ShouldReport);
        Assert.Equal(TokenRefreshBackoff.FirstDelay, next.RetryAfter);
    }

    /// <summary>
    /// A suppressed attempt reports the last real result, and before any attempt has been made that
    /// is a failure — never 0, which the caller reads as "use the token the refresh produced".
    /// </summary>
    [Fact]
    public void TheReplayedResultIsNeverASuccessWhileTheBackoffIsActive()
    {
        var backoff = new TokenRefreshBackoff();

        Assert.Equal(-1, backoff.LastResult);

        backoff.RecordFailure("status|/Authentication/GetToken|403", 1);
        Assert.Equal(1, backoff.LastResult);
    }

    /// <summary>The remaining wait shrinks with the clock and bottoms out at zero.</summary>
    [Fact]
    public void RetryAfterCountsDownAndDoesNotGoNegative()
    {
        var clock = new TestClock();
        var backoff = new TokenRefreshBackoff(() => clock.Now);

        backoff.RecordFailure(Html, -1);
        Assert.Equal(TokenRefreshBackoff.FirstDelay, backoff.RetryAfter);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(20), backoff.RetryAfter);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.Zero, backoff.RetryAfter);
    }
}
