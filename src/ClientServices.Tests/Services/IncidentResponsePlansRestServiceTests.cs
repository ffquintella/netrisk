using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

public class IncidentResponsePlansRestServiceTests: BaseServiceTest
{
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;
    
    public IncidentResponsePlansRestServiceTests()
    {
        _incidentResponsePlansService = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }
    
    [Fact]
    public async Task TestGetAllAsync()
    {
        
        var plans = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(plans);
        
        Assert.Equal(2, plans.Count);
        
    }

    [Fact]
    public async Task TestCreateAsync()
    {
        
        var plan = new IncidentResponsePlan()
        {
            Id = 0,
            Name = "TestCreate",
            Description = "Test"
        };
        
        var createdPlan = await _incidentResponsePlansService.CreateAsync(plan);
        
        Assert.NotNull(createdPlan);
        Assert.NotEqual(0, createdPlan.Id);
        Assert.Equal("TestCreate", createdPlan.Name);
        
    }

    [Fact]
    public async Task TestUpdateAsync()
    {
        var plan = new IncidentResponsePlan()
        {
            Id = 1,
            Name = "TestUpdate",
            Description = "Test"
        };
        
        var updatedPlan = await _incidentResponsePlansService.UpdateAsync(plan);
        
        Assert.NotNull(updatedPlan);
        Assert.Equal(1, updatedPlan.Id);
        Assert.Equal("TestUpdate", updatedPlan.Name);
    }
    
    [Fact]
    public async Task TestDeleteAsync()
    {
        await _incidentResponsePlansService.DeleteAsync(1);
        
    }
    
    [Fact]
    public async Task TestGetByIdAsync()
    {
        var plan = await _incidentResponsePlansService.GetByIdAsync(1);
        
        Assert.NotNull(plan);
        Assert.Equal(1, plan.Id);
        Assert.Equal("IncidentResponsePlan1", plan.Name);
        
    }

    [Fact]
    public async Task TestCreateTaskAsync()
    {
        var task = new IncidentResponsePlanTask
        {
            Description = "Task 3",
            PlanId = 1
        };
        
        var createdTask = await _incidentResponsePlansService.CreateTaskAsync(task);
        
        Assert.NotNull(createdTask);
        Assert.NotEqual(0, createdTask.Id);
        Assert.Equal("Task 3", createdTask.Description);
    }

    [Fact]
    public async Task TestUpdateTaskAsync()
    {
        var task = new IncidentResponsePlanTask
        {
            Id = 1,
            Description = "Task 3",
            PlanId = 1
        };
        
        var updatedTask = await _incidentResponsePlansService.UpdateTaskAsync(task);
        
        Assert.NotNull(updatedTask);
        Assert.Equal(1, updatedTask.Id);
    }

    [Fact]
    public async Task TestGetTaskByIdAsync()
    {
        var task = await _incidentResponsePlansService.GetTaskByIdAsync(1, 1);
        
        Assert.NotNull(task);
        Assert.Equal(1, task.Id);
    }

    [Fact]
    public async Task TestDeleteTaskAsync()
    {
        await _incidentResponsePlansService.DeleteTaskAsync(1, 1);
    }
}