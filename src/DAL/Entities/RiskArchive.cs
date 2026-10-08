using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The Phase 4 "archive" decision on a risk (Stage 9.9, S50 §4.1): a justification, the conditions that reopen it and a
/// quarterly review. Archiving closes the risk through a <see cref="Closure"/> of its own (<see cref="ClosureId"/>), so
/// every list, job and gate that skips closed risks skips an archived one unchanged; the archive adds what a closure
/// lacks — a reason recorded as a decision, a trigger that brings the risk back, and a date by which somebody must look.
///
/// Live (computed on read, S50 D1) = <see cref="Status"/> is <see cref="RiskArchiveStatus.Archived"/>, the risk is
/// closed, and its closure is still the one this archive made. Reopened once, never reopened again (S50 D4): a further
/// condition reaches an open risk, which the Stage 9.8 trigger already flags for review.
/// </summary>
public class RiskArchive
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    /// <summary>The closure the archive made; cleared when the closure is removed.</summary>
    public int? ClosureId { get; set; }

    public RiskArchiveStatus Status { get; set; } = RiskArchiveStatus.Archived;

    /// <summary>Why the risk is not worth treating — the auditor-facing field.</summary>
    public string Justification { get; set; } = null!;

    /// <summary>The register status before archiving, restored when the archive is reopened.</summary>
    public string PreviousStatus { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime ArchivedAt { get; set; }

    public int? ArchivedById { get; set; }

    /// <summary>UTC. A quarter after archiving, then a quarter after each review that keeps it.</summary>
    public DateTime NextReviewDueAt { get; set; }

    /// <summary>UTC. The last quarterly review that kept it archived.</summary>
    public DateTime? LastReviewedAt { get; set; }

    /// <summary>UTC. When the due notice for <see cref="NextReviewDueAt"/> went out — once per due date.</summary>
    public DateTime? ReviewNotifiedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ReopenedAt { get; set; }

    public RiskArchiveReopenOrigin? ReopenOrigin { get; set; }

    /// <summary>Who reopened it; null when a condition did.</summary>
    public int? ReopenedById { get; set; }

    public string? ReopenReason { get; set; }

    /// <summary>The reassessment event whose trigger reopened it (<see cref="RiskArchiveReopenOrigin.Condition"/>).</summary>
    public int? ReopenEventId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual Closure? Closure { get; set; }

    public virtual User? ArchivedBy { get; set; }

    public virtual User? ReopenedBy { get; set; }

    public virtual ReassessmentEvent? ReopenEvent { get; set; }

    public virtual ICollection<RiskArchiveCondition> Conditions { get; set; } = new List<RiskArchiveCondition>();

    public virtual ICollection<RiskArchiveReview> Reviews { get; set; } = new List<RiskArchiveReview>();
}

/// <summary>
/// A condition that reopens an archive (S50 §4.1): one of the six Phase 7 reassessment triggers of Stage 9.8. The
/// archive is reopened, once, by the first event of a watched type that reaches the risk — declared naming it, or a
/// breach of a KRI linked to it (S50 D3). One row per archive and type.
/// </summary>
public class RiskArchiveCondition
{
    public int Id { get; set; }

    public int ArchiveId { get; set; }

    public ReassessmentTriggerType TriggerType { get; set; }

    /// <summary>What, specifically, would bring the risk back ("the supplier changes", "the LGPD rule is revised").</summary>
    public string? Description { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskArchive Archive { get; set; } = null!;
}

/// <summary>
/// One quarterly review of an archive (S50 §4.2): kept, with the next review a quarter away, or reopened. Insert-only.
/// </summary>
public class RiskArchiveReview
{
    public int Id { get; set; }

    public int ArchiveId { get; set; }

    public RiskArchiveReviewOutcome Outcome { get; set; }

    public string Note { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime ReviewedAt { get; set; }

    public int? ReviewedById { get; set; }

    /// <summary>UTC. The next due date this review set, when it kept the archive.</summary>
    public DateTime? NextReviewDueAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskArchive Archive { get; set; } = null!;

    public virtual User? ReviewedBy { get; set; }
}
