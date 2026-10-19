using System;

namespace DAL.Entities;

/// <summary>
/// The closure date the first snapshot that saw an incident closed read, frozen (S39 §9.2, D-15 interim) —
/// <c>risk_index_incident_closures</c>: later edits to <c>incidents.last_update</c> must not restart the decay of a closed
/// incident. Written by the snapshot job only; dropped when the incident is seen open again (a reopened incident freezes
/// anew when it closes) and by the retention job after 400 days. Superseded by <c>incidents.resolved_at</c> when T279
/// adds it. Follows its incident's scope.
/// </summary>
public class RiskIndexIncidentClosure
{
    /// <summary>The incident. No foreign key: the row is history and the job tolerates a deleted incident.</summary>
    public int IncidentId { get; set; }

    /// <summary>UTC: the closure date the decay is measured from.</summary>
    public DateTime ClosedAt { get; set; }

    /// <summary>The snapshot day that froze it.</summary>
    public DateOnly FrozenOn { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
