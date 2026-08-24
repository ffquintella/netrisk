using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using Model.IncidentResponsePlan;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Covers the actions and error branches of <see cref="IncidentResponsePlansController"/> that
/// <see cref="IncidentResponsePlansControllerTest"/> leaves untouched. Every collaborator is a
/// per-test substitute, so a test can arrange the same method to succeed here and blow up there
/// without disturbing the shared mocks in <c>API.Tests/Mock</c>.
/// </summary>
[TestSubject(typeof(IncidentResponsePlansController))]
public class IncidentResponsePlansControllerExtendedTest : BaseControllerTest
{
    private readonly IIncidentResponsePlansService _plansService = Substitute.For<IIncidentResponsePlansService>();
    private readonly IIncidentsService _incidentsService = Substitute.For<IIncidentsService>();
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IIrpScheduleService _scheduleService = Substitute.For<IIrpScheduleService>();

    private readonly IncidentResponsePlansController _controller;

    public IncidentResponsePlansControllerExtendedTest()
    {
        _controller = ResolveController<IncidentResponsePlansController>(s =>
        {
            s.AddSingleton(_plansService);
            s.AddSingleton(_incidentsService);
            s.AddSingleton(_filesService);
            s.AddSingleton(_scheduleService);
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Several catch blocks log <c>ex.InnerException!.Message</c>, so an exception used to drive
    /// them has to carry an inner one or the handler itself throws.
    /// </summary>
    private static Exception Wrapped() => new Exception("boom", new Exception("inner"));

    private static void AssertStatus(int expected, IActionResult result)
    {
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(expected, status.StatusCode);
    }

    private static void AssertObjectStatus(int expected, IActionResult result)
    {
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expected, status.StatusCode);
    }

    private static IncidentResponsePlan Plan(int id, string name = "Plan")
        => new() { Id = id, Name = name, Description = "d" };

    private static IncidentResponsePlanTask PlanTask(int id, int planId)
        => new() { Id = id, PlanId = planId, Name = "Task", Description = "Task" };

    private static IncidentResponsePlanExecution Execution(int id, int planId, int status)
        => new()
        {
            Id = id, PlanId = planId, Status = status,
            ExecutionTrigger = "trigger", ExecutionResult = "result"
        };

    // ------------------------------------------------------------- GetAllAsync

    [Fact]
    public async Task TestGetAllAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetAllAsync().Returns<Task<List<IncidentResponsePlan>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetAllAsync();

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ----------------------------------------------------- GetAllApprovedAsync

    [Fact]
    public async Task TestGetAllApprovedAsync()
    {
        _plansService.GetAllApprovedAsync().Returns(new List<IncidentResponsePlan> { Plan(1), Plan(2) });

        var result = await _controller.GetAllApprovedAsync();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncidentResponsePlan>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetAllApprovedAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetAllApprovedAsync()
            .Returns<Task<List<IncidentResponsePlan>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetAllApprovedAsync();

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // --------------------------------------------------------- GetScheduleAsync

    [Fact]
    public async Task TestGetScheduleAsync()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _scheduleService.GetScheduleAsync(1).Returns(new IrpSchedule
        {
            PlanId = 1,
            PlanName = "Plan",
            PlanStart = start,
            PlanEnd = start.AddHours(2),
            TotalDuration = TimeSpan.FromHours(2),
            CriticalPath = new List<int> { 1 },
            Items = new List<IrpScheduleItem>
            {
                new() { TaskId = 1, Name = "Contain", ExecutionOrder = 1, IsCritical = true }
            }
        });

        var result = await _controller.GetScheduleAsync(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var schedule = Assert.IsType<IrpSchedule>(ok.Value);
        Assert.Equal(1, schedule.PlanId);
        Assert.Single(schedule.Items);
        Assert.Single(schedule.CriticalPath);
    }

    [Fact]
    public async Task TestGetScheduleAsyncReturnsNotFoundForUnknownPlan()
    {
        _scheduleService.GetScheduleAsync(999).Returns((IrpSchedule)null);

        var result = await _controller.GetScheduleAsync(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestGetScheduleAsyncReturnsInternalServerErrorOnFailure()
    {
        _scheduleService.GetScheduleAsync(2).Returns<Task<IrpSchedule>>(_ => throw new Exception("nope"));

        var result = await _controller.GetScheduleAsync(2);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ----------------------------------------------------- GetDependenciesAsync

    [Fact]
    public async Task TestGetDependenciesAsync()
    {
        _scheduleService.GetDependenciesAsync(1).Returns(new List<IrpTaskDependency>
        {
            new() { Id = 1, TaskId = 2, TaskName = "Eradicate", DependsOnTaskId = 1, DependsOnTaskName = "Contain" }
        });

        var result = await _controller.GetDependenciesAsync(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IrpTaskDependency>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(1, list[0].DependsOnTaskId);
    }

    [Fact]
    public async Task TestGetDependenciesAsyncReturnsInternalServerErrorOnFailure()
    {
        _scheduleService.GetDependenciesAsync(2)
            .Returns<Task<List<IrpTaskDependency>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetDependenciesAsync(2);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------- AddDependencyAsync

    [Fact]
    public async Task TestAddDependencyAsync()
    {
        _scheduleService.AddDependencyAsync(1, 2, 3)
            .Returns(new IrpTaskDependency { Id = 10, TaskId = 2, DependsOnTaskId = 3 });

        var result = await _controller.AddDependencyAsync(1, 2, 3);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var edge = Assert.IsType<IrpTaskDependency>(created.Value);
        Assert.Equal(10, edge.Id);
        Assert.Equal(2, edge.TaskId);
        Assert.Equal(3, edge.DependsOnTaskId);
    }

    [Fact]
    public async Task TestAddDependencyAsyncReturnsNotFoundWhenTargetMissing()
    {
        _scheduleService.AddDependencyAsync(1, 2, 999)
            .Returns<Task<IrpTaskDependency>>(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.AddDependencyAsync(1, 2, 999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAddDependencyAsyncReturnsBadRequestOnCycle()
    {
        _scheduleService.AddDependencyAsync(1, 2, 2)
            .Returns<Task<IrpTaskDependency>>(_ => throw new RuleBrokenException("cycle", "no-cycles"));

        var result = await _controller.AddDependencyAsync(1, 2, 2);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("cycle", bad.Value);
    }

    // ----------------------------------------------------- RemoveDependencyAsync

    [Fact]
    public async Task TestRemoveDependencyAsync()
    {
        var result = await _controller.RemoveDependencyAsync(1, 2, 3);

        Assert.IsType<NoContentResult>(result);
        await _scheduleService.Received(1).RemoveDependencyAsync(1, 2, 3);
    }

    // -------------------------------------------------- CompleteWithOverrideAsync

    [Fact]
    public async Task TestCompleteWithOverrideAsync()
    {
        _scheduleService.CompleteBlockedTaskAsync(1, 2, 1, "because")
            .Returns(new IrpScheduleItem { TaskId = 2, Name = "Eradicate", Status = (int)IntStatus.Closed });

        var result = await _controller.CompleteWithOverrideAsync(1, 2, new IrpOverrideRequest { Reason = "because" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var item = Assert.IsType<IrpScheduleItem>(ok.Value);
        Assert.Equal(2, item.TaskId);
    }

    [Fact]
    public async Task TestCompleteWithOverrideAsyncReturnsNotFound()
    {
        _scheduleService.CompleteBlockedTaskAsync(1, 999, 1, "because")
            .Returns<Task<IrpScheduleItem>>(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.CompleteWithOverrideAsync(1, 999, new IrpOverrideRequest { Reason = "because" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestCompleteWithOverrideAsyncReturnsBadRequestWithoutReason()
    {
        _scheduleService.CompleteBlockedTaskAsync(1, 2, 1, string.Empty)
            .Returns<Task<IrpScheduleItem>>(_ => throw new RuleBrokenException("reason required", "override-reason"));

        var result = await _controller.CompleteWithOverrideAsync(1, 2, new IrpOverrideRequest());

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("reason required", bad.Value);
    }

    // ------------------------------------------------------------ GetByIdAsync

    [Fact]
    public async Task TestGetByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetByIdAsync(5).Returns<Task<IncidentResponsePlan>>(_ => throw new Exception("nope"));

        var result = await _controller.GetByIdAsync(5);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------- GetAttachementsByIdAsync

    [Fact]
    public async Task TestGetAttachementsByIdAsync()
    {
        _filesService.GetObjectFileListingsAsync(1, FileCollectionType.IncidentResponsePlanFile)
            .Returns(new List<FileListing>
            {
                new() { Name = "plan.pdf", UniqueName = "u1", OwnerId = 1 }
            });

        var result = await _controller.GetAttachementsByIdAsync(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<FileListing>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("plan.pdf", list[0].Name);
    }

    [Fact]
    public async Task TestGetAttachementsByIdAsyncReturnsNotFound()
    {
        _filesService.GetObjectFileListingsAsync(999, FileCollectionType.IncidentResponsePlanFile)
            .Returns<Task<List<FileListing>>>(_ => throw new DataNotFoundException("plan", "999"));

        var result = await _controller.GetAttachementsByIdAsync(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetAttachementsByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _filesService.GetObjectFileListingsAsync(2, FileCollectionType.IncidentResponsePlanFile)
            .Returns<Task<List<FileListing>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetAttachementsByIdAsync(2);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // --------------------------------------------------- GetTaskAttachmentsAsync

    [Fact]
    public async Task TestGetTaskAttachmentsAsync()
    {
        _filesService.GetObjectFileListingsAsync(7, FileCollectionType.IncidentResponsePlanTaskFile)
            .Returns(new List<FileListing>
            {
                new() { Name = "task.pdf", UniqueName = "u2", OwnerId = 7 }
            });

        var result = await _controller.GetTaskAttachmentsAsync(1, 7);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<FileListing>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("task.pdf", list[0].Name);
    }

    [Fact]
    public async Task TestGetTaskAttachmentsAsyncReturnsNotFound()
    {
        _filesService.GetObjectFileListingsAsync(999, FileCollectionType.IncidentResponsePlanTaskFile)
            .Returns<Task<List<FileListing>>>(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.GetTaskAttachmentsAsync(1, 999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetTaskAttachmentsAsyncReturnsInternalServerErrorOnFailure()
    {
        _filesService.GetObjectFileListingsAsync(8, FileCollectionType.IncidentResponsePlanTaskFile)
            .Returns<Task<List<FileListing>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetTaskAttachmentsAsync(1, 8);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------------- GetTasksByIdAsync

    [Fact]
    public async Task TestGetTasksByIdAsyncReturnsNotFound()
    {
        _plansService.GetByIdAsync(999, true)
            .Returns<Task<IncidentResponsePlan>>(_ => throw new DataNotFoundException("plan", "999"));

        var result = await _controller.GetTasksByIdAsync(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetTasksByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetByIdAsync(6, true).Returns<Task<IncidentResponsePlan>>(_ => throw new Exception("nope"));

        var result = await _controller.GetTasksByIdAsync(6);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------- GetTaskByIdAsync

    [Fact]
    public async Task TestGetTaskByIdAsyncReturnsNotFound()
    {
        _plansService.GetTaskByIdAsync(999)
            .Returns<Task<IncidentResponsePlanTask>>(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.GetTaskByIdAsync(1, 999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetTaskByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetTaskByIdAsync(4).Returns<Task<IncidentResponsePlanTask>>(_ => throw new Exception("nope"));

        var result = await _controller.GetTaskByIdAsync(1, 4);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------- CreateTasksAsync

    [Fact]
    public async Task TestCreateTasksAsyncRefusesParallelAndOptionalTask()
    {
        var task = PlanTask(0, 1);
        task.IsParallel = true;
        task.IsOptional = true;

        var result = await _controller.CreateTasksAsync(1, task);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await _plansService.DidNotReceive().CreateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>());
    }

    [Fact]
    public async Task TestCreateTasksAsyncReturnsNotFound()
    {
        _plansService.CreateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanTask>>(_ => throw new DataNotFoundException("plan", "1"));

        var result = await _controller.CreateTasksAsync(1, PlanTask(0, 1));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateTasksAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.CreateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanTask>>(_ => throw new Exception("nope"));

        var result = await _controller.CreateTasksAsync(1, PlanTask(0, 1));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // --------------------------------------------------------- UpdateTaskAsync

    [Fact]
    public async Task TestUpdateTaskAsyncRefusesParallelAndOptionalTask()
    {
        var task = PlanTask(1, 1);
        task.IsParallel = true;
        task.IsOptional = true;

        var result = await _controller.UpdateTaskAsync(1, 1, task);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await _plansService.DidNotReceive().UpdateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>());
    }

    [Fact]
    public async Task TestUpdateTaskAsyncReturnsNotFound()
    {
        _plansService.UpdateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>())
            .Returns(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.UpdateTaskAsync(1, 999, PlanTask(999, 1));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.UpdateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>())
            .Returns(_ => throw new Exception("nope"));

        var result = await _controller.UpdateTaskAsync(1, 3, PlanTask(3, 1));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // --------------------------------------------------------- DeleteTaskAsync

    [Fact]
    public async Task TestDeleteTaskAsyncReturnsNotFound()
    {
        _plansService.DeleteTaskAsync(999).Returns(_ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.DeleteTaskAsync(1, 999);

        AssertStatus(StatusCodes.Status404NotFound, result);
    }

    [Fact]
    public async Task TestDeleteTaskAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.DeleteTaskAsync(3).Returns(_ => throw new Exception("nope"));

        var result = await _controller.DeleteTaskAsync(1, 3);

        AssertStatus(StatusCodes.Status500InternalServerError, result);
    }

    // --------------------------------------------------- GetTaskExecutionsAsync

    [Fact]
    public async Task TestGetTaskExecutionsAsync()
    {
        _plansService.GetTaskExecutionsByIdAsync(2).Returns(new List<IncidentResponsePlanTaskExecution>
        {
            new() { Id = 1, TaskId = 2, PlanExecutionId = 7 },
            new() { Id = 2, TaskId = 2, PlanExecutionId = 7 }
        });

        var result = await _controller.GetTaskExecutionsAsync(1, 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncidentResponsePlanTaskExecution>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetTaskExecutionsAsyncReturnsNotFound()
    {
        _plansService.GetTaskExecutionsByIdAsync(999)
            .Returns<Task<List<IncidentResponsePlanTaskExecution>>>(
                _ => throw new DataNotFoundException("task", "999"));

        var result = await _controller.GetTaskExecutionsAsync(1, 999);

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestGetTaskExecutionsAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetTaskExecutionsByIdAsync(3)
            .Returns<Task<List<IncidentResponsePlanTaskExecution>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetTaskExecutionsAsync(1, 3);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------- GetExecutionByIdAsync

    [Fact]
    public async Task TestGetExecutionByIdAsyncReturnsNotFound()
    {
        _plansService.GetExecutionByIdAsync(999)
            .Returns<Task<IncidentResponsePlanExecution>>(_ => throw new DataNotFoundException("exec", "999"));

        var result = await _controller.GetExecutionByIdAsync(1, 999);

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestGetExecutionByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetExecutionByIdAsync(3)
            .Returns<Task<IncidentResponsePlanExecution>>(_ => throw new Exception("nope"));

        var result = await _controller.GetExecutionByIdAsync(1, 3);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------ CreateTaskExecutionsAsync

    private IncidentResponsePlan PlanWithIncident(int id)
    {
        var plan = Plan(id);
        plan.ActivatedBy = new List<Incident>
        {
            new()
            {
                Id = 30, Name = "old", Description = "d",
                CreationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Id = 31, Name = "recent", Description = "d",
                CreationDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        return plan;
    }

    [Fact]
    public async Task TestCreateTaskExecutionsAsync()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.GetByIdAsync(1, includeActivatedBy: true).Returns(PlanWithIncident(1));
        _plansService.CreateTaskExecutionAsync(
                Arg.Any<IncidentResponsePlanTaskExecution>(), Arg.Any<Incident>(), Arg.Any<User>())
            .Returns(new IncidentResponsePlanTaskExecution { Id = 40, TaskId = 2, PlanExecutionId = 7 });

        var result = await _controller.CreateTaskExecutionsAsync(1, 2,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var execution = Assert.IsType<IncidentResponsePlanTaskExecution>(ok.Value);
        Assert.Equal(40, execution.Id);
        Assert.Equal(2, execution.TaskId);

        // The most recent activating incident is the one handed to the service.
        await _plansService.Received(1).CreateTaskExecutionAsync(
            Arg.Any<IncidentResponsePlanTaskExecution>(),
            Arg.Is<Incident>(i => i.Id == 31),
            Arg.Any<User>());
    }

    [Fact]
    public async Task TestCreateTaskExecutionsAsyncRequiresActiveExecution()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Closed)
        });

        var result = await _controller.CreateTaskExecutionsAsync(1, 2,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertObjectStatus(StatusCodes.Status400BadRequest, result.Result);
    }

    [Fact]
    public async Task TestCreateTaskExecutionsAsyncRequiresIncident()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.GetByIdAsync(1, includeActivatedBy: true).Returns(Plan(1));

        var result = await _controller.CreateTaskExecutionsAsync(1, 2,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertObjectStatus(StatusCodes.Status400BadRequest, result.Result);
    }

    [Fact]
    public async Task TestCreateTaskExecutionsAsyncReturnsNotFound()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.GetByIdAsync(1, includeActivatedBy: true)
            .Returns<Task<IncidentResponsePlan>>(_ => throw new DataNotFoundException("plan", "1"));

        var result = await _controller.CreateTaskExecutionsAsync(1, 2,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestCreateTaskExecutionsAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.GetByIdAsync(1, includeActivatedBy: true).Returns(PlanWithIncident(1));
        _plansService.CreateTaskExecutionAsync(
                Arg.Any<IncidentResponsePlanTaskExecution>(), Arg.Any<Incident>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanTaskExecution>>(_ => throw new Exception("nope"));

        var result = await _controller.CreateTaskExecutionsAsync(1, 2,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------ UpdateTaskExecutionsAsync

    [Fact]
    public async Task TestUpdateTaskExecutionsAsync()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.UpdateTaskExecutionAsync(Arg.Any<IncidentResponsePlanTaskExecution>(), Arg.Any<User>())
            .Returns(new IncidentResponsePlanTaskExecution { Id = 40, TaskId = 2, PlanExecutionId = 7 });

        var result = await _controller.UpdateTaskExecutionsAsync(1, 2, 40,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var execution = Assert.IsType<IncidentResponsePlanTaskExecution>(ok.Value);
        Assert.Equal(40, execution.Id);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionsAsyncRequiresActiveExecution()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>());

        var result = await _controller.UpdateTaskExecutionsAsync(1, 2, 40,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertObjectStatus(StatusCodes.Status400BadRequest, result.Result);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionsAsyncReturnsNotFound()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.UpdateTaskExecutionAsync(Arg.Any<IncidentResponsePlanTaskExecution>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanTaskExecution>>(_ => throw new DataNotFoundException("exec", "40"));

        var result = await _controller.UpdateTaskExecutionsAsync(1, 2, 40,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionsAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active)
        });
        _plansService.UpdateTaskExecutionAsync(Arg.Any<IncidentResponsePlanTaskExecution>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanTaskExecution>>(_ => throw new Exception("nope"));

        var result = await _controller.UpdateTaskExecutionsAsync(1, 2, 40,
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 7 });

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------ DeleteTaskExecutionsAsync

    [Fact]
    public async Task TestDeleteTaskExecutionsAsync()
    {
        var result = await _controller.DeleteTaskExecutionsAsync(1, 2, 40);

        Assert.IsType<OkResult>(result);
        await _plansService.Received(1).DeleteTaskExecutionAsync(40);
    }

    [Fact]
    public async Task TestDeleteTaskExecutionsAsyncReturnsNotFound()
    {
        _plansService.DeleteTaskExecutionAsync(999).Returns(_ => throw new DataNotFoundException("exec", "999"));

        var result = await _controller.DeleteTaskExecutionsAsync(1, 2, 999);

        AssertStatus(StatusCodes.Status404NotFound, result);
    }

    [Fact]
    public async Task TestDeleteTaskExecutionsAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.DeleteTaskExecutionAsync(41).Returns(_ => throw new Exception("nope"));

        var result = await _controller.DeleteTaskExecutionsAsync(1, 2, 41);

        AssertStatus(StatusCodes.Status500InternalServerError, result);
    }

    // ----------------------------------------------- GetTaskExecutionsByIdAsync

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsync()
    {
        _plansService.GetTaskExecutionsByIdAsync(40).Returns(new List<IncidentResponsePlanTaskExecution>
        {
            new() { Id = 40, TaskId = 2, PlanExecutionId = 7 }
        });

        var result = await _controller.GetTaskExecutionsByIdAsync(1, 2, 40);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncidentResponsePlanTaskExecution>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(40, list[0].Id);
    }

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsyncReturnsNotFound()
    {
        _plansService.GetTaskExecutionsByIdAsync(999)
            .Returns<Task<List<IncidentResponsePlanTaskExecution>>>(
                _ => throw new DataNotFoundException("exec", "999"));

        var result = await _controller.GetTaskExecutionsByIdAsync(1, 2, 999);

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetTaskExecutionsByIdAsync(41)
            .Returns<Task<List<IncidentResponsePlanTaskExecution>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetTaskExecutionsByIdAsync(1, 2, 41);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsync()
    {
        _plansService.CreateAsync(Arg.Any<IncidentResponsePlan>(), Arg.Any<User>()).Returns(Plan(11, "New plan"));

        var result = await _controller.CreateAsync(Plan(0, "New plan"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var plan = Assert.IsType<IncidentResponsePlan>(created.Value);
        Assert.Equal(11, plan.Id);
        Assert.Equal("IncidentResponsePlan/11", created.Location);
    }

    [Fact]
    public async Task TestCreateAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.CreateAsync(Arg.Any<IncidentResponsePlan>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlan>>(_ => throw Wrapped());

        var result = await _controller.CreateAsync(Plan(0));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsync()
    {
        _plansService.UpdateAsync(Arg.Any<IncidentResponsePlan>(), Arg.Any<User>()).Returns(Plan(12, "Updated"));

        var result = await _controller.UpdateAsync(12, Plan(0, "Updated"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<IncidentResponsePlan>(ok.Value);
        Assert.Equal(12, plan.Id);

        await _plansService.Received(1).UpdateAsync(Arg.Is<IncidentResponsePlan>(p => p.Id == 12), Arg.Any<User>());
    }

    [Fact]
    public async Task TestUpdateAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.UpdateAsync(Arg.Any<IncidentResponsePlan>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlan>>(_ => throw Wrapped());

        var result = await _controller.UpdateAsync(12, Plan(0));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // -------------------------------------------------------------- DeleteAsync

    [Fact]
    public async Task TestDeleteAsync()
    {
        var result = await _controller.DeleteAsync(13);

        Assert.IsType<OkResult>(result);
        await _plansService.Received(1).DeleteAsync(13, Arg.Any<User>());
    }

    [Fact]
    public async Task TestDeleteAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.DeleteAsync(14, Arg.Any<User>()).Returns(_ => throw Wrapped());

        var result = await _controller.DeleteAsync(14);

        AssertStatus(StatusCodes.Status500InternalServerError, result);
    }

    // -------------------------------------------------- GetExecutionsByIdAsync

    [Fact]
    public async Task TestGetExecutionsByIdAsync()
    {
        _plansService.GetExecutionsByPlanIdAsync(1).Returns(new List<IncidentResponsePlanExecution>
        {
            Execution(7, 1, (int)IntStatus.Active),
            Execution(8, 1, (int)IntStatus.Closed)
        });

        var result = await _controller.GetExecutionsByIdAsync(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncidentResponsePlanExecution>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetExecutionsByIdAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetExecutionsByPlanIdAsync(2)
            .Returns<Task<List<IncidentResponsePlanExecution>>>(_ => throw new Exception("nope"));

        var result = await _controller.GetExecutionsByIdAsync(2);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------- CreatePlanExecutionAsync

    [Fact]
    public async Task TestCreatePlanExecutionAsync()
    {
        _plansService.GetByIdAsync(1, includeActivatedBy: true).Returns(PlanWithIncident(1));
        _plansService.CreateExecutionAsync(
                Arg.Any<IncidentResponsePlanExecution>(), Arg.Any<Incident>(), Arg.Any<User>())
            .Returns(Execution(20, 1, (int)IntStatus.Active));

        var result = await _controller.CreatePlanExecutionAsync(1, Execution(0, 0, (int)IntStatus.Active));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var execution = Assert.IsType<IncidentResponsePlanExecution>(ok.Value);
        Assert.Equal(20, execution.Id);

        await _plansService.Received(1).CreateExecutionAsync(
            Arg.Is<IncidentResponsePlanExecution>(e => e.PlanId == 1),
            Arg.Is<Incident>(i => i.Id == 31),
            Arg.Any<User>());
    }

    [Fact]
    public async Task TestCreatePlanExecutionAsyncRequiresIncident()
    {
        _plansService.GetByIdAsync(1, includeActivatedBy: true).Returns(Plan(1));

        var result = await _controller.CreatePlanExecutionAsync(1, Execution(0, 0, (int)IntStatus.Active));

        AssertObjectStatus(StatusCodes.Status400BadRequest, result.Result);
    }

    [Fact]
    public async Task TestCreatePlanExecutionAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.GetByIdAsync(1, includeActivatedBy: true)
            .Returns<Task<IncidentResponsePlan>>(_ => throw new Exception("nope"));

        var result = await _controller.CreatePlanExecutionAsync(1, Execution(0, 0, (int)IntStatus.Active));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------- UpdatePlanExecutionAsync

    [Fact]
    public async Task TestUpdatePlanExecutionAsync()
    {
        _plansService.UpdateExecutionAsync(Arg.Any<IncidentResponsePlanExecution>(), Arg.Any<User>())
            .Returns(Execution(20, 1, (int)IntStatus.Active));

        var result = await _controller.UpdatePlanExecutionAsync(1, 20, Execution(0, 0, (int)IntStatus.Active));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var execution = Assert.IsType<IncidentResponsePlanExecution>(ok.Value);
        Assert.Equal(20, execution.Id);

        await _plansService.Received(1).UpdateExecutionAsync(
            Arg.Is<IncidentResponsePlanExecution>(e => e.PlanId == 1 && e.Id == 20), Arg.Any<User>());
    }

    [Fact]
    public async Task TestUpdatePlanExecutionAsyncReturnsNotFound()
    {
        _plansService.UpdateExecutionAsync(Arg.Any<IncidentResponsePlanExecution>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanExecution>>(_ => throw new DataNotFoundException("exec", "20"));

        var result = await _controller.UpdatePlanExecutionAsync(1, 20, Execution(0, 0, (int)IntStatus.Active));

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestUpdatePlanExecutionAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.UpdateExecutionAsync(Arg.Any<IncidentResponsePlanExecution>(), Arg.Any<User>())
            .Returns<Task<IncidentResponsePlanExecution>>(_ => throw new Exception("nope"));

        var result = await _controller.UpdatePlanExecutionAsync(1, 20, Execution(0, 0, (int)IntStatus.Active));

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }

    // ------------------------------------------------- DeletePlanExecutionAsync

    [Fact]
    public async Task TestDeletePlanExecutionAsync()
    {
        var result = await _controller.DeletePlanExecutionAsync(1, 20);

        Assert.IsType<OkResult>(result.Result);
        await _plansService.Received(1).DeleteExecutionAsync(20);
    }

    [Fact]
    public async Task TestDeletePlanExecutionAsyncReturnsNotFound()
    {
        _plansService.DeleteExecutionAsync(999).Returns(_ => throw new DataNotFoundException("exec", "999"));

        var result = await _controller.DeletePlanExecutionAsync(1, 999);

        AssertStatus(StatusCodes.Status404NotFound, result.Result);
    }

    [Fact]
    public async Task TestDeletePlanExecutionAsyncReturnsInternalServerErrorOnFailure()
    {
        _plansService.DeleteExecutionAsync(21).Returns(_ => throw new Exception("nope"));

        var result = await _controller.DeletePlanExecutionAsync(1, 21);

        AssertStatus(StatusCodes.Status500InternalServerError, result.Result);
    }
}
