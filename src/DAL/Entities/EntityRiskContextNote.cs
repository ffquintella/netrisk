using System;

namespace DAL.Entities;

/// <summary>
/// The justification of one risk-context batch for one entity (S40 §6.7, §8.5; M55, T264): one row per entity the
/// batch changed, written in the same save as the property rows. <see cref="BatchId"/> is that save's
/// <c>audit_logs.correlation_id</c> (<c>AuditableContext.AuditCorrelationId</c>), which is how the history joins an
/// audit row to its reason. Insert-only; not entity-scoped (the map has no scope filter, the batch needs global scope).
/// </summary>
public class EntityRiskContextNote
{
    public int Id { get; set; }

    /// <summary>The batch, equal to the <c>correlation_id</c> of the audit rows the batch's save wrote (32 hex characters).</summary>
    public string BatchId { get; set; } = null!;

    /// <summary>The entity whose context changed. A deleted entity takes its notes (CASCADE).</summary>
    public int EntityId { get; set; }

    /// <summary>Why, as the author wrote it, trimmed: 20 to 2000 characters (<c>EntityRiskContextRules</c>).</summary>
    public string Justification { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The author. Null once the user is deleted (SET NULL: the evidence stays).</summary>
    public int? CreatedById { get; set; }

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
