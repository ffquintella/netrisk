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
    
    /// <summary>
    /// Update an existing incidentResponsePlan
    /// </summary>
    /// <param name="incidentResponsePlan">The object with the data to be updated</param>
    /// <param name="user">The user updating the plan</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> UpdateAsync(IncidentResponsePlan incidentResponsePlan, User user);
    
    
    /// <summary>
    /// Get an incidentResponsePlan by its id
    /// </summary>
    /// <param name="id">Plan Id</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> GetByIdAsync(int id);
    
    /// <summary>
    /// Delete an incidentResponsePlan
    /// </summary>
    /// <param name="id"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task DeleteAsync(int id, User user);
}