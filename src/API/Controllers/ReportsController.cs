
using API.Exceptions;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DAL.EntitiesDto;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class ReportsController(
    IReportsService reportsService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IReportsService ReportsService { get; } = reportsService;

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Report>))]
    public ActionResult<List<Report>> Get()
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} listed reports", user.Value);
        return Ok(ReportsService.GetAll().ToList());
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Report))]
    public async Task<ActionResult<Report>> Create([FromBody] ReportDto report, 
        [FromQuery] string localization = "pt-BR")
    {

        try
        {
            SetLocalization(localization);
        }
        catch (BadRequestException e)
        {
            Logger.Warning(e, "Invalid localization {localization}", localization);
            return BadRequest(e.Message);
        }      
        
        var user = GetUser();

        try
        {
            Logger.Information("User:{UserValue} created a report", user.Value);
        
            var created = await ReportsService.CreateAsync(report, user);
        
            return Created($"Reports/{created.Id}",created);
        }catch (Exception e)
        {
            Logger.Error(e,"Error creating report");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

    }
    
    [HttpDelete]
    [Route("{reportId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Report))]
    public ActionResult<Report> Delete(int reportId)
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} deleted a report {reportId}", user.Value, reportId);

        try
        {
            ReportsService.Delete(reportId);
            return Ok();
        }catch (DataNotFoundException e)
        {
            Logger.Warning(e,"Trial to delete unknown report {reportId}", reportId);
            return NotFound(e.Message);
        }catch (Exception e)
        {
            Logger.Error(e,"Error deleting report {reportId}", reportId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}