using System;
using System.Collections.Generic;

namespace Model.IncidentResponsePlan;

/// <summary>
/// Computed timeline for one incident response plan (Track 2 milestone 2.4.3). The critical
/// path is calculated server-side so every client draws the same bars.
/// </summary>
public class IrpSchedule
{
    public int PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Wall-clock anchor the offsets are measured from — the plan's creation date.</summary>
    public DateTime PlanStart { get; set; }

    /// <summary>Anchor plus the longest chain: the earliest the plan can finish.</summary>
    public DateTime PlanEnd { get; set; }

    /// <summary>Total duration of the critical path.</summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>Tasks in execution order, each already placed on the timeline.</summary>
    public List<IrpScheduleItem> Items { get; set; } = new();

    /// <summary>Task ids forming the longest chain, in order.</summary>
    public List<int> CriticalPath { get; set; } = new();
}
