using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(IncidentResponsePlansController))]
public class IncidentResponsePlansControllerTest: BaseControllerTest
{

    private readonly IncidentResponsePlansController _incidentResponsePlansController;

    public IncidentResponsePlansControllerTest()
    {
        _incidentResponsePlansController = _serviceProvider.GetRequiredService<IncidentResponsePlansController>();
    }
    
    
    [Fact]
    public async Task TestGetAllAsync()
    {
        
        var result = await _incidentResponsePlansController.GetAllAsync();
        
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);

        var okResult = (OkObjectResult)result.Result;
        
        var resultList = (List<IncidentResponsePlan>)okResult.Value;

        Assert.NotNull(resultList);
        Assert.Equal(2, resultList.Count);

    }

    [Fact]
    public async Task TestGetByIdAsync()
    {
        var result = await _incidentResponsePlansController.GetByIdAsync(1);
        
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);
        
        var okResult = (OkObjectResult)result.Result;
        
        var resultObject = (IncidentResponsePlan)okResult.Value;
        
        Assert.NotNull(resultObject);
        Assert.Equal(1, resultObject.Id);
        Assert.Equal("Teste", resultObject.Name);


        var notFoundResult = await _incidentResponsePlansController.GetByIdAsync(1000);
        
        Assert.NotNull(notFoundResult);
        Assert.IsType<NotFoundResult>(notFoundResult.Result);
        
    }

    [Fact]
    public async Task TestGetTaskByIdAsync()
    {
        var result = await _incidentResponsePlansController.GetTaskByIdAsync(1, 1);
        
        Assert.NotNull(result);
        
        Assert.IsType<BadRequestObjectResult>(result.Result);
        
        result = await _incidentResponsePlansController.GetTaskByIdAsync(2, 1);
        
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);
        
        var okResult = (OkObjectResult)result.Result;
        
        var resultObject = (IncidentResponsePlanTask)okResult.Value;
        
        Assert.NotNull(resultObject);
        Assert.Equal(1, resultObject.Id);
        Assert.Equal(2, resultObject.PlanId);
        Assert.Equal("Task 1", resultObject.Description);
    }

    [Fact]
    public async Task TestGetTasksByIdAsync()
    {
        var result = await _incidentResponsePlansController.GetTasksByIdAsync(2);
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);
        
        var okResult = (OkObjectResult)result.Result;
        
        var resultList = (List<IncidentResponsePlanTask>)okResult.Value;
        
        Assert.NotNull(resultList);
        
        Assert.Equal(2, resultList.Count);
        
        var notFoundResult = await _incidentResponsePlansController.GetByIdAsync(1000);
        
        Assert.NotNull(notFoundResult);
        Assert.IsType<NotFoundResult>(notFoundResult.Result);
        
    }

    [Fact]
    public async Task TestCreateTasksAsync()
    {
        var task = new IncidentResponsePlanTask
        {
            Description = "Task 3",
            CreatedById = 1,
            UpdatedById = 1,
            PlanId = 1
        };
        
        var result = await _incidentResponsePlansController.CreateTasksAsync(1, task); 
        
        Assert.NotNull(result);
        
        Assert.IsType<CreatedResult>(result.Result);
        
        var createdResult = (CreatedResult)result.Result;
        var createdTask = (IncidentResponsePlanTask)createdResult.Value;
        
        Assert.NotNull(createdTask);
        
        Assert.Equal(1, createdTask.PlanId);
        Assert.Equal("Task 3", createdTask.Description);
        
    }


    [Fact]
    public async Task TestUpdateTaskAsync()
    {
        var task = new IncidentResponsePlanTask
        {
            Description = "Task 3",
            CreatedById = 1,
            UpdatedById = 1,
            PlanId = 2, 
            Id = 1
        };
        
        var result = await _incidentResponsePlansController.UpdateTaskAsync(2, 1, task); 
        
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);
        
    }

    [Fact]
    public async Task TestDeleteTaskAsync()
    {
        var result = await _incidentResponsePlansController.DeleteTaskAsync(2, 1); 
        
        Assert.NotNull(result);
        
        Assert.IsType<OkResult>(result);

    }

}