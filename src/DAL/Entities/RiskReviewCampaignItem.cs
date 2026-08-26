using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One risk inside a review campaign, with the reviewer's business ranking and decision
/// (Track 8 milestone 8.6.3/8.6.4).
///
/// <see cref="Rank"/> is the part that has no equivalent anywhere else in NetRisk: the technical
/// score says how bad a risk is, and the rank says how much the business cares. Both are needed to
/// prioritize, and until now only the first existed.
/// </summary>
public class RiskReviewCampaignItem
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public int RiskId { get; set; }

    /// <summary>Business priority within the campaign, 1 = highest. Null until the reviewer ranks.</summary>
    public int? Rank { get; set; }

    public RiskReviewDecision Decision { get; set; } = RiskReviewDecision.Pending;

    public string? DecisionNotes { get; set; }

    public int? DecidedById { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>The acceptance an <c>Accepted</c> decision produced, when it produced one.</summary>
    public int? RiskAcceptanceId { get; set; }

    /// <summary>The senior approver an <c>Escalated</c> item was routed to.</summary>
    public int? EscalatedToId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RiskReviewCampaign? Campaign { get; set; }

    public virtual Risk? Risk { get; set; }

    public virtual User? DecidedBy { get; set; }

    public virtual User? EscalatedTo { get; set; }

    public virtual RiskAcceptance? RiskAcceptance { get; set; }
}
