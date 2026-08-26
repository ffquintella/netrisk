using DAL.Entities;
using Model.Governance;

namespace ServerServices.Interfaces;

/// <summary>
/// Periodic business review campaigns and the decisions taken in them
/// (Track 8 milestones 8.6.3–8.6.5).
///
/// This is the service the portal is a thin view over. Every decision it records materializes as a
/// first-class record elsewhere — an 8.1 acceptance, 8.5.3 mitigation tasks, a <c>MgmtReview</c> —
/// so the desktop app and the portal read one approval timeline rather than two.
/// </summary>
public interface IRiskReviewCampaignsService
{
    /// <summary>
    /// Generates the campaigns due as of <paramref name="asOfUtc"/>, one per entity per period.
    /// Idempotent: the unique (entity, period) index means a second run on the same day converges on
    /// the same campaigns instead of creating a new set every morning.
    /// </summary>
    Task<List<RiskReviewCampaign>> GenerateDueCampaignsAsync(DateTime asOfUtc);

    /// <summary>Campaigns visible to a reviewer, newest first.</summary>
    Task<List<RiskReviewCampaign>> GetForReviewerAsync(int userId, bool openOnly = true);

    /// <summary>One campaign with its items, risks and scores — the portal's review screen.</summary>
    Task<RiskReviewCampaign> GetAsync(int campaignId);

    /// <summary>Persists a drag-to-rank ordering, and mirrors it onto <c>risks.business_rank</c>.</summary>
    Task SaveRankingAsync(int campaignId, List<int> orderedItemIds, int actingUserId);

    /// <summary>
    /// Records one decision. Accept creates an acceptance (appetite-gated), Request mitigation
    /// creates task line items, Escalate routes the item to a named senior approver. Every branch
    /// writes a <c>MgmtReview</c>.
    /// </summary>
    Task<RiskReviewCampaignItem> DecideAsync(int campaignId, int itemId, CampaignDecisionRequest request,
        int actingUserId);

    /// <summary>Marks campaigns whose due date has passed as overdue, for the notification job.</summary>
    Task<List<RiskReviewCampaign>> MarkOverdueAsync(DateTime asOfUtc);

    /// <summary>
    /// Overdue campaigns that are due a reminder, with the undecided count and who to chase, marking
    /// each as reminded in the same pass.
    ///
    /// The bookkeeping lives here rather than in the job for the same reason the overdue-review
    /// resolution does: a job that owns state is a job that cannot be tested without a database, and
    /// this one carries the escalation an organization relies on.
    /// </summary>
    Task<List<CampaignReminder>> TakeOverdueRemindersAsync(DateTime asOfUtc, int reminderIntervalDays);

    /// <summary>Completion and decision-mix statistics per campaign (8.6.5).</summary>
    Task<List<CampaignStatistics>> GetStatisticsAsync(int? entityId = null);
}

/// <summary>One overdue campaign that needs chasing, and who to chase.</summary>
public class CampaignReminder
{
    public RiskReviewCampaign Campaign { get; set; } = null!;

    public int PendingItems { get; set; }

    public List<int> ReviewerUserIds { get; set; } = [];
}
