using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class IncidentResponsePlansServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIncidentResponsePlansService _svc;
    private static readonly User Admin = new() { Value = 1, Name = "Admin", Type = "local", Admin = true, Lang = "en" };

    public IncidentResponsePlansServiceInMemoryTest() => _svc = GetService<IIncidentResponsePlansService>();

    private static IncidentResponsePlan NewPlan(int id, bool approved = false) => new()
    {
        Id = id, Name = $"IRP{id}", Description = "d", HasBeenApproved = approved,
        CreationDate = new DateTime(2026, 1, 1), LastUpdate = new DateTime(2026, 1, 1)
    };

    private static IncidentResponsePlanTask NewTask(int id, int planId) => new()
    {
        Id = id, PlanId = planId, Name = $"Task{id}"
    };

    private static IncidentResponsePlanExecution NewExecution(int id, int planId, bool isTest = true) => new()
    {
        Id = id, PlanId = planId, IsTest = isTest, ExecutionTrigger = "manual", ExecutionResult = "---",
        Duration = TimeSpan.Zero
    };

    [Fact]
    public async Task TestCreateGetUpdateDelete()
    {
        var created = await _svc.CreateAsync(NewPlan(0), Admin);
        Assert.True(created.Id > 0);

        Assert.Equal(created.Id, (await _svc.GetByIdAsync(created.Id)).Id);
        Assert.NotEmpty(await _svc.GetAllAsync());

        created.Name = "Renamed";
        var updated = await _svc.UpdateAsync(created, Admin);
        Assert.Equal("Renamed", updated.Name);
        Assert.True(updated.HasBeenUpdated);

        await _svc.DeleteAsync(created.Id, Admin);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task TestGetAllApproved()
    {
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(NewPlan(1, approved: true));
            ctx.IncidentResponsePlans.Add(NewPlan(2, approved: false));
        });

        Assert.Single(await _svc.GetAllApprovedAsync());
    }

    [Fact]
    public async Task TestGetByIdIncludeVariants()
    {
        Seed(ctx =>
        {
            var plan = NewPlan(1);
            plan.Tasks.Add(NewTask(1, 1));
            ctx.IncidentResponsePlans.Add(plan);
        });

        Assert.NotNull(await _svc.GetByIdAsync(1, includeTasks: true));
        Assert.NotNull(await _svc.GetByIdAsync(1, includeActivatedBy: true));
        Assert.NotNull(await _svc.GetByIdAsync(1, includeTasks: true, includeActivatedBy: true));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(99));
    }

    [Fact]
    public async Task TestTaskLifecycle()
    {
        Seed(ctx => ctx.IncidentResponsePlans.Add(NewPlan(1)));

        var task = await _svc.CreateTaskAsync(NewTask(0, 1), Admin);
        Assert.True(task.Id > 0);

        Assert.Equal(task.Id, (await _svc.GetTaskByIdAsync(task.Id)).Id);
        Assert.Single(await _svc.GetTasksByPlanIdAsync(1));

        // Pass a fresh, detached task (the returned one carries EF-fixup nav cycles).
        var taskUpdate = NewTask(task.Id, 1);
        taskUpdate.Name = "Updated";
        await _svc.UpdateTaskAsync(taskUpdate, Admin);
        Assert.Equal("Updated", (await _svc.GetTaskByIdAsync(task.Id)).Name);

        await _svc.DeleteTaskAsync(task.Id);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetTaskByIdAsync(task.Id));
    }

    [Fact]
    public async Task TestCreateTaskMissingPlanThrows()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.CreateTaskAsync(NewTask(0, 999), Admin));
    }

    [Fact]
    public async Task TestExecutionLifecycle()
    {
        var incident = new Incident { Id = 1, Description = "d", Name = "INC" };
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(NewPlan(1));
            ctx.Incidents.Add(incident);
        });

        // IsTest = true skips task-execution creation / e-mail.
        var execution = await _svc.CreateExecutionAsync(NewExecution(0, 1, isTest: true), incident, Admin);
        Assert.True(execution.Id > 0);

        Assert.Equal(execution.Id, (await _svc.GetExecutionByIdAsync(execution.Id)).Id);
        Assert.Single(await _svc.GetExecutionsByPlanIdAsync(1));

        var executionUpdate = NewExecution(execution.Id, 1);
        executionUpdate.ExecutionResult = "done";
        var updated = await _svc.UpdateExecutionAsync(executionUpdate, Admin);
        Assert.Equal("done", updated.ExecutionResult);

        await _svc.DeleteExecutionAsync(execution.Id);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetExecutionByIdAsync(execution.Id));
    }

    [Fact]
    public async Task TestNotFoundPaths()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.UpdateAsync(NewPlan(99), Admin));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.DeleteAsync(99, Admin));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetTaskExecutionByIdAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.DeleteExecutionAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.DeleteTaskExecutionAsync(99));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetExecutionByIdAsync(99));
    }
}

public class IncidentsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIncidentsService _svc;
    private static readonly User Admin = new() { Value = 1, Name = "Admin", Type = "local", Admin = true, Lang = "en" };

    public IncidentsServiceInMemoryTest() => _svc = GetService<IIncidentsService>();

    private static Incident NewIncident(int id, int year = 2026, int seq = 1) => new()
    {
        Id = id, Year = year, Sequence = seq, Name = $"INC-{id}", Description = "d", Category = "general"
    };

    [Fact]
    public async Task TestCreateGetAllGetById()
    {
        var created = await _svc.CreateAsync(NewIncident(0), Admin);
        Assert.True(created.Id > 0);
        Assert.Equal(1, created.CreatedById);

        Assert.Single(await _svc.GetAllAsync());
        Assert.Equal(created.Id, (await _svc.GetByIdAsync(created.Id)).Id);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(999));
    }

    [Fact]
    public async Task TestGetNextSequence()
    {
        Assert.Equal(1, await _svc.GetNextSequenceAsync(2026));

        Seed(ctx =>
        {
            ctx.Incidents.Add(NewIncident(1, year: 2026, seq: 1));
            ctx.Incidents.Add(NewIncident(2, year: 2026, seq: 2));
        });

        Assert.Equal(3, await _svc.GetNextSequenceAsync(2026));
    }

    [Fact]
    public async Task TestUpdateAndDelete()
    {
        Seed(ctx => ctx.Incidents.Add(NewIncident(1)));

        var update = NewIncident(1);
        update.Name = "Updated";
        var updated = await _svc.UpdateAsync(update, Admin);
        Assert.Equal("Updated", updated.Name);

        await _svc.DeleteByIdAsync(1);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.DeleteByIdAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.UpdateAsync(NewIncident(99), Admin));
    }

    [Fact]
    public async Task TestIncidentResponsePlanAssociation()
    {
        Seed(ctx =>
        {
            ctx.Incidents.Add(NewIncident(1));
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
                { Id = 5, Name = "IRP", Description = "d", CreationDate = DateTime.Now, LastUpdate = DateTime.Now });
        });

        Assert.Empty(await _svc.GetIncidentResponsPlanIdsByIdAsync(1));

        await _svc.AssociateIncidentResponsPlanIdsByIdAsync(1, new List<int> { 5 }, Admin);

        Assert.Single(await _svc.GetIncidentResponsPlanIdsByIdAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.GetIncidentResponsPlanIdsByIdAsync(999));
    }

    [Fact]
    public async Task TestGetAttachments()
    {
        // Backed by FilesServiceMock; just exercises the delegation.
        Assert.NotNull(await _svc.GetAttachmentsByIdAsync(1));
    }
}
