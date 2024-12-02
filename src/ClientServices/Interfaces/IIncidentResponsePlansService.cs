using DAL.Entities;

namespace ClientServices.Interfaces;

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
    /// <returns></returns>
    public Task<IncidentResponsePlan> CreateAsync(IncidentResponsePlan incidentResponsePlan);
    
    /// <summary>
    /// Update an existing incidentResponsePlan
    /// </summary>
    /// <param name="incidentResponsePlan">The object with the data to be updated</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> UpdateAsync(IncidentResponsePlan incidentResponsePlan);
    
    
    /// <summary>
    /// Delete an incidentResponsePlan
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task DeleteAsync(int id);
    
    
    /// <summary>
    /// Get an incidentResponsePlan by its id
    /// </summary>
    /// <param name="id">Plan Id</param>
    /// <param name="includeTasks">Include Plan Tasks</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false);
    
    /// <summary>
    /// Create a new task for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanTask"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTask> CreateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask);
    
    /// <summary>
    /// Update an existing task for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanTask"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTask> UpdateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask);
}