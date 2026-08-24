using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One state transition on one finding (Track 3 milestone 3.2.2). Append-only: there is no update
/// or delete path anywhere in the API, because the value of this table is that nobody can quietly
/// rewrite the record of who suppressed what.
/// </summary>
public class FindingStatusHistory
{
    public int Id { get; set; }

    public int VulnerabilityId { get; set; }

    /// <summary>
    /// Null for the row that records a finding's creation — there is no state it came from, and
    /// writing Active there would misrepresent a brand-new finding as a transition.
    /// </summary>
    public FindingStatus? FromStatus { get; set; }

    public FindingStatus ToStatus { get; set; }

    /// <summary>
    /// Who did it. Null when the actor was a scheduled job rather than a person; the
    /// <see cref="Source"/> column is what distinguishes "nobody" from "unknown".
    /// </summary>
    public int? UserId { get; set; }

    public DateTime ChangedAt { get; set; }

    public FindingStatusChangeSource Source { get; set; } = FindingStatusChangeSource.Manual;

    /// <summary>
    /// Why. Mandatory for suppressing transitions (see
    /// <see cref="FindingStatusExtensions.RequiresJustification"/>) and enforced in the service, not
    /// just the UI.
    /// </summary>
    public string? Justification { get; set; }

    /// <summary>The acceptance that caused a transition to <see cref="FindingStatus.RiskAccepted"/>.</summary>
    public int? RiskAcceptanceId { get; set; }

    /// <summary>The canonical finding, for a transition to <see cref="FindingStatus.Duplicate"/>.</summary>
    public int? DuplicateOfId { get; set; }

    public virtual Vulnerability? Vulnerability { get; set; }

    public virtual User? User { get; set; }

    public virtual RiskAcceptance? RiskAcceptance { get; set; }
}
