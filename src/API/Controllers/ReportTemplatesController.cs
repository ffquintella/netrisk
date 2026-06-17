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

public class CreateTemplateRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string LayoutJson { get; set; } = null!;
    public string BrandingJson { get; set; } = null!;
}

public class UpdateTemplateRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string LayoutJson { get; set; } = null!;
    public string BrandingJson { get; set; } = null!;
}

public class PreviewTemplateRequest
{
    public string LayoutJson { get; set; } = null!;
    public string BrandingJson { get; set; } = null!;
    public string? ReportTitle { get; set; }
}

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class ReportTemplatesController(
    IDalService dalService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IQuestPdfRenderingService questPdfRenderingService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IDalService DalService { get; } = dalService;

    [HttpPost]
    [Route("preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    public async Task<IActionResult> Preview([FromBody] PreviewTemplateRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested a report template preview", user.Value);

        var image = await questPdfRenderingService.RenderPreviewImageAsync(
            request.LayoutJson ?? string.Empty,
            request.BrandingJson ?? string.Empty,
            request.ReportTitle ?? "Report Preview");

        return File(image, "image/png");
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ReportTemplate>))]
    public async Task<ActionResult<List<ReportTemplate>>> GetAll()
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed all report templates", user.Value);

        using var dbContext = DalService.GetContext();
        var templates = await dbContext.ReportTemplates
            .Include(t => t.Owner)
            .Include(t => t.Versions)
            .ToListAsync();

        return Ok(templates);
    }

    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportTemplate))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportTemplate>> GetById(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested report template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.ReportTemplates
            .Include(t => t.Owner)
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            return NotFound($"Report template with ID {id} not found");

        return Ok(template);
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ReportTemplate))]
    public async Task<ActionResult<ReportTemplate>> Create([FromBody] CreateTemplateRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is creating a new report template '{Name}'", user.Value, request.Name);

        using var dbContext = DalService.GetContext();

        var template = new ReportTemplate
        {
            Name = request.Name,
            Description = request.Description,
            OwnerId = user.Value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.ReportTemplates.Add(template);
        await dbContext.SaveChangesAsync(); // Generates template ID

        var version = new ReportTemplateVersion
        {
            TemplateId = template.Id,
            Version = 1,
            LayoutJson = request.LayoutJson,
            BrandingJson = request.BrandingJson,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.ReportTemplateVersions.Add(version);
        await dbContext.SaveChangesAsync();

        return Created($"ReportTemplates/{template.Id}", template);
    }

    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportTemplate))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportTemplate>> Update(int id, [FromBody] UpdateTemplateRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is updating report template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.ReportTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            return NotFound($"Report template with ID {id} not found");

        template.Name = request.Name;
        template.Description = request.Description;
        template.UpdatedAt = DateTime.UtcNow;

        // Get latest version number
        var latestVersionNumber = template.Versions.Any() ? template.Versions.Max(v => v.Version) : 0;

        var newVersion = new ReportTemplateVersion
        {
            TemplateId = template.Id,
            Version = latestVersionNumber + 1,
            LayoutJson = request.LayoutJson,
            BrandingJson = request.BrandingJson,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.ReportTemplateVersions.Add(newVersion);
        dbContext.ReportTemplates.Update(template);
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
        Logger.Information("User:{UserValue} is deleting report template {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var template = await dbContext.ReportTemplates.FindAsync(id);

        if (template == null)
            return NotFound($"Report template with ID {id} not found");

        dbContext.ReportTemplates.Remove(template);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }
}
