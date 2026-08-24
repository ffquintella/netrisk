using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Rest;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers <see cref="IrpTemplatesRestService"/> over <see cref="StubRestBackend"/>.
///
/// Two RestSharp facts shape the error tests, both verified against RestSharp 114 with
/// <c>ThrowOnAnyError = false</c>:
/// <list type="bullet">
/// <item>a 2xx or a 404 leaves the response <c>Completed</c>, so RestSharp hands it to the service
/// and the service's own status check decides;</item>
/// <item>any other failing status becomes <c>ResponseStatus.Error</c> with an
/// <see cref="System.Net.Http.HttpRequestException"/>, which RestSharp throws out of the verb
/// extension — that is what drives the <c>catch</c> branches.</item>
/// </list>
/// So an "unexpected status" branch is exercised with a status RestSharp considers legitimate
/// (404, or a 2xx the method does not expect), and a communication branch with 500.
/// </summary>
[TestSubject(typeof(IrpTemplatesRestService))]
public class IrpTemplatesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIrpTemplatesService _service;

    public IrpTemplatesRestServiceTest()
    {
        _service = ResolveWith<IIrpTemplatesService>(_backend);
    }

    private static IrpTemplate Template(int id = 1, string name = "Ransomware") => new()
    {
        Id = id,
        Name = name,
        Description = "Playbook",
        MatchingRulesJson = "{\"category\":\"malware\"}",
        IsEnabled = true
    };

    private static IrpTemplateTask TemplateTask(int id = 1, int templateId = 1, string title = "Isolate host") => new()
    {
        Id = id,
        IrpTemplateId = templateId,
        Title = title,
        InstructionsMarkdown = "# Pull the cable",
        AssigneeRuleJson = "{\"role\":\"SOC\"}",
        DueOffsetSeconds = 900,
        PredecessorTaskId = null,
        RequiresConfirmation = true
    };

    private static OperationError Failure() => new()
    {
        Title = "Template rejected",
        Status = 422,
        Errors = new Dictionary<string, string[]> { ["Name"] = ["is already taken"] }
    };

    // ---------------------------------------------------------------- GetAllAsync

    [Fact]
    public async Task TestGetAllAsync()
    {
        _backend.OnGet("/IrpTemplates", new List<IrpTemplate> { Template(), Template(2, "Phishing") });

        var templates = await _service.GetAllAsync();

        Assert.Equal(2, templates.Count);
        Assert.Equal("Ransomware", templates[0].Name);
        Assert.Equal("Phishing", templates[1].Name);
        Assert.True(templates[0].IsEnabled);
        Assert.Equal("GET /IrpTemplates", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());

        Assert.Equal("/IrpTemplates", ex.Url);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/IrpTemplates");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    // ---------------------------------------------------------------- GetByIdAsync

    [Fact]
    public async Task TestGetByIdAsync()
    {
        _backend.OnGet("/IrpTemplates/4", Template(4, "Data exfiltration"));

        var template = await _service.GetByIdAsync(4);

        Assert.Equal(4, template.Id);
        Assert.Equal("Data exfiltration", template.Name);
        Assert.Equal("{\"category\":\"malware\"}", template.MatchingRulesJson);
        Assert.Equal("GET /IrpTemplates/4", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenTheTemplateIsMissing()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates/99", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetByIdAsync(99));

        Assert.Equal("/IrpTemplates/99", ex.Url);
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates/4", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(4));
    }

    // ---------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsyncPostsTheFlatRequestAndReturnsTheSavedTemplate()
    {
        _backend.OnPost("/IrpTemplates", Template(11), HttpStatusCode.Created);

        var created = await _service.CreateAsync(Template(0, "Ransomware"));

        Assert.Equal(11, created.Id);
        Assert.Equal("POST /IrpTemplates", _backend.LastRequest.ToString());

        // The service posts a flat request rather than the entity, so the Tasks navigation must not
        // travel with it.
        var body = _backend.LastRequest.Body;
        Assert.Contains("\"name\":\"Ransomware\"", body);
        Assert.Contains("\"isEnabled\":true", body);
        Assert.DoesNotContain("tasks", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"id\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        // 200 instead of 201: RestSharp treats it as a legitimate answer, so the service's own
        // status check is what rejects it.
        _backend.OnPost("/IrpTemplates", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.CreateAsync(Template(0)));

        Assert.Equal("Template rejected", ex.Result.Title);
        Assert.Equal(422, ex.Result.Status);
    }

    [Fact]
    public async Task TestCreateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IrpTemplates", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(Template(0)));
    }

    // ---------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsyncPutsToTheTemplateRoute()
    {
        _backend.OnPut("/IrpTemplates/7", Template(7, "Renamed"));

        var updated = await _service.UpdateAsync(Template(7, "Renamed"));

        Assert.Equal(7, updated.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("PUT /IrpTemplates/7", _backend.LastRequest.ToString());
        Assert.Contains("\"name\":\"Renamed\"", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IrpTemplates/7", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.UpdateAsync(Template(7)));

        Assert.Equal("Template rejected", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IrpTemplates/7", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(Template(7)));
    }

    // ---------------------------------------------------------------- DeleteAsync

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task TestDeleteAsyncAcceptsBothSuccessShapes(HttpStatusCode status)
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3", status);

        await _service.DeleteAsync(3);

        Assert.Equal("DELETE /IrpTemplates/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestDeleteAsyncThrowsOnAnUnexpectedStatus()
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.DeleteAsync(3));

        Assert.Equal("/IrpTemplates/3", ex.Url);
        Assert.Equal("DELETE", ex.Method);
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(3));
    }

    // ---------------------------------------------------------------- CloneAsync

    [Fact]
    public async Task TestCloneAsync()
    {
        var clone = Template(21, "Ransomware (copy)");
        clone.IsEnabled = false;
        _backend.OnPost("/IrpTemplates/5/Clone", clone, HttpStatusCode.Created);

        var result = await _service.CloneAsync(5);

        Assert.Equal(21, result.Id);
        Assert.Equal("Ransomware (copy)", result.Name);
        Assert.False(result.IsEnabled);
        Assert.Equal("POST /IrpTemplates/5/Clone", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestCloneAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IrpTemplates/5/Clone", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.CloneAsync(5));

        Assert.Equal("Template rejected", ex.Result.Title);
    }

    [Fact]
    public async Task TestCloneAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IrpTemplates/5/Clone", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CloneAsync(5));
    }

    // ---------------------------------------------------------------- GetTasksAsync

    [Fact]
    public async Task TestGetTasksAsync()
    {
        _backend.OnGet("/IrpTemplates/2/Tasks",
            new List<IrpTemplateTask> { TemplateTask(1, 2), TemplateTask(2, 2, "Collect evidence") });

        var tasks = await _service.GetTasksAsync(2);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("Isolate host", tasks[0].Title);
        Assert.Equal(900, tasks[0].DueOffsetSeconds);
        Assert.True(tasks[0].RequiresConfirmation);
        Assert.Equal("GET /IrpTemplates/2/Tasks", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetTasksAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates/2/Tasks", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetTasksAsync(2));

        Assert.Equal("/IrpTemplates/2/Tasks", ex.Url);
    }

    [Fact]
    public async Task TestGetTasksAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/IrpTemplates/2/Tasks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetTasksAsync(2));
    }

    // ---------------------------------------------------------------- CreateTaskAsync

    [Fact]
    public async Task TestCreateTaskAsyncPostsTheFlatTaskRequest()
    {
        _backend.OnPost("/IrpTemplates/2/Tasks", TemplateTask(31, 2), HttpStatusCode.Created);

        var created = await _service.CreateTaskAsync(2, TemplateTask(0, 2));

        Assert.Equal(31, created.Id);
        Assert.Equal("Isolate host", created.Title);
        Assert.Equal("POST /IrpTemplates/2/Tasks", _backend.LastRequest.ToString());

        var body = _backend.LastRequest.Body;
        Assert.Contains("\"title\":\"Isolate host\"", body);
        Assert.Contains("\"dueOffsetSeconds\":900", body);
        Assert.Contains("\"requiresConfirmation\":true", body);
        // The template id travels in the route, so it is not part of the body.
        Assert.DoesNotContain("irpTemplateId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestCreateTaskAsyncThrowsWithTheServerErrorWhenTheStatusIsNotCreated()
    {
        _backend.OnPost("/IrpTemplates/2/Tasks", Failure());

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.CreateTaskAsync(2, TemplateTask(0, 2)));

        Assert.Equal("Template rejected", ex.Result.Title);
        Assert.Contains("Name", ex.Result.Errors.Keys);
    }

    [Fact]
    public async Task TestCreateTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/IrpTemplates/2/Tasks", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateTaskAsync(2, TemplateTask(0, 2)));
    }

    // ---------------------------------------------------------------- UpdateTaskAsync

    [Fact]
    public async Task TestUpdateTaskAsyncPutsToTheTaskRoute()
    {
        var task = TemplateTask(9, 3, "Notify legal");
        task.PredecessorTaskId = 8;
        _backend.OnPut("/IrpTemplates/3/Tasks/9", task);

        var updated = await _service.UpdateTaskAsync(3, task);

        Assert.Equal(9, updated.Id);
        Assert.Equal("Notify legal", updated.Title);
        Assert.Equal(8, updated.PredecessorTaskId);
        Assert.Equal("PUT /IrpTemplates/3/Tasks/9", _backend.LastRequest.ToString());
        Assert.Contains("\"predecessorTaskId\":8", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncThrowsWithTheServerErrorWhenTheStatusIsNotOk()
    {
        _backend.OnPut("/IrpTemplates/3/Tasks/9", Failure(), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.UpdateTaskAsync(3, TemplateTask(9, 3)));

        Assert.Equal("Template rejected", ex.Result.Title);
    }

    [Fact]
    public async Task TestUpdateTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/IrpTemplates/3/Tasks/9", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateTaskAsync(3, TemplateTask(9, 3)));
    }

    // ---------------------------------------------------------------- DeleteTaskAsync

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task TestDeleteTaskAsyncAcceptsBothSuccessShapes(HttpStatusCode status)
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3/Tasks/9", status);

        await _service.DeleteTaskAsync(3, 9);

        Assert.Equal("DELETE /IrpTemplates/3/Tasks/9", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestDeleteTaskAsyncThrowsOnAnUnexpectedStatus()
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3/Tasks/9", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.DeleteTaskAsync(3, 9));

        Assert.Equal("/IrpTemplates/3/Tasks/9", ex.Url);
        Assert.Equal("DELETE", ex.Method);
    }

    [Fact]
    public async Task TestDeleteTaskAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/IrpTemplates/3/Tasks/9", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteTaskAsync(3, 9));
    }
}
