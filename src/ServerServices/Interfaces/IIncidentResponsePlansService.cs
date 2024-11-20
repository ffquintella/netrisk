using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IIncidentResponsePlansService
{
    /// <summary>
    /// Get all incidentResponsePlans
    /// </summary>
    /// <returns></returns>
    public Task<List<IncidentResponsePlan>> GetAllAsync();
    
    /// <summary>
    /// Create a new incidentResponsePlan
    /// </summary>
    /// <param name="incidentResponsePlan">A new version of the object to be created</param>
    /// <param name="user">The user creating the plan</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> CreateAsync(IncidentResponsePlan incidentResponsePlan, User user);
}