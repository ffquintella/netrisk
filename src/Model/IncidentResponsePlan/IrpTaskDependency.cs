using System;

namespace Model.IncidentResponsePlan;

/// <summary>
/// An explicit "waits on" edge between two tasks of an incident response plan
/// (Track 2 milestone 2.4.3).
/// </summary>
public class IrpTaskDependency
{
    public int Id { get; set; }

    /// <summary>The task that waits.</summary>
    public int TaskId { get; set; }

    public string TaskName { get; set; } = string.Empty;

    /// <summary>The task that must finish first.</summary>
    public int DependsOnTaskId { get; set; }

    public string DependsOnTaskName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
