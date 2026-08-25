namespace DAL.Entities;

/// <summary>
/// One single-use recovery code for a user whose second factor is unavailable
/// (Track 4 milestone 4.3.3).
///
/// Hashed, never stored in clear, and shown exactly once at generation — the same discipline as an
/// API token, for the same reason: a readable recovery code in the database is a permanent bypass of
/// the hardware factor it exists to back up.
/// </summary>
public class MfaRecoveryCode
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string CodeHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    /// <summary>Who generated the batch. Generation is an administrative act and is audited.</summary>
    public int? CreatedById { get; set; }

    /// <summary>Set the moment the code is redeemed. A used code is never accepted again.</summary>
    public DateTime? UsedAt { get; set; }

    public virtual User? User { get; set; }

    public virtual User? CreatedBy { get; set; }
}
