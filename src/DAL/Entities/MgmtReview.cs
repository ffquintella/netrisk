using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class MgmtReview
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public DateTime SubmissionDate { get; set; }

    public int Review { get; set; }

    public int Reviewer { get; set; }

    public int NextStep { get; set; }

    public string Comments { get; set; } = null!;

    public DateOnly NextReview { get; set; }

    public virtual NextStep NextStepNavigation { get; set; } = null!;

    public virtual Review ReviewNavigation { get; set; } = null!;

    public virtual User ReviewerNavigation { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;

    // --- Track 8 milestone 8.3.4: counter-signature -------------------------------------------

    /// <summary>
    /// Set when the residual score crossed the appetite's dual-approval threshold, in which case
    /// this review does not take effect until a second, distinct approver counter-signs.
    /// </summary>
    public bool RequiresCountersignature { get; set; }

    /// <summary>The second approver. Must differ from <see cref="Reviewer"/> — and from the risk's
    /// submitter, owner and manager, like the first (8.3.2).</summary>
    public int? SecondReviewerId { get; set; }

    public DateTime? SecondReviewAt { get; set; }

    /// <summary>
    /// Recorded when the segregation-of-duties rule was deliberately overridden. A break-glass that
    /// leaves no trace is indistinguishable from a bypass, so the reason is required when used.
    /// </summary>
    public string? SegregationOverrideReason { get; set; }

    public virtual User? SecondReviewer { get; set; }
}
