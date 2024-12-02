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
    

    /// <summary>
    /// Gets a task by its id
    /// </summary>
    /// <param name="planId"></param>
    /// <param name="taskId"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTask> GetTaskByIdAsync(int planId, int taskId);
    
    /// <summary>
    /// Deletes an incident response plan task
    /// </summary>
    /// <param name="planId"></param>
    /// <param name="taskId"></param>
    /// <returns></returns>
    public Task DeleteTaskAsync(int planId, int taskId);
    
    /// <summary>
    /// Get all task executions for a task
    /// </summary>
    /// <param name="planId"></param>
    /// <param name="taskId">The Id of the task</param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int planId, int taskId);
    
    /// <summary>
    /// Get an execution by its id
    /// </summary>
    /// <param name="planId"></param>
    /// <param name="taskId">The Id of the task</param>
    /// <param name="executionId"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> GetExecutionByTaskIdAsync(int planId, int taskId, int executionId);
    
}