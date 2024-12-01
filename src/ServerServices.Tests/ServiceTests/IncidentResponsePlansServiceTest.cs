using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(IncidentResponsePlansService))]
public class IncidentResponsePlansServiceTest: BaseServiceTest
{
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;
    
    public IncidentResponsePlansServiceTest()
    {
        _incidentResponsePlansService = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }

    [Fact]
    public async Task TestGetAllAsync()
    {

        var result1 = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(result1);
        Assert.Equal(15, result1.Count);


    }
    
    [Fact]
    public async Task TestCreateAsync()
    {

        var newirp = new IncidentResponsePlan
        {
            Name = "IRP16",
            Description = "D16"
        };

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentResponsePlansService.CreateAsync(newirp, user);
        
        Assert.NotNull(result1);
        Assert.Equal(0, result1.Id);
        Assert.Equal("IRP16", result1.Name);
        Assert.Equal("D16", result1.Description);
        Assert.Equal(1, result1.CreatedById);
        Assert.Equal((int)IntStatus.AwaitingApproval, result1.Status);
        

    }

    [Fact]
    public async Task TestUpdateAsync()
    {
        
        var oldIrps = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(oldIrps);
        Assert.True(oldIrps.Count > 0);
        
        var newirp = oldIrps[0];
        
        newirp.Name = "IRP16";
        newirp.Description = "D16";
        
        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentResponsePlansService.UpdateAsync(newirp, user);
        
        Assert.NotNull(result1);
        Assert.Equal(newirp.Id, result1.Id);
        Assert.Equal("IRP16", result1.Name);
        Assert.Equal("D16", result1.Description);
        
        var result2 = await _incidentResponsePlansService.GetByIdAsync(newirp.Id);
        
        Assert.NotNull(result2);
        Assert.Equal(newirp.Id, result2.Id);
        
    }

    [Fact]
    public async Task TestGetByIdAsync()
    {
        var irp = await _incidentResponsePlansService.GetByIdAsync(1);
        
        Assert.NotNull(irp);
        Assert.Equal(1, irp.Id);

        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
                await _incidentResponsePlansService.GetByIdAsync(20)
        );
        
        var irpt = await _incidentResponsePlansService.GetByIdAsync(2, true);
        
