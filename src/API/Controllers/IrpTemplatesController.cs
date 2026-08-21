using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

public class CreateIrpTemplateRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string MatchingRulesJson { get; set; } = null!;
    public bool IsEnabled { get; set; }
}

public class UpdateIrpTemplateRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string MatchingRulesJson { get; set; } = null!;
    public bool IsEnabled { get; set; }
}

public class IrpTemplateTaskRequest
{
    public string Title { get; set; } = null!;
    public string? InstructionsMarkdown { get; set; }
    public string AssigneeRuleJson { get; set; } = null!;

    /// <summary>Offset from plan activation ("T+4h"), stored in seconds.</summary>
    public int DueOffsetSeconds { get; set; }

    /// <summary>The task that must finish before this one starts, or null for a root task.</summary>
    public int? PredecessorTaskId { get; set; }

    /// <summary>SOAR human-in-the-loop gate: generated as Proposed for a coordinator to approve.</summary>
    public bool RequiresConfirmation { get; set; }
}

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class IrpTemplatesController(
    IDalService dalService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IDalService DalService { get; } = dalService;

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IrpTemplate>))]
    public async Task<ActionResult<List<IrpTemplate>>> GetAll()
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed all IRP templates", user.Value);

        using var dbContext = DalService.GetContext();
        var templates = await dbContext.IrpTemplates
            .Include(t => t.Tasks)
            .ToListAsync();

        return Ok(templates);
    }

    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IrpTemplate))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrpTemplate>> GetById(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested IRP template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.IrpTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            return NotFound($"IRP template with ID {id} not found");

        return Ok(template);
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IrpTemplate))]
    public async Task<ActionResult<IrpTemplate>> Create([FromBody] CreateIrpTemplateRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is creating a new IRP template '{Name}'", user.Value, request.Name);

        using var dbContext = DalService.GetContext();

        var template = new IrpTemplate
        {
            Name = request.Name,
            Description = request.Description,
            MatchingRulesJson = request.MatchingRulesJson,
            IsEnabled = request.IsEnabled
        };

        dbContext.IrpTemplates.Add(template);
        await dbContext.SaveChangesAsync();

        return Created($"IrpTemplates/{template.Id}", template);
    }

    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IrpTemplate))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrpTemplate>> Update(int id, [FromBody] UpdateIrpTemplateRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is updating IRP template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.IrpTemplates.FindAsync(id);

        if (template == null)
            return NotFound($"IRP template with ID {id} not found");

        template.Name = request.Name;
        template.Description = request.Description;
        template.MatchingRulesJson = request.MatchingRulesJson;
        template.IsEnabled = request.IsEnabled;

        dbContext.IrpTemplates.Update(template);
        await dbContext.SaveChangesAsync();

        return Ok(template);
    }

    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is deleting IRP template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.IrpTemplates.FindAsync(id);

        if (template == null)
            return NotFound($"IRP template with ID {id} not found");

        dbContext.IrpTemplates.Remove(template);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet]
    [Route("{id}/Tasks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IrpTemplateTask>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IrpTemplateTask>>> GetTasks(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed tasks of IRP template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();

        if (!await dbContext.IrpTemplates.AnyAsync(t => t.Id == id))
            return NotFound($"IRP template with ID {id} not found");

        var tasks = await dbContext.IrpTemplateTasks
            .Where(t => t.IrpTemplateId == id)
            .OrderBy(t => t.Id)
            .ToListAsync();

        return Ok(tasks);
    }

    [HttpPost]
    [Route("{id}/Tasks")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IrpTemplateTask))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrpTemplateTask>> CreateTask(int id, [FromBody] IrpTemplateTaskRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is adding task '{Title}' to IRP template {Id}", user.Value, request.Title, id);

        using var dbContext = DalService.GetContext();

        if (!await dbContext.IrpTemplates.AnyAsync(t => t.Id == id))
            return NotFound($"IRP template with ID {id} not found");

        var predecessorError = await ValidatePredecessorAsync(dbContext, id, taskId: null, request.PredecessorTaskId);
        if (predecessorError != null) return BadRequest(predecessorError);

        var task = new IrpTemplateTask
        {
            IrpTemplateId = id,
            Title = request.Title,
            InstructionsMarkdown = request.InstructionsMarkdown,
            AssigneeRuleJson = request.AssigneeRuleJson,
            DueOffsetSeconds = request.DueOffsetSeconds,
            PredecessorTaskId = request.PredecessorTaskId,
            RequiresConfirmation = request.RequiresConfirmation
        };

        dbContext.IrpTemplateTasks.Add(task);
        await dbContext.SaveChangesAsync();

        return Created($"IrpTemplates/{id}/Tasks/{task.Id}", task);
    }

    [HttpPut]
    [Route("{id}/Tasks/{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IrpTemplateTask))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrpTemplateTask>> UpdateTask(int id, int taskId, [FromBody] IrpTemplateTaskRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is updating task {TaskId} of IRP template {Id}", user.Value, taskId, id);

        using var dbContext = DalService.GetContext();

        var task = await dbContext.IrpTemplateTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.IrpTemplateId == id);

        if (task == null)
            return NotFound($"Task with ID {taskId} not found on IRP template {id}");

        var predecessorError = await ValidatePredecessorAsync(dbContext, id, taskId, request.PredecessorTaskId);
        if (predecessorError != null) return BadRequest(predecessorError);

        task.Title = request.Title;
        task.InstructionsMarkdown = request.InstructionsMarkdown;
        task.AssigneeRuleJson = request.AssigneeRuleJson;
        task.DueOffsetSeconds = request.DueOffsetSeconds;
        task.PredecessorTaskId = request.PredecessorTaskId;
        task.RequiresConfirmation = request.RequiresConfirmation;

        dbContext.IrpTemplateTasks.Update(task);
        await dbContext.SaveChangesAsync();

        return Ok(task);
    }

    [HttpDelete]
    [Route("{id}/Tasks/{taskId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTask(int id, int taskId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is deleting task {TaskId} of IRP template {Id}", user.Value, taskId, id);

        using var dbContext = DalService.GetContext();

        var task = await dbContext.IrpTemplateTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.IrpTemplateId == id);

        if (task == null)
            return NotFound($"Task with ID {taskId} not found on IRP template {id}");

        // Successors would otherwise be left pointing at a missing row, which the acyclicity
        // walk below treats as a broken chain. Detach them instead of cascading the delete.
        var successors = await dbContext.IrpTemplateTasks
            .Where(t => t.PredecessorTaskId == taskId)
            .ToListAsync();

        foreach (var successor in successors)
        {
            successor.PredecessorTaskId = task.PredecessorTaskId;
        }

        dbContext.IrpTemplateTasks.Remove(task);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost]
    [Route("{id}/Clone")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IrpTemplate))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrpTemplate>> Clone(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is cloning IRP template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();

        var source = await dbContext.IrpTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (source == null)
            return NotFound($"IRP template with ID {id} not found");

        var clone = new IrpTemplate
        {
            Name = $"{source.Name} (copy)",
            Description = source.Description,
            MatchingRulesJson = source.MatchingRulesJson,
            // A clone starts disabled so it cannot begin matching incidents before it is reviewed.
            IsEnabled = false
        };

        dbContext.IrpTemplates.Add(clone);
        await dbContext.SaveChangesAsync();

        // Copy tasks in dependency order, remapping predecessor ids onto the new rows.
        var idMap = new Dictionary<int, int>();
        foreach (var sourceTask in OrderByDependency(source.Tasks.ToList()))
        {
            var copy = new IrpTemplateTask
            {
                IrpTemplateId = clone.Id,
                Title = sourceTask.Title,
                InstructionsMarkdown = sourceTask.InstructionsMarkdown,
                AssigneeRuleJson = sourceTask.AssigneeRuleJson,
                DueOffsetSeconds = sourceTask.DueOffsetSeconds,
                RequiresConfirmation = sourceTask.RequiresConfirmation,
                PredecessorTaskId = sourceTask.PredecessorTaskId.HasValue
                    ? idMap.GetValueOrDefault(sourceTask.PredecessorTaskId.Value)
                    : null
            };

            dbContext.IrpTemplateTasks.Add(copy);
            await dbContext.SaveChangesAsync();

            idMap[sourceTask.Id] = copy.Id;
        }

        var result = await dbContext.IrpTemplates
            .Include(t => t.Tasks)
            .FirstAsync(t => t.Id == clone.Id);

        return Created($"IrpTemplates/{clone.Id}", result);
    }

    /// <summary>
    /// Rejects a predecessor that does not exist, belongs to another template, is the task
    /// itself, or would close a cycle. The spec requires acyclicity to be validated on save —
    /// a cycle here would make the generated plan impossible to schedule.
    /// </summary>
    private static async Task<string?> ValidatePredecessorAsync(
        DAL.Context.AuditableContext dbContext, int templateId, int? taskId, int? predecessorId)
    {
        if (predecessorId == null) return null;

        if (taskId.HasValue && predecessorId.Value == taskId.Value)
            return "A task cannot depend on itself";

        var edges = await dbContext.IrpTemplateTasks
            .Where(t => t.IrpTemplateId == templateId)
            .Select(t => new { t.Id, t.PredecessorTaskId })
            .ToListAsync();

        if (edges.All(e => e.Id != predecessorId.Value))
            return $"Predecessor task {predecessorId.Value} does not belong to template {templateId}";

        // Walk up from the proposed predecessor; reaching this task means the edge closes a cycle.
        var predecessorById = edges.ToDictionary(e => e.Id, e => e.PredecessorTaskId);
        var seen = new HashSet<int>();
        var cursor = predecessorId;

        while (cursor.HasValue)
        {
            if (taskId.HasValue && cursor.Value == taskId.Value)
                return "That predecessor would create a dependency cycle";

            // Guards against a cycle that already exists among the other tasks.
            if (!seen.Add(cursor.Value)) break;

            cursor = predecessorById.GetValueOrDefault(cursor.Value);
        }

        return null;
    }

    /// <summary>Topological order so a clone always writes a predecessor before its dependants.</summary>
    private static List<IrpTemplateTask> OrderByDependency(List<IrpTemplateTask> tasks)
    {
        var ordered = new List<IrpTemplateTask>();
        var placed = new HashSet<int>();
        var remaining = new List<IrpTemplateTask>(tasks);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(t => t.PredecessorTaskId == null || placed.Contains(t.PredecessorTaskId.Value))
                .ToList();

            // Nothing ready means the stored data already holds a cycle; emit the rest in id
            // order rather than looping forever, and let the clone come out flattened.
            if (ready.Count == 0) ready = remaining.OrderBy(t => t.Id).ToList();

            foreach (var task in ready)
            {
                ordered.Add(task);
                placed.Add(task.Id);
                remaining.Remove(task);
            }
        }

        return ordered;
    }
}
