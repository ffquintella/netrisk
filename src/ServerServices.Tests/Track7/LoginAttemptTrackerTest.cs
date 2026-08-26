using System;
using JetBrains.Annotations;
using Serilog;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Track7;

/// <summary>
/// Track 7 finding NR-2026-008 — no brute-force protection anywhere.
///
/// <c>BasicAuthenticationHandler</c> read <c>User.Lockout</c>, but nothing in the codebase ever set
/// it: the <c>failed_login_attempts</c> column Track 6 inventoried had no live logic behind it. A
/// password could therefore be guessed as fast as bcrypt would answer, forever.
/// </summary>
[TestSubject(typeof(LoginAttemptTracker))]
public class LoginAttemptTrackerTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    /// <summary>
    /// A hand-rolled controllable clock rather than <c>Microsoft.Extensions.TimeProvider.Testing</c>:
    /// the whole need is "advance by n", and adding a package to the solution for two methods is not
    /// a trade worth making.
    /// </summary>
    private sealed class StoppedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static (LoginAttemptTracker Tracker, StoppedClock Clock) Build()
    {
        var clock = new StoppedClock(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        return (new LoginAttemptTracker(Log, clock), clock);
    }

    [Fact]
    public void AFreshIdentityIsNotThrottled()
    {
        var (tracker, _) = Build();

        var state = tracker.Check("alice", "203.0.113.7");

        Assert.False(state.IsLockedOut);
        Assert.Equal(0, state.FailureCount);
    }

    /// <summary>
    /// The first few failures cost nothing. A user who mistypes their password twice must not be
    /// delayed, or the control is experienced as a bug and gets turned off.
    /// </summary>
    [Fact]
    public void TheFirstFailuresAreFree()
    {
        var (tracker, _) = Build();

        for (var i = 1; i <= LoginAttemptTracker.FreeAttempts; i++)
        {
            var state = tracker.RegisterFailure("alice", "203.0.113.7");
            Assert.False(state.IsLockedOut);
            Assert.Equal(i, state.FailureCount);
        }

        Assert.False(tracker.Check("alice", "203.0.113.7").IsLockedOut);
    }

    /// <summary>The regression assertion: guessing eventually stops being free.</summary>
    [Fact]
    public void OneFailurePastTheFreeAllowanceLocksTheIdentityOut()
    {
        var (tracker, _) = Build();

        for (var i = 0; i < LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "203.0.113.7");

        var state = tracker.RegisterFailure("alice", "203.0.113.7");

        Assert.True(state.IsLockedOut);
        Assert.Equal(LoginAttemptTracker.BaseLockout, state.RetryAfter);
        Assert.True(tracker.Check("alice", "203.0.113.7").IsLockedOut);
    }

    [Fact]
    public void TheLockoutDoublesAndIsCapped()
    {
        Assert.Equal(TimeSpan.Zero, LoginAttemptTracker.LockoutFor(1));
        Assert.Equal(TimeSpan.Zero, LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttempts));
        Assert.Equal(LoginAttemptTracker.BaseLockout,
            LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttempts + 1));
        Assert.Equal(LoginAttemptTracker.BaseLockout * 2,
            LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttempts + 2));
        Assert.Equal(LoginAttemptTracker.BaseLockout * 4,
            LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttempts + 3));

        // The cap is what stops the throttle from becoming a permanent denial of service against a
        // user whose login somebody else knows.
        Assert.Equal(LoginAttemptTracker.MaxLockout, LoginAttemptTracker.LockoutFor(200));

        // The same curve, shifted, for the address key's larger budget.
        Assert.Equal(TimeSpan.Zero,
            LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttemptsPerAddress,
                LoginAttemptTracker.FreeAttemptsPerAddress));
        Assert.Equal(LoginAttemptTracker.BaseLockout,
            LoginAttemptTracker.LockoutFor(LoginAttemptTracker.FreeAttemptsPerAddress + 1,
                LoginAttemptTracker.FreeAttemptsPerAddress));
    }

    [Fact]
    public void TheLockoutExpiresOnItsOwn()
    {
        var (tracker, clock) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "203.0.113.7");

        Assert.True(tracker.Check("alice", "203.0.113.7").IsLockedOut);

        clock.Advance(LoginAttemptTracker.BaseLockout + TimeSpan.FromSeconds(1));

        Assert.False(tracker.Check("alice", "203.0.113.7").IsLockedOut);
    }

    [Fact]
    public void CountersDecayAfterAQuietPeriod()
    {
        var (tracker, clock) = Build();

        for (var i = 0; i < LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "203.0.113.7");

        clock.Advance(LoginAttemptTracker.FailureWindow + TimeSpan.FromMinutes(1));

        // Back to a clean slate, so the next mistyped password is free again.
        var state = tracker.RegisterFailure("alice", "203.0.113.7");
        Assert.Equal(1, state.FailureCount);
        Assert.False(state.IsLockedOut);
    }

    [Fact]
    public void ASuccessfulLoginClearsTheCounters()
    {
        var (tracker, _) = Build();

        for (var i = 0; i < LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "203.0.113.7");

        tracker.RegisterSuccess("alice", "203.0.113.7");

        Assert.Equal(0, tracker.Check("alice", "203.0.113.7").FailureCount);
        Assert.False(tracker.RegisterFailure("alice", "203.0.113.7").IsLockedOut);
    }

    /// <summary>
    /// Keying on the account alone would let an attacker hammer one login from a botnet; keying on
    /// the address alone would let them cycle logins from one host. Both keys are tracked, so both
    /// patterns are throttled — this test pins the account half.
    /// </summary>
    [Fact]
    public void ThrottlingFollowsTheAccountAcrossSourceAddresses()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", $"203.0.113.{i}");

        Assert.True(tracker.Check("alice", "198.51.100.9").IsLockedOut);
    }

    /// <summary>
    /// And this one pins the address half: cycling usernames does not reset the budget. It takes far
    /// more attempts than the per-account budget, deliberately — see the next test.
    /// </summary>
    [Fact]
    public void ThrottlingFollowsTheSourceAddressAcrossAccounts()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttemptsPerAddress; i++)
            tracker.RegisterFailure($"user{i}", "203.0.113.7");

        Assert.True(tracker.Check("someone-else", "203.0.113.7").IsLockedOut);
    }

    /// <summary>
    /// The regression assertion for a self-inflicted denial of service. NetRisk is normally deployed
    /// behind a reverse proxy, where <c>RemoteIpAddress</c> is the proxy for *every* client in the
    /// organisation. With one shared budget of four, two colleagues mistyping their passwords would
    /// lock out everybody — including users with no failures of their own. The address budget has to
    /// be an order of magnitude larger than the per-account one.
    /// </summary>
    [Fact]
    public void ASharedSourceAddressIsNotLockedOutByAFewColleaguesMistyping()
    {
        var (tracker, _) = Build();

        // Alice fumbles three times, Bob twice — five failures from the shared proxy address, above
        // the per-account budget of four.
        for (var i = 0; i < 3; i++) tracker.RegisterFailure("alice", "10.0.0.2");
        for (var i = 0; i < 2; i++) tracker.RegisterFailure("bob", "10.0.0.2");

        // Carol, who has typed nothing wrong, must still be able to sign in.
        Assert.False(tracker.Check("carol", "10.0.0.2").IsLockedOut);
        // And so must Alice and Bob, who are each still inside their own budget.
        Assert.False(tracker.Check("alice", "10.0.0.2").IsLockedOut);
        Assert.False(tracker.Check("bob", "10.0.0.2").IsLockedOut);
    }

    /// <summary>
    /// The counterpart: the address budget is generous, not absent. Sustained guessing from one
    /// address is still stopped even if every attempt names a different account.
    /// </summary>
    [Fact]
    public void ASustainedRunFromOneAddressIsStillStopped()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttemptsPerAddress; i++)
            tracker.RegisterFailure($"guess{i}", "203.0.113.7");

        Assert.True(tracker.Check("guess999", "203.0.113.7").IsLockedOut);
    }

    /// <summary>
    /// And the per-account budget is what actually stops password guessing — it is unaffected by the
    /// larger address allowance.
    /// </summary>
    [Fact]
    public void TheAccountBudgetIsUnaffectedByTheLargerAddressAllowance()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "10.0.0.2");

        Assert.True(tracker.Check("alice", "10.0.0.2").IsLockedOut);
        // Nobody else on that address is affected yet.
        Assert.False(tracker.Check("carol", "10.0.0.2").IsLockedOut);
    }

    [Fact]
    public void TheAddressBudgetIsAnOrderOfMagnitudeAboveTheAccountBudget() =>
        Assert.True(LoginAttemptTracker.FreeAttemptsPerAddress >= LoginAttemptTracker.FreeAttempts * 10,
            "a shared proxy address must tolerate far more failures than one account");

    [Fact]
    public void AnUnrelatedAccountFromAnUnrelatedAddressIsUnaffected()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure("alice", "203.0.113.7");

        Assert.False(tracker.Check("bob", "198.51.100.9").IsLockedOut);
    }

    [Fact]
    public void LoginNamesAreComparedWithoutRegardToCaseOrSurroundingSpace()
    {
        var (tracker, _) = Build();

        for (var i = 0; i <= LoginAttemptTracker.FreeAttempts; i++)
            tracker.RegisterFailure(" ALICE ", "203.0.113.7");

        Assert.True(tracker.Check("alice", "198.51.100.9").IsLockedOut);
    }

    [Fact]
    public void MissingIdentifiersAreToleratedRatherThanThrowing()
    {
        var (tracker, _) = Build();

        Assert.False(tracker.Check(null, null).IsLockedOut);
        Assert.False(tracker.RegisterFailure(null, null).IsLockedOut);
        tracker.RegisterSuccess(null, null);
    }
}
