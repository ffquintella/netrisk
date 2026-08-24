using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using Model.IncidentResponsePlan;
using Model.Rest;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// The parts of <see cref="IncidentResponsePlansRestService"/> that
/// <c>IncidentResponsePlansRestServiceTests</c> cannot reach: the methods its shared mock has no
/// route for (approved plans, sorted tasks, attachments, schedule, dependencies, blocked-task
/// override) and the error branch of every method.
///
/// RestSharp 114 with <c>ThrowOnAnyError = false</c> classifies a response before the service sees
/// it: a 2xx or a 404 stays <c>ResponseStatus.Completed</c> and is handed over intact, while any
/// other failing status becomes <c>ResponseStatus.Error</c> carrying an
/// <see cref="System.Net.Http.HttpRequestException"/> that the verb extension throws. So an
/// "unexpected status" branch is driven with a 404 (or a 2xx the method does not expect) plus the
/// error body the API would send, and a communication branch with 500.
/// </summary>
[TestSubject(typeof(IncidentResponsePlansRestService))]
public class IncidentResponsePlansRestServiceStubTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIncidentResponsePlansService _service;

    public IncidentResponsePlansRestServiceStubTest()
    {
        _service = ResolveWith<IIncidentResponsePlansService>(_backend);
    }

    private static readonly DateTime Anchor = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private static IncidentResponsePlan Plan(int id = 1, string name = "Containment") => new()
    {
        Id = id,
        Name = name,
        Description = "How we contain it",
        CreationDate = Anchor,
        LastUpdate = Anchor,
        CreatedById = 3,
        Status = 1,
        HasBeenApproved = true
    };

    private static IncidentResponsePlanTask PlanTask(int id, int planId, string name, int priority = 0) => new()
    {
        Id = id,
        PlanId = planId,
        Name = name,
        Description = "Step",
        CreationDate = Anchor,
        Status = 0,
        ExecutionOrder = 1,
        AssignedToId = 2,
        Priority = priority,
        IsOptional = false,
        IsSequential = false,
        IsParallel = false
    };

    private static IncidentResponsePlanExecution Execution(int id, int planId) => new()
    {
        Id = id,
        PlanId = planId,
        Status = 1,
        CreatedById = 3,
        LastUpdatedById = 3,
        ExecutionTrigger = "Manual",
        ExecutionResult = "Contained",
        Duration = TimeSpan.FromMinutes(30),
        ExecutionDate = Anchor,
        CreationDate = Anchor
    };

    private static IncidentResponsePlanTaskExecution TaskExecution(int id, int taskId) => new()
    {
        Id = id,
        TaskId = taskId,
        PlanExecutionId = 5,
        Status = 2,
        Notes = "done",
        Duration = TimeSpan.FromMinutes(4),
        ExecutionDate = Anchor,
        CreatedAt = Anchor,
        LastUpdatedAt = null
    };

    private static OperationError Failure() => new()
    {
        Title = "The server refused",
        Status = 422,
        Errors = new Dictionary<string, string[]> { ["Name"] = ["is required"] }
    };

    private static List<FileListing> Attachments() =>
    [
        new() { Name = "runbook.pdf", UniqueName = "aaa-runbook.pdf", Type = "pdf", Timestamp = Anchor, OwnerId = 1 },
        new() { Name = "log.txt", UniqueName = "bbb-log.txt", Type = "txt", Timestamp = Anchor, OwnerId = 1 }
    ];

    // ---------------------------------------------------------------- GetAllAsync (error branches)

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());

        Assert.Equal("/IncidentResponsePlans", ex.Url);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/IncidentResponsePlans");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    // ---------------------------------------------------------------- GetAllApprovedAsync

    [Fact]
    public async Task TestGetAllApprovedAsync()
    {
        _backend.OnGet("/IncidentResponsePlans/Approved",
            new List<IncidentResponsePlan> { Plan(), Plan(2, "Eradication") });

        var plans = await _service.GetAllApprovedAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("Containment", plans[0].Name);
        Assert.Equal("Eradication", plans[1].Name);
        Assert.True(plans[0].HasBeenApproved);
        Assert.Equal("GET /IncidentResponsePlans/Approved", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllApprovedAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/Approved", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllApprovedAsync());

        Assert.Equal("/IncidentResponsePlans/Approved", ex.Url);
    }

    [Fact]
    public async Task TestGetAllApprovedAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/Approved", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllApprovedAsync());
    }

    // ---------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsyncPostsThePlanAndReturnsTheSavedOne()
    {
        _backend.OnPost("/IncidentResponsePlans", Plan(14, "New plan"), HttpStatusCode.Created);

        var created = await _service.CreateAsync(Plan(0, "New plan"));

        Assert.Equal(14, created.Id);
        Assert.Equal("POST /IncidentResponsePlans", _backend.LastRequest.ToString());
        Assert.Contains("\"name\":\"New plan\"", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IncidentResponsePlans", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.CreateAsync(Plan(0)));

        Assert.Equal("The server refused", ex.Result.Title);
        Assert.Equal(422, ex.Result.Status);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWhenTheCreatedPlanCannotBeRead()
    {
        // 201 with a JSON null body: the status check passes and the deserialization returns null.
        _backend.OnPost("/IncidentResponsePlans", "null", HttpStatusCode.Created);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.CreateAsync(Plan(0)));

        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestCreateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(Plan(0)));
    }

    // ---------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsyncPutsToThePlanRoute()
    {
        _backend.OnPut("/IncidentResponsePlans/6", Plan(6, "Renamed"));

        var updated = await _service.UpdateAsync(Plan(6, "Renamed"));

        Assert.Equal(6, updated.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("PUT /IncidentResponsePlans/6", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IncidentResponsePlans/6", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.UpdateAsync(Plan(6)));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWhenTheUpdatedPlanCannotBeRead()
    {
        _backend.OnPut("/IncidentResponsePlans/6", "null");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.UpdateAsync(Plan(6)));

        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IncidentResponsePlans/6", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(Plan(6)));
    }

    // ---------------------------------------------------------------- DeleteAsync

    [Fact]
    public async Task TestDeleteAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnDelete("/IncidentResponsePlans/6", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.DeleteAsync(6));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/6", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(6));
    }

    // ---------------------------------------------------------------- GetByIdAsync

    [Fact]
    public async Task TestGetByIdAsyncAsksForTheTasksWhenRequested()
    {
        var plan = Plan(7);
        plan.Tasks = [PlanTask(1, 7, "Isolate")];
        _backend.OnGet("/IncidentResponsePlans/7", plan);

        var result = await _service.GetByIdAsync(7, includeTasks: true);

        Assert.Equal(7, result.Id);
        Assert.Single(result.Tasks);
        Assert.Equal("?includeTasks=true", _backend.LastRequest.Query);
        Assert.Equal("GET /IncidentResponsePlans/7?includeTasks=true", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetByIdAsyncDoesNotAskForTheTasksByDefault()
    {
        _backend.OnGet("/IncidentResponsePlans/7", Plan(7));

        var result = await _service.GetByIdAsync(7);

        Assert.Equal(7, result.Id);
        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/7", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetByIdAsync(7));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenThePlanCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/7", "null");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetByIdAsync(7));

        Assert.Equal("/IncidentResponsePlans/7", ex.Url);
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/7", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(7));
    }

    // ---------------------------------------------------------------- GetTasksByPlanIdAsync

    [Fact]
    public async Task TestGetTasksByPlanIdAsyncSortsTheTasks()
    {
        // The service runs the tasks through TaskSorter: priority descending, then name.
        _backend.OnGet("/IncidentResponsePlans/1/Tasks", new List<IncidentResponsePlanTask>
        {
            PlanTask(1, 1, "Alpha", priority: 1),
            PlanTask(2, 1, "Charlie", priority: 5),
            PlanTask(3, 1, "Bravo", priority: 5)
        });

        var tasks = await _service.GetTasksByPlanIdAsync(1);

        Assert.Equal(3, tasks.Count);
        Assert.Equal("Bravo", tasks[0].Name);
        Assert.Equal("Charlie", tasks[1].Name);
        Assert.Equal("Alpha", tasks[2].Name);
        Assert.Equal("GET /IncidentResponsePlans/1/Tasks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetTasksByPlanIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetTasksByPlanIdAsync(1));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetTasksByPlanIdAsyncThrowsWhenTheTasksCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks", "null");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTasksByPlanIdAsync(1));

        Assert.Equal("/IncidentResponsePlans/1/Tasks", ex.Url);
    }

    [Fact]
    public async Task TestGetTasksByPlanIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Tasks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetTasksByPlanIdAsync(1));
    }

    // ---------------------------------------------------------------- CreateTaskAsync

    [Fact]
    public async Task TestCreateTaskAsyncPostsToThePlanTasksRoute()
    {
        _backend.OnPost("/IncidentResponsePlans/4/Tasks", PlanTask(22, 4, "Isolate"), HttpStatusCode.Created);

        var created = await _service.CreateTaskAsync(PlanTask(0, 4, "Isolate"));

        Assert.Equal(22, created.Id);
        Assert.Equal("Isolate", created.Name);
        Assert.Equal("POST /IncidentResponsePlans/4/Tasks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestCreateTaskAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IncidentResponsePlans/4/Tasks", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.CreateTaskAsync(PlanTask(0, 4, "Isolate")));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestCreateTaskAsyncThrowsWhenTheCreatedTaskCannotBeRead()
    {
        _backend.OnPost("/IncidentResponsePlans/4/Tasks", "null", HttpStatusCode.Created);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateTaskAsync(PlanTask(0, 4, "Isolate")));

        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestCreateTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/4/Tasks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateTaskAsync(PlanTask(0, 4, "Isolate")));
    }

    // ---------------------------------------------------------------- UpdateTaskAsync

    [Fact]
    public async Task TestUpdateTaskAsyncDropsThePlanNavigationBeforeSending()
    {
        var task = PlanTask(9, 4, "Isolate");
        task.Plan = Plan(4);
        _backend.OnPut("/IncidentResponsePlans/4/Tasks/9", PlanTask(9, 4, "Isolate"));

        var updated = await _service.UpdateTaskAsync(task);

        Assert.Equal(9, updated.Id);
        Assert.Equal("PUT /IncidentResponsePlans/4/Tasks/9", _backend.LastRequest.ToString());
        // Posting the plan back with the task would make the server re-key rows it already owns.
        Assert.Null(task.Plan);
        Assert.Contains("\"plan\":null", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IncidentResponsePlans/4/Tasks/9", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.UpdateTaskAsync(PlanTask(9, 4, "Isolate")));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncThrowsWhenTheUpdatedTaskCannotBeRead()
    {
        _backend.OnPut("/IncidentResponsePlans/4/Tasks/9", "null");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.UpdateTaskAsync(PlanTask(9, 4, "Isolate")));

        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IncidentResponsePlans/4/Tasks/9", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UpdateTaskAsync(PlanTask(9, 4, "Isolate")));
    }

    // ---------------------------------------------------------------- GetTaskByIdAsync

    [Fact]
    public async Task TestGetTaskByIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetTaskByIdAsync(1, 2));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetTaskByIdAsyncThrowsWhenTheTaskCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTaskByIdAsync(1, 2));
    }

    [Fact]
    public async Task TestGetTaskByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Tasks/2", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetTaskByIdAsync(1, 2));
    }

    // ---------------------------------------------------------------- DeleteTaskAsync

    [Fact]
    public async Task TestDeleteTaskAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnDelete("/IncidentResponsePlans/1/Tasks/2", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.DeleteTaskAsync(1, 2));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestDeleteTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Tasks/2", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteTaskAsync(1, 2));
    }

    // ---------------------------------------------------------------- GetTaskExecutionsByIdAsync

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Executions", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetTaskExecutionsByIdAsync(1, 2));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsyncThrowsWhenTheExecutionsCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Executions", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTaskExecutionsByIdAsync(1, 2));
    }

    [Fact]
    public async Task TestGetTaskExecutionsByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Tasks/2/Executions",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetTaskExecutionsByIdAsync(1, 2));
    }

    // ---------------------------------------------------------------- GetExecutionByTaskIdAsync

    [Fact]
    public async Task TestGetExecutionByTaskIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetExecutionByTaskIdAsync(1, 2, 3));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetExecutionByTaskIdAsyncThrowsWhenTheExecutionCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Executions/3", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetExecutionByTaskIdAsync(1, 2, 3));
    }

    [Fact]
    public async Task TestGetExecutionByTaskIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Tasks/2/Executions/3",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetExecutionByTaskIdAsync(1, 2, 3));
    }

    // ---------------------------------------------------------------- GetExecutionsByPlanIdAsync

    [Fact]
    public async Task TestGetExecutionsByPlanIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Executions", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetExecutionsByPlanIdAsync(1));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetExecutionsByPlanIdAsyncThrowsWhenTheExecutionsCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Executions", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetExecutionsByPlanIdAsync(1));
    }

    [Fact]
    public async Task TestGetExecutionsByPlanIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Executions", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetExecutionsByPlanIdAsync(1));
    }

    // ---------------------------------------------------------------- GetExecutionByIdAsync

    [Fact]
    public async Task TestGetExecutionByIdAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetExecutionByIdAsync(1, 3));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetExecutionByIdAsyncThrowsWhenTheExecutionCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Executions/3", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetExecutionByIdAsync(1, 3));
    }

    [Fact]
    public async Task TestGetExecutionByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Executions/3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetExecutionByIdAsync(1, 3));
    }

    // ---------------------------------------------------------------- CreateExecutionAsync

    [Fact]
    public async Task TestCreateExecutionAsyncSendsAZeroIdSoTheServerKeysTheRow()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Executions", Execution(31, 1), HttpStatusCode.Created);

        var execution = Execution(99, 1);
        var created = await _service.CreateExecutionAsync(execution);

        Assert.Equal(31, created.Id);
        Assert.Equal("Contained", created.ExecutionResult);
        Assert.Equal("POST /IncidentResponsePlans/1/Executions", _backend.LastRequest.ToString());
        Assert.Contains("\"id\":0", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Executions", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.CreateExecutionAsync(Execution(0, 1)));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestCreateExecutionAsyncThrowsWhenTheCreatedExecutionCannotBeRead()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Executions", "null", HttpStatusCode.Created);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateExecutionAsync(Execution(0, 1)));
    }

    [Fact]
    public async Task TestCreateExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Executions", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateExecutionAsync(Execution(0, 1)));
    }

    // ---------------------------------------------------------------- CreateTaskExecutionAsync

    [Fact]
    public async Task TestCreateTaskExecutionAsyncSendsAZeroIdSoTheServerKeysTheRow()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Executions", TaskExecution(41, 2), HttpStatusCode.Created);

        var created = await _service.CreateTaskExecutionAsync(1, TaskExecution(99, 2));

        Assert.Equal(41, created.Id);
        Assert.Equal("done", created.Notes);
        Assert.Equal("POST /IncidentResponsePlans/1/Tasks/2/Executions", _backend.LastRequest.ToString());
        Assert.Contains("\"id\":0", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateTaskExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Executions", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.CreateTaskExecutionAsync(1, TaskExecution(0, 2)));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestCreateTaskExecutionAsyncThrowsWhenTheCreatedExecutionCannotBeRead()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Executions", "null", HttpStatusCode.Created);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateTaskExecutionAsync(1, TaskExecution(0, 2)));
    }

    [Fact]
    public async Task TestCreateTaskExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Tasks/2/Executions",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateTaskExecutionAsync(1, TaskExecution(0, 2)));
    }

    // ---------------------------------------------------------------- UpdateExecutionAsync

    [Fact]
    public async Task TestUpdateExecutionAsyncPutsToTheExecutionRoute()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Executions/3", Execution(3, 1));

        var updated = await _service.UpdateExecutionAsync(Execution(3, 1));

        Assert.Equal(3, updated.Id);
        Assert.Equal("PUT /IncidentResponsePlans/1/Executions/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestUpdateExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.UpdateExecutionAsync(Execution(3, 1)));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateExecutionAsyncThrowsWhenTheUpdatedExecutionCannotBeRead()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Executions/3", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.UpdateExecutionAsync(Execution(3, 1)));
    }

    [Fact]
    public async Task TestUpdateExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IncidentResponsePlans/1/Executions/3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UpdateExecutionAsync(Execution(3, 1)));
    }

    // ---------------------------------------------------------------- UpdateTaskExecutionAsync

    [Fact]
    public async Task TestUpdateTaskExecutionAsyncPutsToTheTaskExecutionRoute()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Tasks/2/Executions/3", TaskExecution(3, 2));

        var updated = await _service.UpdateTaskExecutionAsync(1, TaskExecution(3, 2));

        Assert.Equal(3, updated.Id);
        Assert.Equal(2, updated.TaskId);
        Assert.Equal("PUT /IncidentResponsePlans/1/Tasks/2/Executions/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestUpdateTaskExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Tasks/2/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(
            () => _service.UpdateTaskExecutionAsync(1, TaskExecution(3, 2)));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateTaskExecutionAsyncThrowsWhenTheUpdatedExecutionCannotBeRead()
    {
        _backend.OnPut("/IncidentResponsePlans/1/Tasks/2/Executions/3", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.UpdateTaskExecutionAsync(1, TaskExecution(3, 2)));
    }

    [Fact]
    public async Task TestUpdateTaskExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IncidentResponsePlans/1/Tasks/2/Executions/3",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UpdateTaskExecutionAsync(1, TaskExecution(3, 2)));
    }

    // ---------------------------------------------------------------- DeleteExecutionAsync

    [Fact]
    public async Task TestDeleteExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnDelete("/IncidentResponsePlans/1/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.DeleteExecutionAsync(1, 3));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestDeleteExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Executions/3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteExecutionAsync(1, 3));
    }

    // ---------------------------------------------------------------- DeleteTaskExecutionAsync

    [Fact]
    public async Task TestDeleteTaskExecutionAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnDelete("/IncidentResponsePlans/1/Tasks/2/Executions/3", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.DeleteTaskExecutionAsync(1, 2, 3));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestDeleteTaskExecutionAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Tasks/2/Executions/3",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteTaskExecutionAsync(1, 2, 3));
    }

    // ---------------------------------------------------------------- GetAttachmentsAsync

    [Fact]
    public async Task TestGetAttachmentsAsync()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Attachments", Attachments());

        var attachments = await _service.GetAttachmentsAsync(1);

        Assert.Equal(2, attachments.Count);
        Assert.Equal("runbook.pdf", attachments[0].Name);
        Assert.Equal("aaa-runbook.pdf", attachments[0].UniqueName);
        Assert.Equal(1, attachments[1].OwnerId);
        Assert.Equal("GET /IncidentResponsePlans/1/Attachments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAttachmentsAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Attachments", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetAttachmentsAsync(1));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetAttachmentsAsyncThrowsWhenTheListCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Attachments", "null");

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAttachmentsAsync(1));
    }

    [Fact]
    public async Task TestGetAttachmentsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Attachments", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAttachmentsAsync(1));
    }

    // ---------------------------------------------------------------- GetTaskAttachmentsAsync

    [Fact]
    public async Task TestGetTaskAttachmentsAsync()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Attachments", Attachments());

        var attachments = await _service.GetTaskAttachmentsAsync(1, 2);

        Assert.Equal(2, attachments.Count);
        Assert.Equal("log.txt", attachments[1].Name);
        Assert.Equal("txt", attachments[1].Type);
        Assert.Equal("GET /IncidentResponsePlans/1/Tasks/2/Attachments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetTaskAttachmentsAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Attachments", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.GetTaskAttachmentsAsync(1, 2));

        Assert.Equal("The server refused", ex.Result.Title);
    }

    [Fact]
    public async Task TestGetTaskAttachmentsAsyncThrowsWhenTheListCannotBeRead()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Tasks/2/Attachments", "null");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTaskAttachmentsAsync(1, 2));

        Assert.Equal("/IncidentResponsePlans/1/Tasks/2/Attachments", ex.Url);
    }

    [Fact]
    public async Task TestGetTaskAttachmentsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Tasks/2/Attachments",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetTaskAttachmentsAsync(1, 2));
    }

    // ---------------------------------------------------------------- GetScheduleAsync

    [Fact]
    public async Task TestGetScheduleAsync()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Schedule", new IrpSchedule
        {
            PlanId = 1,
            PlanName = "Containment",
            PlanStart = Anchor,
            PlanEnd = Anchor.AddHours(4),
            TotalDuration = TimeSpan.FromHours(4),
            CriticalPath = [1, 2],
            Items =
            [
                new IrpScheduleItem
                {
                    TaskId = 1, Name = "Isolate", ExecutionOrder = 1, Duration = TimeSpan.FromHours(1),
                    EarlyStart = TimeSpan.Zero, EarlyFinish = TimeSpan.FromHours(1), IsCritical = true,
                    StartDate = Anchor, EndDate = Anchor.AddHours(1)
                },
                new IrpScheduleItem
                {
                    TaskId = 2, Name = "Eradicate", ExecutionOrder = 2, Duration = TimeSpan.FromHours(3),
                    EarlyStart = TimeSpan.FromHours(1), EarlyFinish = TimeSpan.FromHours(4), IsCritical = true,
                    DependsOn = [1], IsBlocked = true, StartDate = Anchor.AddHours(1), EndDate = Anchor.AddHours(4)
                }
            ]
        });

        var schedule = await _service.GetScheduleAsync(1);

        Assert.Equal(1, schedule.PlanId);
        Assert.Equal("Containment", schedule.PlanName);
        Assert.Equal(TimeSpan.FromHours(4), schedule.TotalDuration);
        Assert.Equal(2, schedule.CriticalPath.Count);
        Assert.Equal(1, schedule.CriticalPath[0]);
        Assert.Equal(2, schedule.CriticalPath[1]);
        Assert.Equal(2, schedule.Items.Count);
        Assert.Equal("Eradicate", schedule.Items[1].Name);
        Assert.Equal(1, Assert.Single(schedule.Items[1].DependsOn));
        Assert.True(schedule.Items[1].IsBlocked);
        Assert.Equal("GET /IncidentResponsePlans/1/Schedule", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetScheduleAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Schedule", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetScheduleAsync(1));

        Assert.Equal("/IncidentResponsePlans/1/Schedule", ex.Url);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public async Task TestGetScheduleAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Schedule", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetScheduleAsync(1));
    }

    // ---------------------------------------------------------------- GetDependenciesAsync

    [Fact]
    public async Task TestGetDependenciesAsync()
    {
        _backend.OnGet("/IncidentResponsePlans/1/Dependencies", new List<IrpTaskDependency>
        {
            new()
            {
                Id = 1, TaskId = 2, TaskName = "Eradicate", DependsOnTaskId = 1,
                DependsOnTaskName = "Isolate", CreatedAt = Anchor
            }
        });

        var dependencies = await _service.GetDependenciesAsync(1);

        Assert.Single(dependencies);
        Assert.Equal(2, dependencies[0].TaskId);
        Assert.Equal(1, dependencies[0].DependsOnTaskId);
        Assert.Equal("Isolate", dependencies[0].DependsOnTaskName);
        Assert.Equal("GET /IncidentResponsePlans/1/Dependencies", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetDependenciesAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Dependencies", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetDependenciesAsync(1));

        Assert.Equal("/IncidentResponsePlans/1/Dependencies", ex.Url);
    }

    [Fact]
    public async Task TestGetDependenciesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IncidentResponsePlans/1/Dependencies", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetDependenciesAsync(1));
    }

    // ---------------------------------------------------------------- AddDependencyAsync

    [Fact]
    public async Task TestAddDependencyAsync()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Dependencies/1", new IrpTaskDependency
        {
            Id = 8, TaskId = 2, TaskName = "Eradicate", DependsOnTaskId = 1,
            DependsOnTaskName = "Isolate", CreatedAt = Anchor
        }, HttpStatusCode.Created);

        var dependency = await _service.AddDependencyAsync(1, 2, 1);

        Assert.Equal(8, dependency.Id);
        Assert.Equal(2, dependency.TaskId);
        Assert.Equal(1, dependency.DependsOnTaskId);
        Assert.Equal("POST /IncidentResponsePlans/1/Tasks/2/Dependencies/1", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestAddDependencyAsyncThrowsOnAnUnexpectedStatus()
    {
        // 200 instead of 201 — the created edge is not there to return.
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Dependencies/1", new IrpTaskDependency { Id = 8 });

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.AddDependencyAsync(1, 2, 1));

        Assert.Equal("/IncidentResponsePlans/1/Tasks/2/Dependencies/1", ex.Url);
        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestAddDependencyAsyncReportsARefusedEdgeAsACommunicationFailure()
    {
        // KNOWN LIMITATION, not the intent of the production code: the method means to translate a
        // 400 into a RuleBrokenException carrying the server's explanation ("cycle", "cross-plan"),
        // but RestSharp classifies a 400 as ResponseStatus.Error and PostAsync throws
        // HttpRequestException before the status check runs, so the catch below wins and the reason
        // never reaches the caller. Asserted as-is so the behaviour change is visible when fixed.
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/Dependencies/1",
            "Adding this dependency would close a cycle", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.AddDependencyAsync(1, 2, 1));
    }

    [Fact]
    public async Task TestAddDependencyAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Tasks/2/Dependencies/1",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.AddDependencyAsync(1, 2, 1));
    }

    // ---------------------------------------------------------------- RemoveDependencyAsync

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task TestRemoveDependencyAsyncAcceptsBothSuccessShapes(HttpStatusCode status)
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Tasks/2/Dependencies/1", status);

        await _service.RemoveDependencyAsync(1, 2, 1);

        Assert.Equal("DELETE /IncidentResponsePlans/1/Tasks/2/Dependencies/1", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestRemoveDependencyAsyncThrowsOnAnUnexpectedStatus()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Tasks/2/Dependencies/1", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.RemoveDependencyAsync(1, 2, 1));

        Assert.Equal("/IncidentResponsePlans/1/Tasks/2/Dependencies/1", ex.Url);
        Assert.Equal("DELETE", ex.Method);
    }

    [Fact]
    public async Task TestRemoveDependencyAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IncidentResponsePlans/1/Tasks/2/Dependencies/1",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RemoveDependencyAsync(1, 2, 1));
    }

    // ---------------------------------------------------------------- CompleteBlockedTaskAsync

    [Fact]
    public async Task TestCompleteBlockedTaskAsyncSendsTheReasonAndReturnsTheReplacedBar()
    {
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/CompleteWithOverride", new IrpScheduleItem
        {
            TaskId = 2, Name = "Eradicate", Status = 3, IsBlocked = false, IsCritical = true,
            Duration = TimeSpan.FromHours(3), StartDate = Anchor, EndDate = Anchor.AddHours(3)
        });

        var item = await _service.CompleteBlockedTaskAsync(1, 2, "Vendor confirmed the box was already off");

        Assert.Equal(2, item.TaskId);
        Assert.Equal(3, item.Status);
        Assert.False(item.IsBlocked);
        Assert.Equal("POST /IncidentResponsePlans/1/Tasks/2/CompleteWithOverride",
            _backend.LastRequest.ToString());
        Assert.Contains("Vendor confirmed the box was already off", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCompleteBlockedTaskAsyncReportsARefusedOverrideAsACommunicationFailure()
    {
        // Same KNOWN LIMITATION as AddDependencyAsync: the RuleBrokenException branch for a 400 is
        // unreachable because RestSharp throws HttpRequestException on that status first, so the
        // server's explanation ("an override reason is required") is lost.
        _backend.OnPost("/IncidentResponsePlans/1/Tasks/2/CompleteWithOverride",
            "An override reason is required", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CompleteBlockedTaskAsync(1, 2, ""));
    }

    [Fact]
    public async Task TestCompleteBlockedTaskAsyncThrowsOnAnUnexpectedStatus()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Tasks/2/CompleteWithOverride",
            HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CompleteBlockedTaskAsync(1, 2, "reason"));

        Assert.Equal("/IncidentResponsePlans/1/Tasks/2/CompleteWithOverride", ex.Url);
        Assert.Equal("POST", ex.Method);
    }

    [Fact]
    public async Task TestCompleteBlockedTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Tasks/2/CompleteWithOverride",
            HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CompleteBlockedTaskAsync(1, 2, "reason"));
    }
}
