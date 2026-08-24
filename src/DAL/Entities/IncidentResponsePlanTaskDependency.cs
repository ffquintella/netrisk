using System;

namespace DAL.Entities;

/// <summary>
/// A persisted dependency edge between two tasks of an incident response plan
/// (Track 2 milestone 2.4.3).
///
/// Before this, the Gantt derived its ordering from <c>ExecutionOrder</c> plus the
/// <c>IsSequential</c> flag, which can only express "stage after stage" — the plan author had no
/// way to say that one particular task waits on one other. These rows carry that, and the graph
/// is validated acyclic on save because a cycle makes the plan impossible to schedule.
/// </summary>
public partial class IncidentResponsePlanTaskDependency
{
    public int Id { get; set; }

    /// <summary>The task that waits.</summary>
    public int TaskId { get; set; }

    /// <summary>The task that must finish first.</summary>
    public int DependsOnTaskId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual IncidentResponsePlanTask Task { get; set; } = null!;

    public virtual IncidentResponsePlanTask DependsOnTask { get; set; } = null!;
}
