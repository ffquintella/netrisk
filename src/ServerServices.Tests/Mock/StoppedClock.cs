using System;

namespace ServerServices.Tests.Mock;

/// <summary>
/// A clock that only moves when a test moves it.
///
/// Hand-rolled rather than <c>Microsoft.Extensions.TimeProvider.Testing</c>: the whole need is
/// "advance by n", and adding a package for two methods is not a trade worth making. The same
/// reasoning, and the same shape, as the private clock in <c>LoginAttemptTrackerTest</c> — this one
/// is shared because cache-expiry tests in more than one place need it.
/// </summary>
public sealed class StoppedClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
