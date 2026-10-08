using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// An event that obliges reassessing risks — one of the six mandatory triggers of MIGR-TI/IA Phase 7 (Stage 9.8, S49
/// §4.5). Declared by a person with the risks it applies to, or detected when a KRI opens a breach episode.
///
/// A KRI breach episode is one row: opened by the first reading of an unbroken run of readings beyond the tolerance
/// (<see cref="KriReadingId"/>, unique) and ended when the KRI is seen back within it
/// (<see cref="KriBreachEndedAt"/>). Re-evaluating a KRI that stays breached reuses the open episode — that, and the
/// unique indexes, is what makes the trigger idempotent (S49 D8). An incident has at most one event (unique
/// <see cref="IncidentId"/>).
/// </summary>
public class ReassessmentEvent
{
    public int Id { get; set; }

    public ReassessmentTriggerType TriggerType { get; set; }

    public ReassessmentEventOrigin Origin { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>UTC. When the fact happened; for a KRI, when the reading that opened the episode was observed.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>The incident or near miss, on a <see cref="ReassessmentTriggerType.SignificantIncidentOrNearMiss"/> event only.</summary>
    public int? IncidentId { get; set; }

    /// <summary>The KRI, on a <see cref="ReassessmentEventOrigin.KriBreach"/> event only.</summary>
    public int? KriId { get; set; }

    /// <summary>The reading that opened the breach episode.</summary>
    public int? KriReadingId { get; set; }

    /// <summary>UTC. When the KRI was seen back within its tolerance; null while the episode is open.</summary>
    public DateTime? KriBreachEndedAt { get; set; }

    public int? DeclaredById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual Incident? Incident { get; set; }

    public virtual Kri? Kri { get; set; }

    public virtual KriReading? KriReading { get; set; }

    public virtual User? DeclaredBy { get; set; }
}
