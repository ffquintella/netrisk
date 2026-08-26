namespace DAL.Entities;

/// <summary>
/// Persisted brute-force counter for one (identity, source) pair (security finding NR-2026-008b).
///
/// The in-memory <c>ConcurrentDictionary</c> this replaces gave every API instance its own budget,
/// so an attacker spreading attempts across a load-balanced deployment multiplied the allowance by
/// the instance count, and a restart cleared it entirely. The row is the shared counter.
///
/// Only *failures* are written. A successful login deletes the row, so the steady-state size of this
/// table is the number of identities currently failing — not the request volume.
/// </summary>
public class LoginAttempt
{
    public int Id { get; set; }

    /// <summary>The login attempted, lower-cased. Not a foreign key: an attacker guesses names
    /// that do not exist, and those attempts have to be counted too.</summary>
    public string Identity { get; set; } = null!;

    /// <summary>Client address, or <c>-</c> when none is available.</summary>
    public string Source { get; set; } = null!;

    public int FailureCount { get; set; }

    public DateTime FirstFailureAt { get; set; }

    public DateTime LastFailureAt { get; set; }

    /// <summary>Set once the threshold is crossed; the handler refuses until it passes.</summary>
    public DateTime? LockedUntil { get; set; }
}
