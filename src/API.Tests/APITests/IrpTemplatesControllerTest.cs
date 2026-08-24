using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Covers the IRP template CRUD surface against the EF Core in-memory provider, since the
/// controller queries the database directly instead of going through a domain service.
/// </summary>
[TestSubject(typeof(IrpTemplatesController))]
public class IrpTemplatesControllerTest : BaseControllerTest
{
    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IrpTemplatesController _controller;

    // Template A: three tasks chained A -> B -> C.
    private readonly int _templateAId;
    private readonly int _taskAId;
    private readonly int _taskBId;
    private readonly int _taskCId;

    // Template B: no tasks.
    private readonly int _templateBId;

    // Template C: two tasks that point at each other, standing in for data that already holds a cycle.
    private readonly int _cyclicTemplateId;
    private readonly int _cyclicTaskXId;
    private readonly int _cyclicTaskYId;

    public IrpTemplatesControllerTest()
    {
        using (var ctx = _dal.GetContext())
        {
            var templateA = new IrpTemplate
            {
                Name = "Template A",
                Description = "First template",
                MatchingRulesJson = "{\"category\":\"phishing\"}",
                IsEnabled = true
            };
            var templateB = new IrpTemplate
            {
                Name = "Template B",
                MatchingRulesJson = "{}",
                IsEnabled = false
            };
            var cyclic = new IrpTemplate
            {
                Name = "Cyclic",
                MatchingRulesJson = "{}",
                IsEnabled = true
            };

            ctx.IrpTemplates.AddRange(templateA, templateB, cyclic);
            ctx.SaveChanges();

            _templateAId = templateA.Id;
            _templateBId = templateB.Id;
            _cyclicTemplateId = cyclic.Id;

            var taskA = new IrpTemplateTask
            {
                IrpTemplateId = templateA.Id,
                Title = "Contain",
                InstructionsMarkdown = "Isolate the host",
                AssigneeRuleJson = "{\"role\":\"analyst\"}",
                DueOffsetSeconds = 3600,
                RequiresConfirmation = false
            };
            ctx.IrpTemplateTasks.Add(taskA);
            ctx.SaveChanges();
            _taskAId = taskA.Id;

            var taskB = new IrpTemplateTask
            {
                IrpTemplateId = templateA.Id,
                Title = "Eradicate",
                AssigneeRuleJson = "{\"role\":\"analyst\"}",
                DueOffsetSeconds = 7200,
                PredecessorTaskId = taskA.Id,
                RequiresConfirmation = true
            };
            ctx.IrpTemplateTasks.Add(taskB);
            ctx.SaveChanges();
            _taskBId = taskB.Id;

            var taskC = new IrpTemplateTask
            {
                IrpTemplateId = templateA.Id,
                Title = "Recover",
                AssigneeRuleJson = "{\"role\":\"coordinator\"}",
                DueOffsetSeconds = 14400,
                PredecessorTaskId = taskB.Id,
                RequiresConfirmation = false
            };
            ctx.IrpTemplateTasks.Add(taskC);
            ctx.SaveChanges();
            _taskCId = taskC.Id;

            var taskX = new IrpTemplateTask
            {
                IrpTemplateId = cyclic.Id,
                Title = "X",
                AssigneeRuleJson = "{}",
                DueOffsetSeconds = 60
            };
            var taskY = new IrpTemplateTask
            {
                IrpTemplateId = cyclic.Id,
                Title = "Y",
                AssigneeRuleJson = "{}",
                DueOffsetSeconds = 120
            };
            ctx.IrpTemplateTasks.AddRange(taskX, taskY);
            ctx.SaveChanges();

            _cyclicTaskXId = taskX.Id;
            _cyclicTaskYId = taskY.Id;

            taskX.PredecessorTaskId = taskY.Id;
            taskY.PredecessorTaskId = taskX.Id;
            ctx.SaveChanges();
        }

        _controller = ResolveController<IrpTemplatesController>(s => s.AddSingleton<IDalService>(_dal));
    }

    [Fact]
    public async Task TestGetAllReturnsEveryTemplateWithItsTasks()
    {
        var result = await _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var templates = Assert.IsType<List<IrpTemplate>>(ok.Value);

        Assert.Equal(3, templates.Count);

        var templateA = templates.Single(t => t.Id == _templateAId);
        Assert.Equal("Template A", templateA.Name);
        Assert.Equal(3, templateA.Tasks.Count);
    }

