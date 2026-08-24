namespace DAL.Entities;

/// <summary>
/// Remediation and triage deadlines per severity (Track 3 milestone 3.4.1).
///
/// Rows are <em>effective-dated</em>: changing the policy inserts a new row rather than editing the
/// old one, so a finding's due date stays derivable from the policy that was in force when it was
/// found. Editing in place would silently rewrite last quarter's compliance figures, which is the
/// one thing an SLA report must never do.
/// </summary>
public class SlaConfiguration
{
    public int Id { get; set; }

    /// <summary>
    /// The severity this applies to, as <c>Contracts.Importers.NormalizedSeverity</c>. Stored as an
    /// int so the table is readable without the enum, per the Track 6 convention.
    /// </summary>
    public int Severity { get; set; }

    /// <summary>Days allowed to triage (Active → Verified or a suppressing verdict).</summary>
    public int MaxTriageDays { get; set; }

    /// <summary>Days allowed to remediate. This is what <c>sla_due_date</c> is computed from.</summary>
    public int MaxRemediationDays { get; set; }

    /// <summary>
    /// Null for the global default; set for an entity-specific override, which composes with the
    /// Track 2.3 multi-entity model.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>When this policy row starts applying.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// When it stopped applying. Null means current. Superseding a policy sets this on the old row
    /// rather than deleting it.
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? CreatedBy { get; set; }
}
