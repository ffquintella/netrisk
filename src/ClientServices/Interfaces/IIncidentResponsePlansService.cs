using DAL.Entities;
using Model.DTO;

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
    /// Get all tasks for an incident response plan
    /// </summary>
    /// <param name="planId"></param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanTask>> GetTasksByPlanIdAsync(int planId);
    
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
    
    /// <summary>
    /// Get all executions
    /// </summary>
    /// <param name="planId">The Id of the plan of the executions</param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanExecution>> GetExecutionsByPlanIdAsync(int planId);
    
    /// <summary>
    /// Get an execution by its id
    /// </summary>
    /// <param name="planId"> The Id of the plan of the execution</param>
    /// <param name="executionId"> The execution Id</param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> GetExecutionByIdAsync(int planId, int executionId);
    
    /// <summary>
    /// Create a new execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanExecution"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> CreateExecutionAsync(IncidentResponsePlanExecution incidentResponsePlanExecution);
    
    /// <summary>
    /// Creates a new Incident Response Plan Task Execution
    /// </summary>
    /// <param name="planId"> The plan id</param>
    /// <param name="incidentResponsePlanTaskExecution"> the task to be created</param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> CreateTaskExecutionAsync(int planId, IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution);
    
    /// <summary>
    /// Update an existing execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanExecution">The plan execution to be updated</param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> UpdateExecutionAsync(IncidentResponsePlanExecution incidentResponsePlanExecution);
    
    /// <summary>
    /// Update an existing task execution for an incident response plan
    /// </summary>
    /// <param name="planId">The plan Id</param>
    /// <param name="incidentResponsePlanTaskExecution">The task execution to be updated</param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> UpdateTaskExecutionAsync(int planId, IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution);
    
    /// <summary>
    /// Deletes an incident response plan execution
    /// </summary>
    /// <param name="planId"> The Id of the plan the execution belongs</param>
    /// <param name="incidentResponsePlanExecutionId">the it of the execution</param>
    /// <returns></returns>
    public Task DeleteExecutionAsync(int planId, int incidentResponsePlanExecutionId);
    
    
    /// <summary>
    /// Deletes an incident response plan task execution
    /// </summary>
    /// <param name="planId"> The Id of the plan the execution belongs</param>
    /// <param name="taskId"> The Id of the task the execution belongs</param>
    /// <param name="incidentResponsePlanTaskExecutionId"></param>
    /// <returns></returns>
    public Task DeleteTaskExecutionAsync(int planId, int taskId, int incidentResponsePlanTaskExecutionId);
    
    /// <summary>
    /// Get all attachments for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanId"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetAttachmentsAsync(int incidentResponsePlanId);
}