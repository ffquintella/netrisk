﻿using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IIncidentResponsePlansService
{
    /// <summary>
    /// Get all incidentResponsePlans
    /// </summary>
    /// <returns></returns>
    public Task<List<IncidentResponsePlan>> GetAllAsync();
    
    /// <summary>
    /// Get all approved incidentResponsePlans
    /// </summary>
    /// <returns></returns>
    public Task<List<IncidentResponsePlan>> GetAllApprovedAsync();
    
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
    /// <param name="includeActivatedBy">Include Plan ActivatedBy Filed</param>
    /// <returns></returns>
    public Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false, bool includeActivatedBy = false);


    /// <summary>
    /// Get the most recent incident related to a task
    /// </summary>
    /// <param name="taskId"></param>
    /// <returns></returns>
    public Task<Incident> GetIncidentByTaskIdAsync(int taskId);
    
    
    /// <summary>
    /// Change the status of a task execution
    /// </summary>
    /// <param name="taskId"></param>
    /// <param name="status"></param>
    /// <returns></returns>
    public Task ChangeExecutionTaskSatusByIdAsync(int taskId, int status);
    
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
    /// Get all task executions for a task
    /// </summary>
    /// <param name="taskId">The Id of the task</param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int taskId);
    
    /// <summary>
    /// Get all executions
    /// </summary>
    /// <param name="planId">The Id of the plan of the executions</param>
    /// <returns></returns>
    public Task<List<IncidentResponsePlanExecution>> GetExecutionsByPlanIdAsync(int planId);
    
    /// <summary>
    /// Get an execution by its id
    /// </summary>
    /// <param name="executionId"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> GetExecutionByIdAsync(int executionId);
    
    /// <summary>
    /// Gets a task execution by Id
    /// </summary>
    /// <param name="taskExecutionId">A task execution id</param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> GetTaskExecutionByIdAsync(int taskExecutionId);

    /// <summary>
    /// Create a new execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanExecution"></param>
    /// <param name="user"/>
    /// <param name="incident"/>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> CreateExecutionAsync(IncidentResponsePlanExecution incidentResponsePlanExecution, Incident incident, User user);
    
    
    /// <summary>
    /// Creates a new Incident Response Plan Task Execution
    /// </summary>
    /// <param name="incidentResponsePlanTaskExecution"></param>
    /// <param name="user"></param>
    /// <param name="incident"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> CreateTaskExecutionAsync(IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution, Incident incident, User user);
    
    /// <summary>
    /// Update an existing execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanExecution"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanExecution> UpdateExecutionAsync(IncidentResponsePlanExecution incidentResponsePlanExecution, User user);
    
    /// <summary>
    /// Update an existing task execution for an incident response plan
    /// </summary>
    /// <param name="incidentResponsePlanTaskExecution"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlanTaskExecution> UpdateTaskExecutionAsync(IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution, User user);
    
    /// <summary>
    /// Deletes an incident response plan execution
    /// </summary>
    /// <param name="incidentResponsePlanExecutionId"></param>
    /// <returns></returns>
    public Task DeleteExecutionAsync(int incidentResponsePlanExecutionId);
    
    /// <summary>
    /// Deletes an incident response plan task execution
    /// </summary>
    /// <param name="incidentResponsePlanTaskExecutionId"></param>
    /// <returns></returns>
    public Task DeleteTaskExecutionAsync(int incidentResponsePlanTaskExecutionId);
    
}