using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class PendingRisk : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    /// <summary>
    /// The assessment that raised this row. NULL for a standalone hypothesis (Stage 9.2, S42 §4.2) —
    /// the column was NOT NULL while an assessment answer was the only way in.
    /// </summary>
    public int? AssessmentId { get; set; }

    /// <summary>The answer that raised this row; NULL for a standalone hypothesis.</summary>
    public int? AssessmentAnswerId { get; set; }

    /// <summary>Assessment-raised or standalone (S42 §4.2). Every row older than the column is <c>Assessment</c>.</summary>
    public Enums.PendingRiskOrigin Origin { get; set; } = Enums.PendingRiskOrigin.Assessment;

    /// <summary>
    /// Who registered a standalone hypothesis. NULL for assessment-raised rows, which carry no author.
    /// A promoted standalone hypothesis becomes a risk submitted by this user, not by the triager
    /// (S42 §5, "the promotion preserves origin and author").
    /// </summary>
    public int? SubmittedById { get; set; }

    public virtual User? SubmittedBy { get; set; }

    /// <summary>
    /// The business entity the hypothesis belongs to (Stage 9.2, S42 §4.2) — the same scope column
    /// risks carry, read by the same query filter and the same write guard. Before it the queue was the
    /// one unscoped register: a hypothesis written in one unit was readable and promotable from every
    /// other. Back-filled from the raising assessment's entity on upgrade; NULL is organization-wide,
    /// visible only to an unrestricted caller, exactly as for a risk.
    /// </summary>
    public int? EntityId { get; set; }

    public virtual Entity? Entity { get; set; }

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
