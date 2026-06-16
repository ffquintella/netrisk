using System.Threading.Tasks;
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IIrpAutomationService
{
    /// <summary>
    /// Evaluates active IRP templates against the new incident's properties,
    /// generating and assigning the correct response plan and tasks automatically.
    /// </summary>
    /// <param name="incident">The newly created incident.</param>
    Task TriggerForIncidentAsync(Incident incident);
}
