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
    /// Delete an incidentResponsePlan
    /// </summary>
    /// <param name="id"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task DeleteAsync(int id, User user);
    
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
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTask> CreateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask, User user);
    
    
    /// <summary>
    /// Update an existing task for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanTask"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task UpdateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask, User user);

    /// <summary>
    /// Gets a task by its id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTask> GetTaskByIdAsync(int id);
    
    /// <summary>
    /// Deletes an incident response plan task
    /// </summary>
    /// <param name="taskId"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task DeleteTaskAsync(int taskId);
    
    
    /// <summary>
    /// Get all tasks for a plan
    /// </summary>
    /// <param name="planId"></param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanTask>> GetTasksByPlanIdAsync(int planId);
    
    /// <summary>
    /// Get all executions
    /// </summary>
    /// <param name="planId"></param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanExecution>> GetExecutionsByPlanIdAsync(int planId);


    /// <summary>
    /// Create a new execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanExecution"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> CreateExecutionAsync(IncidentResponsePlanExecution incidentResponsePlanExecution, User user);
    
}