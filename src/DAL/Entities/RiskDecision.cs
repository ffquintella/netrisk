using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One Phase 4 decision on a risk (Stage 9.5, S46 §4.3): act immediately, treat within the cycle,
/// monitor/accept or archive. Insert-only; the decision in force is the latest.
///
/// A classification record, not an action: "monitor/accept" creates no acceptance and "archive" closes
/// nothing — those keep their own paths and controls. What it adds is the "act immediately" state the
/// register lacked (T171): an escalation that is recorded and notified, and that no severity band sets.
/// </summary>
public class RiskDecision
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public RiskDecisionKind Decision { get; set; }

    public RiskDecisionSource Source { get; set; }

    /// <summary>The written reason; for a Gate A decision, the conditions and their bases.</summary>
    public string Reason { get; set; } = null!;

    /// <summary>The Gate A condition codes in force when the decision was recorded, comma-separated ("1,3").</summary>
    public string? GateAConditions { get; set; }

    /// <summary>UTC.</summary>
    public DateTime DecidedAt { get; set; }

    /// <summary>Null when the system recorded it.</summary>
    public int? DecidedById { get; set; }

    /// <summary>UTC. When the escalation event was raised — act-immediately decisions only.</summary>
    public DateTime? EscalatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? DecidedBy { get; set; }
}