        Assert.NotNull(irpt);
        Assert.Equal(2, irpt.Id);
        Assert.True( irpt.Tasks.Count == 3);

    }

    [Fact]
    public async Task DeleteAsync()
    {
        var irps = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(irps);
        Assert.True(irps.Count > 0);
        
        var irp = irps[0];
        
        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        await _incidentResponsePlansService.DeleteAsync(irp.Id, user);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
            await _incidentResponsePlansService.GetByIdAsync(irp.Id)
        );
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
            await _incidentResponsePlansService.DeleteAsync(irp.Id, user)
        );
    }
    
    [Fact]
    public async Task TestCreateTaskAsync()
    {

        var newirpt = new IncidentResponsePlanTask
        {
            Description = "T1",
            ConditionToSkip = "---",
            CompletionCriteria = "---",
            PlanId = 1
        };

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentResponsePlansService.CreateTaskAsync(newirpt, user);
        
        Assert.NotNull(result1);
        Assert.Equal(0, result1.Id);
        Assert.Equal("T1", result1.Description);
        Assert.Equal(1, result1.CreatedById);
        Assert.Equal((int)IntStatus.AwaitingApproval, result1.Status);

    }

    [Fact]
    public async Task TestUpdateTaskAsync()
    {

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };

        var existingIrp = await _incidentResponsePlansService.GetByIdAsync(2, true);
        var existingIrpt = existingIrp.Tasks.First();

        Assert.NotNull(existingIrpt);

        existingIrpt.Description = "T1.1";

        await _incidentResponsePlansService.UpdateTaskAsync(existingIrpt, user);

        var updatedIrpt = await _incidentResponsePlansService.GetTaskByIdAsync(existingIrpt.Id);

        Assert.NotNull(updatedIrpt);
        Assert.Equal(existingIrpt.Id, updatedIrpt.Id);
        //Assert.Equal("T1.1", updatedIrpt.Description);
        //Assert.Equal(1, updatedIrpt.UpdatedById);
    }

    [Fact]
    public async Task TestDeleteAsync()
    {
        var existingIrpt = await _incidentResponsePlansService.GetByIdAsync(2, true);
        Assert.NotNull(existingIrpt);
        
        await Assert.ThrowsAsync<DataNotFoundException>( async () =>
            await _incidentResponsePlansService.DeleteTaskAsync(100)
        );
        
        await _incidentResponsePlansService.DeleteTaskAsync(existingIrpt.Tasks.First().Id);
        
        await Assert.ThrowsAsync<DataNotFoundException>( async () =>
            await _incidentResponsePlansService.GetTaskByIdAsync(existingIrpt.Tasks.First().Id)
        );
    }

    [Fact]
    public async Task TestGetTasksByPlanIdAsync()
    {
        var existingIrp = await _incidentResponsePlansService.GetByIdAsync(2, true);
        Assert.NotNull(existingIrp);
        
        var existingIrpt = existingIrp.Tasks.First();
        Assert.NotNull(existingIrpt);
        
        var result = await _incidentResponsePlansService.GetTasksByPlanIdAsync(existingIrp.Id);
        
        Assert.NotNull(result);
        Assert.True(result.Count > 0);
        Assert.True(result.Count == existingIrp.Tasks.Count);
    }

    [Fact]
    public async Task TestGetExecutionsByPlanIdAsync()
    {
        var existingIrp = await _incidentResponsePlansService.GetByIdAsync(2, true);
        Assert.NotNull(existingIrp);
        
        var result = await _incidentResponsePlansService.GetExecutionsByPlanIdAsync(existingIrp.Id);
        
        Assert.NotNull(result);
        Assert.True(result.Count == 1);
        
        
    }

    [Fact]
    public async Task TestGetExecutionsByIdAsync()
    {
        var execution = await _incidentResponsePlansService.GetExecutionByIdAsync(1);
        Assert.NotNull(execution);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
            await _incidentResponsePlansService.GetExecutionByIdAsync(100)
        );
    }

    [Fact]
    public async Task TestCreateExecutionsAsync()
    {
        var existingIrp = await _incidentResponsePlansService.GetByIdAsync(2, true);
        Assert.NotNull(existingIrp);

        var existingIrpt = existingIrp.Tasks.First();
        Assert.NotNull(existingIrpt);

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("teste@teste.com")
        };
        
        var newExecution = new IncidentResponsePlanExecution
        {
            PlanId = existingIrp.Id,
            Status = (int)IntStatus.New,
            ExecutedById = user.Value
        };
        
        var result = await _incidentResponsePlansService.CreateExecutionAsync(newExecution, user);
        
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TestUpdateExecutionsAsync()
    {
        
        var existingIrp = await _incidentResponsePlansService.GetByIdAsync(2, true);
        Assert.NotNull(existingIrp);
        

        var existingExecution = existingIrp.Executions.First();
        Assert.NotNull(existingExecution);

        existingExecution.Status = (int)IntStatus.Completed;
        
        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("teste@teste.com")
        };
        
        var result = await _incidentResponsePlansService.UpdateExecutionAsync(existingExecution, user);
        
        Assert.NotNull(result);
        Assert.Equal((int)IntStatus.Completed, result.Status);
        
    }

    [Fact]
    public async Task TestDeleteExecutionsAsync()
    {
        var existing = await _incidentResponsePlansService.GetExecutionByIdAsync(1);
        Assert.NotNull(existing);
        await _incidentResponsePlansService.DeleteExecutionAsync(1);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
            await _incidentResponsePlansService.GetExecutionByIdAsync(1)
        );
        
    }
    
    [Fact]
    public async Task TestGetTaskExecutionsByIdAsync()
    {
        var texecution = await _incidentResponsePlansService.GetTaskExecutionsByIdAsync(1);
        Assert.NotNull(texecution);
        Assert.True(texecution.Count > 0);

    }
    
    [Fact]
    public async Task TestCreateTaskExecutionsAsync()
    {
        var existingIrp = await _incidentResponsePlansService.GetTaskByIdAsync(1);
        Assert.NotNull(existingIrp);
        

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("teste@teste.com")
        };
        
        var newExecution = new IncidentResponsePlanTaskExecution
        {
            TaskId = existingIrp.Id,
            Status = (int)IntStatus.New,
            ExecutedById = user.Value,
            CreatedAt = DateTime.Now,
            Notes = "---"
        };
        
        var result = await _incidentResponsePlansService.CreateTaskExecutionAsync(newExecution, user);
        
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TestGetTaskExecutionAsyncById()
    {
        var existingIrp = await _incidentResponsePlansService.GetTaskExecutionByIdAsync(1);
        Assert.NotNull(existingIrp);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionsAsync()
    {
        var existingIrp = await _incidentResponsePlansService.GetTaskExecutionByIdAsync(1);
        Assert.NotNull(existingIrp);
        
        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("teste@teste.com")
        };

        existingIrp.Notes = "test1";
        
        var result = await _incidentResponsePlansService.UpdateTaskExecutionAsync(existingIrp, user);
        
        Assert.NotNull(result);
        Assert.Equal("test1", result.Notes);


    }
    
    [Fact]
    public async Task TestDeleteTaskExecutionsAsync()
    {
        var existing = await _incidentResponsePlansService.GetTaskExecutionsByIdAsync(1);
        Assert.NotNull(existing);
        await _incidentResponsePlansService.DeleteExecutionAsync(1);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () =>
            await _incidentResponsePlansService.GetExecutionByIdAsync(1)
        );
        
    }

}