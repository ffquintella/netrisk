using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One line item of a treatment plan (Track 8 milestone 8.5.3) — the POA&amp;M shape NIST RMF
/// documents remediation with, and what ISO auditors mean by "treatment plan with timelines,
/// responsibilities and status".
///
/// <c>Mitigation</c> already had a single <c>PlanningDate</c> and a percentage, which cannot say who
/// is doing what by when. These rows can, and they are what the business portal (8.6) creates when a
/// reviewer asks for mitigation instead of accepting a risk.
/// </summary>
public class MitigationTask
{
    public int Id { get; set; }

    public int MitigationId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>The named person accountable. Nullable only so an unassigned task can be filed.</summary>
    public int? OwnerId { get; set; }

    public DateTime? DueDate { get; set; }

    public MitigationTaskStatus Status { get; set; } = MitigationTaskStatus.Open;

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>
    /// The smallest overdue/pre-due warning threshold already notified, in days. Same idempotence
    /// device as <c>RiskAcceptance.LastWarningDaysBefore</c>: without it the daily job re-sends.
    /// </summary>
    public int? LastNotifiedDaysBefore { get; set; }

    public virtual Mitigation? Mitigation { get; set; }

    public virtual User? Owner { get; set; }

    public virtual User? CreatedBy { get; set; }
}
