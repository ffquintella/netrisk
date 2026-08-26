namespace ServerServices.Interfaces;

/// <summary>
/// The outcome of asking whether a credential presentation may even be attempted.
/// </summary>
/// <param name="IsLockedOut">True when the attempt must be refused without checking the password.</param>
/// <param name="RetryAfter">
/// How long until the caller may try again. Meaningful only when <paramref name="IsLockedOut"/> is
/// true; it is what a <c>Retry-After</c> header is set from.
/// </param>
/// <param name="FailureCount">Consecutive failures recorded for this identity so far.</param>
public readonly record struct LoginAttemptState(bool IsLockedOut, TimeSpan RetryAfter, int FailureCount);

/// <summary>
/// Brute-force throttling for the authentication endpoints (Track 7 milestone 7.3.2).
///
/// Track 6 established that the <c>failed_login_attempts</c> column carried no live logic, and the
/// Track 7 audit confirmed it: <c>BasicAuthenticationHandler</c> read <c>Lockout</c> but nothing ever
/// set it, so a password could be guessed as fast as bcrypt would answer. This interface is the
/// missing half — every credential check consults it first and reports its outcome to it.
///
/// Keyed by both account and source address, deliberately. Keying only on the account lets one
/// attacker lock every user out; keying only on the address lets a distributed attempt through.
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>
    /// Whether an attempt for <paramref name="userName"/> from <paramref name="ipAddress"/> is
    /// currently allowed, and if not, for how much longer.
    /// </summary>
    LoginAttemptState Check(string? userName, string? ipAddress);

    /// <summary>
    /// Records a failed credential presentation and returns the resulting state, so the caller can
    /// log the transition into lockout exactly once.
    /// </summary>
    LoginAttemptState RegisterFailure(string? userName, string? ipAddress);

    /// <summary>
    /// Clears the counters after a successful authentication. Called on success only — a successful
    /// login is the only evidence that the earlier failures were the legitimate owner mistyping.
    /// </summary>
    void RegisterSuccess(string? userName, string? ipAddress);
}
