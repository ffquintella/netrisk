﻿using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Model;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("incident-response-plans")]
[ApiController]
[Route("[controller]")]
public class IncidentResponsePlansController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIncidentResponsePlansService incidentResponsePlansService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = incidentResponsePlansService;

    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlan>>> GetAllAsync()
    {

        var user = await GetUserAsync();

        try
        {
            var irps = await IncidentResponsePlansService.GetAllAsync();
            Logger.Information("User:{User} listed all incidentResponsePlans", user.Value);
            return Ok(irps);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing incidentResponsePlans: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlan>> GetByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var irp = await IncidentResponsePlansService.GetByIdAsync(id);
            Logger.Information("User:{User} got incident response plan {id}", user.Value, id);
            return Ok(irp);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Data not found  while getting got incident response plan {id}: {Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id}: {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Tasks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanTask>>> GetTasksByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var irp = await IncidentResponsePlansService.GetByIdAsync(id, true);
            Logger.Information("User:{User} got incident response plan {id} tasks", user.Value, id);
            return Ok(irp.Tasks);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Data not found  while getting got incident response plan tasks {id}: {Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id} tasks: {Message}",  id,ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{planId}/Tasks/{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlanTask))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTask>> GetTaskByIdAsync(int planId, int taskId)
    {

        var user = await GetUserAsync();

        try
        {
            var irpt = await IncidentResponsePlansService.GetTaskByIdAsync(taskId);

            if (irpt.PlanId != planId)
            {
                Log.Warning("Task do not belong to plan");
                return BadRequest("Task do not belong to plan");
            }
            
            Logger.Information("User:{User} got incident response plan {id} task {taskId}",  user.Value, planId,  taskId);
            return Ok(irpt);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Data not found  while getting got incident response plan task {id}: {Message}", taskId, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id} tasks: {Message}",  planId,ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("{id}/Tasks")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTask>> CreateTasksAsync(int id, [FromBody] IncidentResponsePlanTask task)
    {

        var user = await GetUserAsync();
        task.PlanId = id;
        
        try
        {
            var irpt = await IncidentResponsePlansService.CreateTaskAsync(task, user);
            Logger.Information("User:{User} create an incident response task with id:{id}", user.Value, irpt.Id);
            return Created($"{id}/Tasks/{irpt.Id}",irpt);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Data not found  while getting got incident response plan {id}: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while create an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}/Tasks/{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlanTask))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTask>> UpdateTaskAsync(int id, int taskId, [FromBody] IncidentResponsePlanTask task)
    {

        var user = await GetUserAsync();
        task.PlanId = id;
        task.Id = taskId;
        
        try
        {
            await IncidentResponsePlansService.UpdateTaskAsync(task, user);
            
            var updatedTask = await IncidentResponsePlansService.GetTaskByIdAsync(taskId);
            
            Logger.Information("User:{User} updated an incident response plan task id:{id}", user.Value, taskId);
            return Ok(updatedTask);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Data not found  while updating an incident response task {id}: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [Route("{id}/Tasks/{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteTaskAsync(int id, int taskId)
    {

        var user = await GetUserAsync();
        
        try
        {
            await IncidentResponsePlansService.DeleteTaskAsync(taskId);
            
            Logger.Information("User:{User} delete a incident response plan {planId} task:{id}", user.Value, id, taskId);
            return Ok();
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while deleting an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Tasks/{taskId}/Executions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanTaskExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanTaskExecution>>> GetTaskExecutionsAsync(int id, int taskId)
    {

        var user = await GetUserAsync();
        
        try
        {
            var result = await IncidentResponsePlansService.GetTaskExecutionsByIdAsync(taskId);
            
            Logger.Information("User:{User} delete a incident response plan {planId} task:{id}", user.Value, id, taskId);
            return Ok(result);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while deleting an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting an incident response task: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{planId}/Executions/{executionId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanExecution>>> GetExecutionByIdAsync(int planId,  int executionId)
    {

        var user = await GetUserAsync();
        
        try
        {
            var result = await IncidentResponsePlansService.GetExecutionByIdAsync(executionId);
            
            Logger.Information("User:{User} got an incident response plan: {planId} execution: {executionId}", user.Value, planId, executionId);
            return Ok(result);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while getting an incident response plan execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error getting an incident response plan execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("{id}/Tasks/{taskId}/Executions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTaskExecution>> CreateTaskExecutionsAsync(int id, int taskId, [FromBody] IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution)
    {

        var user = await GetUserAsync();
        
        incidentResponsePlanTaskExecution.TaskId = taskId;

        var planExecutions = await IncidentResponsePlansService.GetExecutionsByPlanIdAsync(id);

        var execution = planExecutions.FirstOrDefault(pe => pe.Status == (int)IntStatus.Active && pe.Id == incidentResponsePlanTaskExecution.PlanExecutionId);

        if (execution == null)
        {
            // An active execution must exists
            return this.StatusCode(StatusCodes.Status400BadRequest, "An active execution must be first created");
        }
        
        try
        {
            var result =
                await IncidentResponsePlansService.CreateTaskExecutionAsync(incidentResponsePlanTaskExecution, user);
            
            Logger.Information("User:{User} created a incident response plan {planId} task:{id} execution:{exeId}", user.Value, id, taskId, result.Id);
            return Ok(result);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while creating an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}/Tasks/{taskId}/Executions/{executionId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTaskExecution>> UpdateTaskExecutionsAsync(int id, int taskId, int executionId, [FromBody] IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution)
    {

        var user = await GetUserAsync();
        
        incidentResponsePlanTaskExecution.TaskId = taskId;
        incidentResponsePlanTaskExecution.Id = executionId;

        var planExecutions = await IncidentResponsePlansService.GetExecutionsByPlanIdAsync(id);

        var execution = planExecutions.FirstOrDefault(pe => pe.Status == (int)IntStatus.Active && pe.Id == incidentResponsePlanTaskExecution.PlanExecutionId);

        if (execution == null)
        {
            // An active execution must exists
            return this.StatusCode(StatusCodes.Status400BadRequest, "An active execution must be first created");
        }
        
        try
        {
            var result =
                await IncidentResponsePlansService.UpdateTaskExecutionAsync(incidentResponsePlanTaskExecution, user);
            
            Logger.Information("User:{User} updated a incident response plan {planId} task:{id} execution:{execId}", user.Value, id, taskId, executionId);
            return Ok(result);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while updating an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [Route("{id}/Tasks/{taskId}/Executions/{executionId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteTaskExecutionsAsync(int id, int taskId, int executionId)
    {

        var user = await GetUserAsync();
        
        
        try
        {
            
            await IncidentResponsePlansService.DeleteTaskExecutionAsync(executionId);
            
            Logger.Information("User:{User} deleted a incident response plan {planId} task:{id} execution:{execId}", user.Value, id, taskId, executionId);
            return Ok();
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while deleting an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Tasks/{taskId}/Executions/{executionId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanTaskExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int id, int taskId, int executionId)
    {

        var user = await GetUserAsync();
        
        try
        {
            
            var result = await IncidentResponsePlansService.GetTaskExecutionsByIdAsync(executionId);
            
            Logger.Information("User:{User} got a incident response plan {planId} task:{id} execution:{execId}", user.Value, id, taskId, executionId);
            return Ok(result);
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while getting an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting an incident response task execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlan>> CreateAsync([FromBody] IncidentResponsePlan incidentResponsePlan)
    {
        var user = await GetUserAsync();

        try
        {
            var result = await IncidentResponsePlansService.CreateAsync(incidentResponsePlan, user);
            Logger.Information("User:{User} created incidentResponsePlan:{IncidentResponsePlan}", user.Value, result.Id);
            return Created($"IncidentResponsePlan/{result.Id}", result);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlan>> UpdateAsync(int id, [FromBody] IncidentResponsePlan incidentResponsePlan)
    {
        var user = await GetUserAsync();

        incidentResponsePlan.Id = id;
        
        try
        {
            var result = await IncidentResponsePlansService.UpdateAsync(incidentResponsePlan, user);
            Logger.Information("User:{User} updated incidentResponsePlan:{IncidentResponsePlan}", user.Value, result.Id);
            return Ok(result);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteAsync(int id)
    {
        var user = await GetUserAsync();
        
        try
        {
            await IncidentResponsePlansService.DeleteAsync(id, user);
            //Logger.Information("User:{User} deleted incidentResponsePlan:{id}", user.Value, id);
            return Ok();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
    [HttpGet]
    [Route("{id}/Executions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanExecution>>> GetExecutionsByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var irpe = await IncidentResponsePlansService.GetExecutionsByPlanIdAsync(id);
            Logger.Information("User:{User} got incident response plan {id} executions", user.Value, id);
            return Ok(irpe);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id} executions: {Message}",  id,ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("{id}/Executions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanExecution>>> CreatePlanExecutionAsync(int id, [FromBody] IncidentResponsePlanExecution execution)
    {

        var user = await GetUserAsync();
        execution.PlanId = id;
        
        try
        {
            var irpe = await IncidentResponsePlansService.CreateExecutionAsync(execution, user);
            Logger.Information("User:{User} created an incident response plan execution with id:{id}", user.Value, irpe.Id);
            return Ok(irpe);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while create an incident response execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}/Executions/{execId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanExecution>>> UpdatePlanExecutionAsync(int id, int execId, [FromBody] IncidentResponsePlanExecution execution)
    {

        var user = await GetUserAsync();
        execution.PlanId = id;
        execution.Id = execId;

        try
        {
            var irpe = await IncidentResponsePlansService.UpdateExecutionAsync(execution, user);
            Logger.Information("User:{User} updated an incident response plan execution with id:{id}", user.Value,
                irpe.Id);
            return Ok(irpe);
        }

        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while updating an incident response execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating an incident response execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }


    [HttpDelete]
    [Route("{id}/Executions/{execId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlanExecution>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanExecution>>> DeletePlanExecutionAsync(int id, int execId)
    {

        var user = await GetUserAsync();

        try
        {
            await IncidentResponsePlansService.DeleteExecutionAsync(execId);
            Logger.Information("User:{User} deleted an incident response plan execution with id:{id}", user.Value, execId);
            return Ok();
        }

        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error while deleting an incident response execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting an incident response execution: {Message}",  ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }


}