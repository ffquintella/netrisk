using System.Collections.Generic;
using System.Threading.Tasks;
using Model.IncidentResponsePlan;

namespace ServerServices.Interfaces;

/// <summary>
/// Critical-path scheduling for incident response plans (Track 2 milestone 2.4.3).
/// </summary>
public interface IIrpScheduleService
{
    /// <summary>
    /// Places a plan's tasks on a timeline and marks the critical path.
    /// </summary>
    /// <param name="planId">The plan to schedule.</param>
    /// <returns>The computed schedule, or null when the plan does not exist.</returns>
    Task<IrpSchedule?> GetScheduleAsync(int planId);

    /// <summary>The explicit dependency edges declared on a plan's tasks.</summary>
    Task<List<IrpTaskDependency>> GetDependenciesAsync(int planId);

    /// <summary>
    /// Declares that <paramref name="taskId"/> waits on <paramref name="dependsOnTaskId"/>.
    /// </summary>
    /// <exception cref="Model.Exceptions.RuleBrokenException">
    /// The edge would be self-referential, cross two plans, or close a cycle — any of which makes
    /// the plan impossible to schedule.
    /// </exception>
    Task<IrpTaskDependency> AddDependencyAsync(int planId, int taskId, int dependsOnTaskId);

    /// <summary>Removes an edge. Removing one that does not exist is not an error.</summary>
    Task RemoveDependencyAsync(int planId, int taskId, int dependsOnTaskId);

    /// <summary>
    /// Completes a task whose predecessors are not all done, recording who overrode the block and
    /// why (Track 2 milestone 2.4.3). A task that is not blocked does not need this and is
    /// completed the ordinary way.
    /// </summary>
    /// <exception cref="Model.Exceptions.RuleBrokenException">The reason is missing.</exception>
    Task<IrpScheduleItem> CompleteBlockedTaskAsync(int planId, int taskId, int userId, string reason);
}
