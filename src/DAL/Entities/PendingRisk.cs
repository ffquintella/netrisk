using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class PendingRisk
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }

    public int AssessmentAnswerId { get; set; }

    public byte[] Subject { get; set; } = null!;

    public float Score { get; set; }

    public int? Owner { get; set; }

    public string? AffectedAssets { get; set; }

    public string Comment { get; set; } = null!;

    public DateTime SubmissionDate { get; set; }

    // --- Track 8 milestone 8.5.2: triage ------------------------------------------------------
    // Before this the table had no state and nothing read it: assessment answers created rows that
    // no code path ever promoted to a risk. The state is what makes the queue drainable.

    public Enums.PendingRiskStatus Status { get; set; } = Enums.PendingRiskStatus.Pending;

    /// <summary>The risk this row became, once promoted. The assessment→register traceability link.</summary>
    public int? PromotedRiskId { get; set; }

    public int? TriagedById { get; set; }

    public DateTime? TriagedAt { get; set; }

    /// <summary>Required when dismissing: a queue drained without reasons is a queue deleted.</summary>
    public string? DismissalReason { get; set; }
}
