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
}
