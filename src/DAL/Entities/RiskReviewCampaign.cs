using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One periodic business review of an entity's risks (Track 8 milestone 8.6.3).
///
/// This is the artifact that answers the ISO 27001 / DORA question "show me that the business
/// reviewed its risks last quarter, and what it decided" — generated on a cadence rather than
/// created by hand, because a review that depends on somebody remembering to start it is the review
/// that does not happen.
/// </summary>
public class RiskReviewCampaign : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    public string Name { get; set; } = null!;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime DueDate { get; set; }

    public RiskReviewCampaignStatus Status { get; set; } = RiskReviewCampaignStatus.Open;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The last pre-due/overdue reminder already sent, in days from the due date (negative once
    /// overdue). Keeps the daily notification job idempotent across re-runs.
    /// </summary>
    public int? LastNotifiedDaysBefore { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual ICollection<RiskReviewCampaignItem> Items { get; set; } = new List<RiskReviewCampaignItem>();

    int? DAL.Interfaces.IEntityScoped.EntityId
    {
        get => EntityId == 0 ? null : EntityId;
        set => EntityId = value ?? 0;
    }
}
