using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A key risk indicator (Stage 9.8, S49 §4.1): what it measures, where its readings come from, the Phase 0 tolerance
/// with the decision that set it, which way it gets worse and how old its latest reading may be before it reads stale.
///
/// The tolerance belongs to the indicator, not to an appetite row (S49 D1); an entity-specific indicator is a KRI of
/// that entity. A KRI is retired, never deleted (D14): its readings are the evidence Gate B decided on. Entity-scoped:
/// <c>entity_id</c> null is an organization-wide indicator, visible to every reader of the register and writable only
/// by an unrestricted caller (the write guard of <c>AuditableContext</c>).
/// </summary>
public class Kri : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>What it measures and how — the formula.</summary>
    public string? Description { get; set; }

    public KriCategory Category { get; set; }

    /// <summary>Where a reading comes from: a system, a report, a person.</summary>
    public string Source { get; set; } = null!;

    public string Unit { get; set; } = null!;

    public KriDirection Direction { get; set; }

    /// <summary>The Phase 0 limit. Exceeded strictly beyond it in <see cref="Direction"/>.</summary>
    public decimal ToleranceThreshold { get; set; }

    /// <summary>An earlier warning, on the good side of the tolerance. Informative; never gates.</summary>
    public decimal? WarningThreshold { get; set; }

    /// <summary>The decision that set the tolerance (minutes, approval reference).</summary>
    public string ToleranceRationale { get; set; } = null!;

    /// <summary>How many days the latest reading stays current. Older, the KRI reads stale — never within tolerance.</summary>
    public int MaxReadingAgeDays { get; set; }

    public int? OwnerId { get; set; }

    /// <summary>The business entity the indicator belongs to; null for the organization.</summary>
    public int? EntityId { get; set; }

    /// <summary>UTC. A retired KRI is not evaluated, gates nothing and takes no reading.</summary>
    public DateTime? RetiredAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual User? Owner { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? UpdatedBy { get; set; }
}
