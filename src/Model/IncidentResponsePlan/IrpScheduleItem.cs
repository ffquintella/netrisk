using System;
using System.Collections.Generic;

namespace Model.IncidentResponsePlan;

/// <summary>
/// One bar on the incident-response Gantt (Track 2 milestone 2.4.3): a plan task placed on the
/// timeline with its critical-path verdict.
/// </summary>
public class IrpScheduleItem
{
    public int TaskId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Plan-relative stage the task belongs to (the task's <c>ExecutionOrder</c>).</summary>
    public int ExecutionOrder { get; set; }

    public int Status { get; set; }

    public int AssignedToId { get; set; }

    /// <summary>Ids this task waits on, derived from the plan's stage ordering.</summary>
    public List<int> DependsOn { get; set; } = new();

    /// <summary>Task duration used for the pass. Zero-duration tasks are milestones.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Offset from plan start at which the task can begin.</summary>
    public TimeSpan EarlyStart { get; set; }

    public TimeSpan EarlyFinish { get; set; }

    /// <summary>Latest the task can begin without pushing the plan's end out.</summary>
    public TimeSpan LateStart { get; set; }

    public TimeSpan LateFinish { get; set; }

    /// <summary>How long the task may slip before it becomes critical.</summary>
    public TimeSpan Slack { get; set; }

    /// <summary>True when <see cref="Slack"/> is zero — the task is on the critical path.</summary>
    public bool IsCritical { get; set; }

    /// <summary>Wall-clock start, once the plan's anchor date is applied.</summary>
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    /// <summary>
    /// True when the task is still open and its wall-clock end has already passed. Drawn in the
    /// standard alert colour.
    /// </summary>
    public bool IsOverdue { get; set; }

    /// <summary>
    /// True when at least one task this one depends on is not complete. The GUI refuses to
    /// complete a blocked task without an explicit, recorded override.
    /// </summary>
    public bool IsBlocked { get; set; }
}