    [Fact]
    public async Task TestGetByIdReturnsTheTemplate()
    {
        var result = await _controller.GetById(_templateAId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var template = Assert.IsType<IrpTemplate>(ok.Value);

        Assert.Equal(_templateAId, template.Id);
        Assert.Equal("Template A", template.Name);
        Assert.Equal("First template", template.Description);
        Assert.True(template.IsEnabled);
        Assert.Equal(3, template.Tasks.Count);
    }

    [Fact]
    public async Task TestGetByIdReturnsNotFoundForAnUnknownTemplate()
    {
        var result = await _controller.GetById(9999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestCreatePersistsTheTemplate()
    {
        var request = new CreateIrpTemplateRequest
        {
            Name = "Created",
            Description = "Created by test",
            MatchingRulesJson = "{\"severity\":\"high\"}",
            IsEnabled = true
        };

        var result = await _controller.Create(request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var template = Assert.IsType<IrpTemplate>(created.Value);

        Assert.True(template.Id > 0);
        Assert.Equal($"IrpTemplates/{template.Id}", created.Location);

        using var ctx = _dal.GetContext();
        var stored = ctx.IrpTemplates.Single(t => t.Id == template.Id);
        Assert.Equal("Created", stored.Name);
        Assert.Equal("Created by test", stored.Description);
        Assert.Equal("{\"severity\":\"high\"}", stored.MatchingRulesJson);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task TestUpdateChangesTheStoredTemplate()
    {
        var request = new UpdateIrpTemplateRequest
        {
            Name = "Renamed",
            Description = "Updated",
            MatchingRulesJson = "{\"category\":\"malware\"}",
            IsEnabled = false
        };

        var result = await _controller.Update(_templateAId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var template = Assert.IsType<IrpTemplate>(ok.Value);
        Assert.Equal("Renamed", template.Name);

        using var ctx = _dal.GetContext();
        var stored = ctx.IrpTemplates.Single(t => t.Id == _templateAId);
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal("Updated", stored.Description);
        Assert.Equal("{\"category\":\"malware\"}", stored.MatchingRulesJson);
        Assert.False(stored.IsEnabled);
    }

    [Fact]
    public async Task TestUpdateReturnsNotFoundForAnUnknownTemplate()
    {
        var request = new UpdateIrpTemplateRequest
        {
            Name = "Renamed",
            MatchingRulesJson = "{}",
            IsEnabled = true
        };

        var result = await _controller.Update(9999, request);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestDeleteRemovesTheTemplate()
    {
        var result = await _controller.Delete(_templateBId);

        Assert.IsType<NoContentResult>(result);

        using var ctx = _dal.GetContext();
        Assert.False(ctx.IrpTemplates.Any(t => t.Id == _templateBId));
    }

    [Fact]
    public async Task TestDeleteReturnsNotFoundForAnUnknownTemplate()
    {
        var result = await _controller.Delete(9999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task TestGetTasksReturnsTheTasksOfTheTemplate()
    {
        var result = await _controller.GetTasks(_templateAId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var tasks = Assert.IsType<List<IrpTemplateTask>>(ok.Value);

        Assert.Equal(3, tasks.Count);
        Assert.Equal(new[] { _taskAId, _taskBId, _taskCId }, tasks.Select(t => t.Id).ToArray());
        Assert.All(tasks, t => Assert.Equal(_templateAId, t.IrpTemplateId));
    }

    [Fact]
    public async Task TestGetTasksReturnsNotFoundForAnUnknownTemplate()
    {
        var result = await _controller.GetTasks(9999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateTaskPersistsTheTask()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Lessons learned",
            InstructionsMarkdown = "Write the post mortem",
            AssigneeRuleJson = "{\"role\":\"coordinator\"}",
            DueOffsetSeconds = 86400,
            PredecessorTaskId = _taskCId,
            RequiresConfirmation = true
        };

        var result = await _controller.CreateTask(_templateAId, request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var task = Assert.IsType<IrpTemplateTask>(created.Value);

        Assert.True(task.Id > 0);
        Assert.Equal($"IrpTemplates/{_templateAId}/Tasks/{task.Id}", created.Location);

        using var ctx = _dal.GetContext();
        var stored = ctx.IrpTemplateTasks.Single(t => t.Id == task.Id);
        Assert.Equal("Lessons learned", stored.Title);
        Assert.Equal("Write the post mortem", stored.InstructionsMarkdown);
        Assert.Equal("{\"role\":\"coordinator\"}", stored.AssigneeRuleJson);
        Assert.Equal(86400, stored.DueOffsetSeconds);
        Assert.Equal(_taskCId, stored.PredecessorTaskId);
        Assert.True(stored.RequiresConfirmation);
    }

    [Fact]
    public async Task TestCreateTaskWithoutPredecessorIsAccepted()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Root task",
            AssigneeRuleJson = "{}",
            DueOffsetSeconds = 0
        };

        var result = await _controller.CreateTask(_templateBId, request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var task = Assert.IsType<IrpTemplateTask>(created.Value);
        Assert.Null(task.PredecessorTaskId);
    }

    [Fact]
    public async Task TestCreateTaskReturnsNotFoundForAnUnknownTemplate()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Orphan",
            AssigneeRuleJson = "{}"
        };

        var result = await _controller.CreateTask(9999, request);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateTaskRejectsAPredecessorOutsideTheTemplate()
    {
        var unknownPredecessor = new IrpTemplateTaskRequest
        {
            Title = "Bad predecessor",
            AssigneeRuleJson = "{}",
            PredecessorTaskId = 9999
        };

        var unknownResult = await _controller.CreateTask(_templateAId, unknownPredecessor);
        Assert.IsType<BadRequestObjectResult>(unknownResult.Result);

        // A real task, but one that belongs to another template.
        var foreignPredecessor = new IrpTemplateTaskRequest
        {
            Title = "Foreign predecessor",
            AssigneeRuleJson = "{}",
            PredecessorTaskId = _taskAId
        };

        var foreignResult = await _controller.CreateTask(_templateBId, foreignPredecessor);
        var badRequest = Assert.IsType<BadRequestObjectResult>(foreignResult.Result);
        Assert.Contains("does not belong to template", (string)badRequest.Value);
    }

    [Fact]
    public async Task TestUpdateTaskChangesTheStoredTask()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Eradicate (revised)",
            InstructionsMarkdown = "Remove persistence",
            AssigneeRuleJson = "{\"role\":\"engineer\"}",
            DueOffsetSeconds = 5400,
            PredecessorTaskId = _taskAId,
            RequiresConfirmation = false
        };

        var result = await _controller.UpdateTask(_templateAId, _taskBId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var task = Assert.IsType<IrpTemplateTask>(ok.Value);
        Assert.Equal("Eradicate (revised)", task.Title);

        using var ctx = _dal.GetContext();
        var stored = ctx.IrpTemplateTasks.Single(t => t.Id == _taskBId);
        Assert.Equal("Eradicate (revised)", stored.Title);
        Assert.Equal("Remove persistence", stored.InstructionsMarkdown);
        Assert.Equal("{\"role\":\"engineer\"}", stored.AssigneeRuleJson);
        Assert.Equal(5400, stored.DueOffsetSeconds);
        Assert.Equal(_taskAId, stored.PredecessorTaskId);
        Assert.False(stored.RequiresConfirmation);
    }

    [Fact]
    public async Task TestUpdateTaskReturnsNotFoundWhenTheTaskIsNotOnTheTemplate()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Nowhere",
            AssigneeRuleJson = "{}"
        };

        var unknownTask = await _controller.UpdateTask(_templateAId, 9999, request);
        Assert.IsType<NotFoundObjectResult>(unknownTask.Result);

        // The task exists, but under another template.
        var wrongTemplate = await _controller.UpdateTask(_templateBId, _taskAId, request);
        Assert.IsType<NotFoundObjectResult>(wrongTemplate.Result);
    }

    [Fact]
    public async Task TestUpdateTaskRejectsSelfDependency()
    {
        var request = new IrpTemplateTaskRequest
        {
            Title = "Self",
            AssigneeRuleJson = "{}",
            PredecessorTaskId = _taskBId
        };

        var result = await _controller.UpdateTask(_templateAId, _taskBId, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("A task cannot depend on itself", (string)badRequest.Value);
    }

    [Fact]
    public async Task TestUpdateTaskRejectsAPredecessorThatWouldCloseACycle()
    {
        // A is the root of A -> B -> C, so making C its predecessor closes the chain.
        var request = new IrpTemplateTaskRequest
        {
            Title = "Contain",
            AssigneeRuleJson = "{}",
            PredecessorTaskId = _taskCId
        };

        var result = await _controller.UpdateTask(_templateAId, _taskAId, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("That predecessor would create a dependency cycle", (string)badRequest.Value);
    }

    /// <summary>
    /// A cycle that already exists between other tasks must not make an unrelated edge invalid —
    /// the acyclicity walk has to stop instead of looping forever.
    /// </summary>
    [Fact]
    public async Task TestUpdateTaskAcceptsAnEdgeThatOnlyReachesAPreexistingCycle()
    {
        int newTaskId;
        using (var ctx = _dal.GetContext())
        {
            var seeded = new IrpTemplateTask
            {
                IrpTemplateId = _cyclicTemplateId,
                Title = "Z",
                AssigneeRuleJson = "{}"
            };
            ctx.IrpTemplateTasks.Add(seeded);
            ctx.SaveChanges();
            newTaskId = seeded.Id;
        }

        var request = new IrpTemplateTaskRequest
        {
            Title = "Z",
            AssigneeRuleJson = "{}",
            PredecessorTaskId = _cyclicTaskXId
        };

        var result = await _controller.UpdateTask(_cyclicTemplateId, newTaskId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var task = Assert.IsType<IrpTemplateTask>(ok.Value);
        Assert.Equal(_cyclicTaskXId, task.PredecessorTaskId);
    }

    [Fact]
    public async Task TestDeleteTaskRewiresItsSuccessors()
    {
        var result = await _controller.DeleteTask(_templateAId, _taskBId);

        Assert.IsType<NoContentResult>(result);

        using var ctx = _dal.GetContext();
        Assert.False(ctx.IrpTemplateTasks.Any(t => t.Id == _taskBId));

        var taskC = ctx.IrpTemplateTasks.Single(t => t.Id == _taskCId);
        Assert.Equal(_taskAId, taskC.PredecessorTaskId);
    }

    [Fact]
    public async Task TestDeleteTaskReturnsNotFoundWhenTheTaskIsNotOnTheTemplate()
    {
        var unknownTask = await _controller.DeleteTask(_templateAId, 9999);
        Assert.IsType<NotFoundObjectResult>(unknownTask);

        var wrongTemplate = await _controller.DeleteTask(_templateBId, _taskAId);
        Assert.IsType<NotFoundObjectResult>(wrongTemplate);
    }

    [Fact]
    public async Task TestCloneCopiesTheTemplateDisabledWithRemappedPredecessors()
    {
        var result = await _controller.Clone(_templateAId);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var clone = Assert.IsType<IrpTemplate>(created.Value);

        Assert.NotEqual(_templateAId, clone.Id);
        Assert.Equal("Template A (copy)", clone.Name);
        Assert.Equal("First template", clone.Description);
        Assert.Equal("{\"category\":\"phishing\"}", clone.MatchingRulesJson);
        Assert.False(clone.IsEnabled);
        Assert.Equal($"IrpTemplates/{clone.Id}", created.Location);

        using var ctx = _dal.GetContext();
        var cloneTasks = ctx.IrpTemplateTasks
            .Where(t => t.IrpTemplateId == clone.Id)
            .OrderBy(t => t.Id)
            .ToList();

        Assert.Equal(3, cloneTasks.Count);

        var root = cloneTasks.Single(t => t.Title == "Contain");
        var middle = cloneTasks.Single(t => t.Title == "Eradicate");
        var last = cloneTasks.Single(t => t.Title == "Recover");

        Assert.Null(root.PredecessorTaskId);
        Assert.Equal(root.Id, middle.PredecessorTaskId);
        Assert.Equal(middle.Id, last.PredecessorTaskId);

        // The originals must be untouched.
        Assert.Equal(3, ctx.IrpTemplateTasks.Count(t => t.IrpTemplateId == _templateAId));
        Assert.True(ctx.IrpTemplates.Single(t => t.Id == _templateAId).IsEnabled);
    }

    [Fact]
    public async Task TestCloneReturnsNotFoundForAnUnknownTemplate()
    {
        var result = await _controller.Clone(9999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    /// <summary>
    /// Stored tasks that already form a cycle must still clone: the topological pass falls back to
    /// id order, and the edge it cannot resolve comes out flattened rather than looping.
    /// </summary>
    [Fact]
    public async Task TestCloneFlattensTasksThatAlreadyFormACycle()
    {
        var result = await _controller.Clone(_cyclicTemplateId);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var clone = Assert.IsType<IrpTemplate>(created.Value);

        using var ctx = _dal.GetContext();
        var cloneTasks = ctx.IrpTemplateTasks
            .Where(t => t.IrpTemplateId == clone.Id)
            .OrderBy(t => t.Id)
            .ToList();

        Assert.Equal(2, cloneTasks.Count);

        var copyX = cloneTasks.Single(t => t.Title == "X");
        var copyY = cloneTasks.Single(t => t.Title == "Y");

        // X was written first, so its predecessor (Y) had no mapping yet and came out unresolved.
        Assert.Equal(0, copyX.PredecessorTaskId);
        Assert.Equal(copyX.Id, copyY.PredecessorTaskId);

        // The source cycle is untouched.
        Assert.Equal(_cyclicTaskYId, ctx.IrpTemplateTasks.Single(t => t.Id == _cyclicTaskXId).PredecessorTaskId);
        Assert.Equal(_cyclicTaskXId, ctx.IrpTemplateTasks.Single(t => t.Id == _cyclicTaskYId).PredecessorTaskId);
    }
}
