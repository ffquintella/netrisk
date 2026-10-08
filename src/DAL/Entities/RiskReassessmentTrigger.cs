using System;

namespace DAL.Entities;

/// <summary>
/// A reassessment event applied to one risk (Stage 9.8, S49 §4.6). One per event and risk (unique), so the same cause
/// never opens a second reassessment of the same risk (S49 D8).
///
/// Its state is computed on read (S49 D9): answered by the first management review of the risk submitted at or after
/// <see cref="RaisedAt"/>; otherwise pending, or closed with the risk. Creating it also flags the risk for review through
/// the Track 8.5.1 mechanism, so the 07:30 cadence sweep tells the owner and the manager.
/// </summary>
public class RiskReassessmentTrigger
{
    public int Id { get; set; }

    public int EventId { get; set; }

    public int RiskId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime RaisedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual ReassessmentEvent Event { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;
}
