using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A formal, expiring, authorized decision to accept risk (Track 3 milestone 3.2.3, entity design
/// generalized by Track 8.1 so one table covers both findings and risks).
///
/// The expiry date is not nullable on purpose. An acceptance without one is the thing this entity
/// exists to prevent: "accepted" quietly becoming "forgotten", with no date on which anyone is
/// obliged to look at it again.
/// </summary>
public class RiskAcceptance
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// The risk this acceptance covers (Track 8 milestone 8.1.1). Nullable because the same table
    /// serves finding-level acceptances, which link through <see cref="Findings"/> instead. Exactly
    /// one of the two shapes is populated; the service refuses a row that is neither or both.
    /// </summary>
    public int? RiskId { get; set; }

    /// <summary>Who asked for the exception, as distinct from who authorized it. The pair is what
    /// makes maker-checker (8.3.2) checkable after the fact.</summary>
    public int? RequestedById { get; set; }

    /// <summary>When the acceptance takes effect. Defaults to creation time.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// The acceptance this one renews. A renewal is a new row, never an edited expiry date: the
    /// question an auditor asks is "what was approved, by whom, until when", and moving a date
    /// erases the previous answer.
    /// </summary>
    public int? RenewedFromId { get; set; }

    /// <summary>Why the organization is accepting this. Rich text; the auditor-facing field.</summary>
    public string? BusinessJustification { get; set; }

    /// <summary>
    /// The manager who authorized it. Required — an acceptance nobody signed is not an acceptance.
    /// </summary>
    public int AuthorizingManagerId { get; set; }

    /// <summary>Required. The date on which this lapses and its findings come back.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>What the organization is doing instead of remediating.</summary>
    public string? CompensatingControls { get; set; }

    /// <summary>
    /// The residual score at the moment of acceptance. A snapshot rather than a live lookup: the
    /// authorizing manager signed off on the risk as it was scored then, and re-scoring later must
    /// not retroactively change what they approved.
    /// </summary>
    public double? ResidualScoreSnapshot { get; set; }

    public RiskAcceptanceStatus Status { get; set; } = RiskAcceptanceStatus.Active;

    public int? EntityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int? RevokedById { get; set; }

    public string? RevocationReason { get; set; }

    /// <summary>
    /// The smallest pre-expiry warning threshold already sent, in days. The expiry job compares
    /// against this to stay idempotent: without it, a job that runs twice on a T-7 day sends the
    /// warning twice, and re-running a failed job becomes something an operator has to think about.
    /// </summary>
    public int? LastWarningDaysBefore { get; set; }

    public virtual User? AuthorizingManager { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? RevokedBy { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual ICollection<RiskAcceptanceFinding> Findings { get; set; } = new List<RiskAcceptanceFinding>();

    public virtual Risk? Risk { get; set; }

    public virtual User? RequestedBy { get; set; }

    public virtual RiskAcceptance? RenewedFrom { get; set; }
}
