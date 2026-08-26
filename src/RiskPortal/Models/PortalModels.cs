using DAL.Entities;
using DAL.Enums;
using Model.Governance;

namespace RiskPortal.Models;

/// <summary>
/// One campaign as the dashboard shows it: what it covers, when it is due, and how far through it is.
/// </summary>
public class CampaignSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime DueDate { get; set; }

    public RiskReviewCampaignStatus Status { get; set; }

    public int TotalItems { get; set; }

    public int DecidedItems { get; set; }

    /// <summary>Whole percent, so the progress bar needs no formatting in the view.</summary>
    public int ProgressPercent => TotalItems == 0 ? 0 : DecidedItems * 100 / TotalItems;

    public bool IsOverdue => Status == RiskReviewCampaignStatus.Overdue || DueDate.Date < DateTime.UtcNow.Date;

    public int DaysRemaining => (int)Math.Ceiling((DueDate.Date - DateTime.UtcNow.Date).TotalDays);
}

/// <summary>
/// One risk in a campaign, with everything a business reviewer needs to decide it without opening
/// the desktop client.
/// </summary>
public class ReviewItem
{
    public int ItemId { get; set; }

    public int RiskId { get; set; }

    public int? Rank { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string ReferenceId { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;

    public float? Inherent { get; set; }

    public float? Residual { get; set; }

    /// <summary>Inherent minus residual — how much the treatment is claimed to have bought.</summary>
    public float? Delta => Inherent is null || Residual is null ? null : Inherent - Residual;

    public RiskReviewDecision Decision { get; set; }

    public string? DecisionNotes { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>What the appetite in force says about accepting this risk, and why.</summary>
    public AppetiteEvaluation? Appetite { get; set; }

    /// <summary>Expiry of the acceptance already in force, if any.</summary>
    public DateTime? AcceptedUntil { get; set; }

    public List<CampaignReviewTask> Tasks { get; set; } = [];

    public bool IsDecided => Decision != RiskReviewDecision.Pending;

    /// <summary>
    /// The band label for the score the appetite is measured against. Rendered as a class name, so
    /// the styling stays in the stylesheet rather than in the markup.
    /// </summary>
    public string SeverityBand => (Residual ?? Inherent) switch
    {
        null => "unscored",
        >= 8 => "critical",
        >= 6 => "high",
        >= 3 => "medium",
        _ => "low"
    };
}

/// <summary>The whole review screen: the campaign, its items, and who is signed in.</summary>
public class CampaignDetail
{
    public CampaignSummary Campaign { get; set; } = new();

    public List<ReviewItem> Items { get; set; } = [];

    public bool IsComplete => Items.Count > 0 && Items.All(i => i.IsDecided);
}

/// <summary>What the sign-in page needs to say when the portal is not yet approved.</summary>
public class PortalRegistrationState
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>True once an administrator has approved this portal in the desktop app.</summary>
    public bool Approved { get; set; }

    /// <summary>Null when the API answered; a message when it could not be reached.</summary>
    public string? Problem { get; set; }
}
