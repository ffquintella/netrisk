using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Hangfire;
using API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

public class CreateScheduleRequest
{
    public int ReportTemplateVersionId { get; set; }
    public string FrequencyCron { get; set; } = null!;
    public string Timezone { get; set; } = "UTC";
    public string RecipientsJson { get; set; } = null!;
    public bool IsEnabled { get; set; }
}

public class UpdateScheduleRequest
{
    public int ReportTemplateVersionId { get; set; }
    public string FrequencyCron { get; set; } = null!;
    public string Timezone { get; set; } = "UTC";
    public string RecipientsJson { get; set; } = null!;
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Report schedules: a template, a cron and the addresses the rendered report is mailed to.
///
/// T237 review. The job that runs a schedule reads under its <b>creator's</b> entity scope as it stands at run time
/// (<see cref="ScheduledReportJob"/>), so the creator is stamped here from the caller and never taken from the body.
/// That makes a schedule its creator's: only they — or an unrestricted caller — may read, change, delete or trigger it,
/// because whoever edits the recipients of somebody else's schedule would otherwise receive that person's entities. A
/// schedule from before the column has no creator and is visible to unrestricted callers only; saving it makes it theirs.
/// Every action needs <see cref="ScheduledReportJob.RequiredPermission"/>; it used to need only a valid user.
/// </summary>
[PermissionAuthorize(ScheduledReportJob.RequiredPermission)]
[ApiController]
[Route("[controller]")]
public class ReportSchedulesController(
    IDalService dalService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IDalService DalService { get; } = dalService;

    /// <summary>An unrestricted caller (global scope or the Admin role) sees and manages every schedule.</summary>
    private bool CallerIsUnrestricted() => DalService.GetCurrentEntityScope().IsUnrestricted;

    /// <summary>Whether the caller may see and act on <paramref name="schedule"/>: its creator, or an unrestricted caller.</summary>
    private bool CallerOwns(ReportSchedule schedule, int userId) =>
        CallerIsUnrestricted() || schedule.CreatedById == userId;

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ReportSchedule>))]
    public async Task<ActionResult<List<ReportSchedule>>> GetAll()
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed all report schedules", user.Value);

        using var dbContext = DalService.GetContext();
        var query = dbContext.ReportSchedules
            .Include(s => s.ReportTemplateVersion)
                .ThenInclude(v => v.Template)
            .AsQueryable();

        // A scoped caller lists their own schedules only: another user's carries that user's recipients.
        if (!CallerIsUnrestricted()) query = query.Where(s => s.CreatedById == user.Value);

        var schedules = await query.ToListAsync();

        return Ok(schedules);
    }

    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportSchedule))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportSchedule>> GetById(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested report schedule {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var schedule = await dbContext.ReportSchedules
            .Include(s => s.ReportTemplateVersion)
                .ThenInclude(v => v.Template)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule == null || !CallerOwns(schedule, user.Value))
            return NotFound($"Report schedule with ID {id} not found");

        return Ok(schedule);
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ReportSchedule))]
    public async Task<ActionResult<ReportSchedule>> Create([FromBody] CreateScheduleRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is creating a new report schedule for template version {TemplateVersionId}", user.Value, request.ReportTemplateVersionId);

        using var dbContext = DalService.GetContext();

        var schedule = new ReportSchedule
        {
            ReportTemplateVersionId = request.ReportTemplateVersionId,
            FrequencyCron = request.FrequencyCron,
            Timezone = request.Timezone,
            RecipientsJson = request.RecipientsJson,
            IsEnabled = request.IsEnabled,
            LastRunAt = null,
            LastStatus = "Created",
            // Whose scope the job reads under (T237 review): the caller, never the body.
            CreatedById = user.Value
        };

        dbContext.ReportSchedules.Add(schedule);
        await dbContext.SaveChangesAsync(); // Generates schedule ID

        if (schedule.IsEnabled)
        {
            // Register or update Recurring Hangfire Job
            RecurringJob.AddOrUpdate<ScheduledReportJob>(
                $"ReportSchedule_{schedule.Id}",
                job => job.Run(schedule.Id),
                schedule.FrequencyCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone) }
            );
        }

        return Created($"ReportSchedules/{schedule.Id}", schedule);
    }

    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportSchedule))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportSchedule>> Update(int id, [FromBody] UpdateScheduleRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is updating report schedule {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var schedule = await dbContext.ReportSchedules.FindAsync(id);

        if (schedule == null || !CallerOwns(schedule, user.Value))
            return NotFound($"Report schedule with ID {id} not found");

        // A schedule from before the creator column runs for nobody; the unrestricted caller who saves it adopts it. An
        // existing creator is kept: an administrator editing a user's schedule does not widen what it reads.
        schedule.CreatedById ??= user.Value;

        schedule.ReportTemplateVersionId = request.ReportTemplateVersionId;
        schedule.FrequencyCron = request.FrequencyCron;
        schedule.Timezone = request.Timezone;
        schedule.RecipientsJson = request.RecipientsJson;
        schedule.IsEnabled = request.IsEnabled;

        dbContext.ReportSchedules.Update(schedule);
        await dbContext.SaveChangesAsync();

        if (schedule.IsEnabled)
        {
            // Register or update Recurring Hangfire Job
            RecurringJob.AddOrUpdate<ScheduledReportJob>(
                $"ReportSchedule_{schedule.Id}",
                job => job.Run(schedule.Id),
                schedule.FrequencyCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone) }
            );
        }
        else
        {
            // Remove recurring job if schedule is disabled
            RecurringJob.RemoveIfExists($"ReportSchedule_{schedule.Id}");
        }

        return Ok(schedule);
    }

    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is deleting report schedule {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var schedule = await dbContext.ReportSchedules.FindAsync(id);

        if (schedule == null || !CallerOwns(schedule, user.Value))
            return NotFound($"Report schedule with ID {id} not found");

        dbContext.ReportSchedules.Remove(schedule);
        await dbContext.SaveChangesAsync();

        // Clear Recurring Hangfire Job
        RecurringJob.RemoveIfExists($"ReportSchedule_{schedule.Id}");

        return NoContent();
    }

    [HttpPost]
    [Route("{id}/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerTest(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} triggered manual test for report schedule {Id}", user.Value, id);

        using var dbContext = DalService.GetContext();
        var schedule = await dbContext.ReportSchedules.FindAsync(id);

        if (schedule == null || !CallerOwns(schedule, user.Value))
            return NotFound($"Report schedule with ID {id} not found");

        // Enqueue single immediate run in Hangfire
        BackgroundJob.Enqueue<ScheduledReportJob>(job => job.Run(schedule.Id));

        return Ok("Report test enqueued successfully");
    }
}
