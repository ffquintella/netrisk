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
}
