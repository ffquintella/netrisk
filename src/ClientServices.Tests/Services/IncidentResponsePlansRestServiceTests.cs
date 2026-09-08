using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Tests.Mock;
using DAL.Entities;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// The happy path of every method on the plans service, one canned response per route.
///
/// This class used to resolve the service from the shared NSubstitute container, whose
/// <c>MockSetup</c> can only stand in for <c>GetReliableClient()</c> — <c>IRestService.GetClient</c>
/// returns a concrete <c>RestClient</c>, so its stub returned null and every method that asked for a
/// plain client threw a <c>NullReferenceException</c>. That was invisible while the writes went
/// through the reliable client and became twelve failures the moment they stopped, which is a fair
/// description of what the mock was worth: it could only test the half of the service that was
/// wired the wrong way. <see cref="StubRestBackend"/> stubs the transport instead and serves both
/// clients, so the routes and statuses below are the same ones production sends.
/// </summary>
public class IncidentResponsePlansRestServiceTests : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;

    public IncidentResponsePlansRestServiceTests()
    {
        var plan = Plans()[0];
        var task = plan.Tasks!.First();

        _backend
            .OnGet("/IncidentResponsePlans", Plans())
            .OnGet("/IncidentResponsePlans/1", plan)
            .On(Method.Post, "/IncidentResponsePlans",
                new IncidentResponsePlan { Id = 1, Name = "TestCreate", Description = "Test" },
                HttpStatusCode.Created)
            .OnPut("/IncidentResponsePlans/1",
                new IncidentResponsePlan { Id = 1, Name = "TestUpdate", Description = "Test" })
            .OnDelete("/IncidentResponsePlans/1", "")
            .OnGet("/IncidentResponsePlans/1/Tasks/1", task)
            .On(Method.Post, "/IncidentResponsePlans/1/Tasks",
                new IncidentResponsePlanTask { Id = 1, PlanId = 1, Description = "Task 3" },
                HttpStatusCode.Created)
            .OnPut("/IncidentResponsePlans/1/Tasks/1",
                new IncidentResponsePlanTask { Id = 1, PlanId = 1, Description = "Test" })
            .OnDelete("/IncidentResponsePlans/1/Tasks/1", "")
            .OnGet("/IncidentResponsePlans/1/Tasks/1/Executions", task.Executions!.ToList())
            .OnGet("/IncidentResponsePlans/1/Tasks/1/Executions/1", task.Executions!.First())
            .On(Method.Post, "/IncidentResponsePlans/1/Tasks/1/Executions",
                new IncidentResponsePlanTaskExecution { Id = 1, TaskId = 1 }, HttpStatusCode.Created)
            .OnPut("/IncidentResponsePlans/1/Tasks/1/Executions/1",
                new IncidentResponsePlanTaskExecution { Id = 1, TaskId = 1 })
            .OnDelete("/IncidentResponsePlans/1/Tasks/1/Executions/1", "")
            .OnGet("/IncidentResponsePlans/1/Executions", plan.Executions!.ToList())
            .OnGet("/IncidentResponsePlans/1/Executions/1", plan.Executions!.First())
            .On(Method.Post, "/IncidentResponsePlans/1/Executions",
                new IncidentResponsePlanExecution { Id = 1, PlanId = 1 }, HttpStatusCode.Created)
            .OnPut("/IncidentResponsePlans/1/Executions/1",
                new IncidentResponsePlanExecution { Id = 1, PlanId = 1 })
            .OnDelete("/IncidentResponsePlans/1/Executions/1", "");

        _incidentResponsePlansService = ResolveWith<IIncidentResponsePlansService>(_backend);
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
            Id = 0,
            Name = "TestCreate",
            Description = "Test"
        };

        var createdPlan = await _incidentResponsePlansService.CreateAsync(plan);

        createdPlan.Name = "TestUpdate";

        var updatedPlan = await _incidentResponsePlansService.UpdateAsync(createdPlan);

        Assert.NotNull(updatedPlan);
        Assert.Equal(1, updatedPlan.Id);
        Assert.Equal("TestUpdate", updatedPlan.Name);
    }

    [Fact]
    public async Task TestDeleteAsync()
    {
        var plan = new IncidentResponsePlan()
        {
            Id = 0,
            Name = "TestCreate",
            Description = "Test"
        };

        var createdPlan = await _incidentResponsePlansService.CreateAsync(plan);

        await _incidentResponsePlansService.DeleteAsync(createdPlan.Id);
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

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsync()
    {
        var taskExecutions = await _incidentResponsePlansService.GetTaskExecutionsByIdAsync(1,1);

        Assert.NotNull(taskExecutions);
        Assert.Equal(2, taskExecutions.Count);
    }

    [Fact]
    public async Task TestGetExecutionByTaskIdAsync()
    {
        var execution = await _incidentResponsePlansService.GetExecutionByTaskIdAsync(1,1,1);

        Assert.NotNull(execution);
        Assert.Equal(1, execution.Id);
    }

    [Fact]
    public async Task TestGetExecutionsByPlanIdAsync()
    {
        var executions = await _incidentResponsePlansService.GetExecutionsByPlanIdAsync(1);

        Assert.NotNull(executions);
        Assert.Equal(2, executions.Count);
    }


    [Fact]
    public async Task TestGetExecutionByIdAsync()
    {
        var execution = await _incidentResponsePlansService.GetExecutionByIdAsync(1,1);

        Assert.NotNull(execution);
        Assert.Equal(1, execution.Id);
    }

    [Fact]
    public async Task TestCreateExecutionAsync()
    {
        var execution = new IncidentResponsePlanExecution
        {
            PlanId = 1,
            Id = 0,
        };

        var createdExecution = await _incidentResponsePlansService.CreateExecutionAsync(execution);

        Assert.NotNull(createdExecution);
        Assert.NotEqual(0, createdExecution.Id);
    }

    [Fact]
    public async Task TestCreateTaskExecutionAsync()
    {
        var taskExecution = new IncidentResponsePlanTaskExecution
        {
            TaskId = 1,
            Id = 0,
        };

        var createdTaskExecution = await _incidentResponsePlansService.CreateTaskExecutionAsync(1, taskExecution);

        Assert.NotNull(createdTaskExecution);
        Assert.NotEqual(0, createdTaskExecution.Id);
    }

    [Fact]
    public async Task TestUpdateExecutionAsync()
    {
        var execution = new IncidentResponsePlanExecution
        {
            Id = 1,
            PlanId = 1
        };

        var updatedExecution = await _incidentResponsePlansService.UpdateExecutionAsync(execution);

        Assert.NotNull(updatedExecution);
        Assert.Equal(1, updatedExecution.Id);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionAsync()
    {
        var taskExecution = new IncidentResponsePlanTaskExecution
        {
            Id = 1,
            TaskId = 1
        };

        var updatedTaskExecution = await _incidentResponsePlansService.UpdateTaskExecutionAsync(1,taskExecution);

        Assert.NotNull(updatedTaskExecution);
        Assert.Equal(1, updatedTaskExecution.Id);
    }

    [Fact]
    public async Task TestDeleteExecutionAsync()
    {
        await _incidentResponsePlansService.DeleteExecutionAsync(1,1);
    }

    [Fact]
    public async Task TestDeleteTaskExecutionAsync()
    {
        await _incidentResponsePlansService.DeleteTaskExecutionAsync(1,1, 1);
    }

    // The same two plans the NSubstitute mock served, so the counts these tests assert still mean
    // what they meant.
    private static List<IncidentResponsePlan> Plans()
    {
        var anchor = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return
        [
            new IncidentResponsePlan
            {
                Id = 1,
                Name = "IncidentResponsePlan1",
                Description = "IncidentResponsePlan1 Description",
                Executions =
                [
                    new IncidentResponsePlanExecution
                        { Id = 1, ExecutionDate = anchor, PlanId = 1, ExecutedById = 1 },
                    new IncidentResponsePlanExecution
                        { Id = 2, ExecutionDate = anchor, PlanId = 1, ExecutedById = 2 }
                ],
                Tasks =
                [
                    new IncidentResponsePlanTask
                    {
                        Id = 1,
                        Description = "Task 1",
                        Executions =
                        [
                            new IncidentResponsePlanTaskExecution
                                { Id = 1, ExecutionDate = anchor, TaskId = 1, ExecutedById = 1 },
                            new IncidentResponsePlanTaskExecution
                                { Id = 2, ExecutionDate = anchor, TaskId = 1, ExecutedById = 2 }
                        ]
                    },
                    new IncidentResponsePlanTask { Id = 2, Description = "Task 2" }
                ]
            },
            new IncidentResponsePlan
            {
                Id = 2,
                Name = "IncidentResponsePlan2",
                Description = "IncidentResponsePlan2 Description"
            }
        ];
    }
}
