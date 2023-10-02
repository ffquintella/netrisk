using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using Host = Microsoft.Extensions.Hosting.Host;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class VulnerabilitiesController: ApiBaseController
{
    IVulnerabilitiesService VulnerabilitiesService { get; }
    
    public VulnerabilitiesController(ILogger logger, IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IVulnerabilitiesService vulnerabilitiesService) 
        : base(logger, httpContextAccessor, usersService)
    {
        VulnerabilitiesService = vulnerabilitiesService;
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Vulnerability>> GetAll()
    {

        var user = GetUser();

        try
        {
            var vulnerabilities = VulnerabilitiesService.GetAll();
            Logger.Information("User:{User} listed all vulnerabilities", user.Value);
            return Ok(vulnerabilities);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing vulnerabilities: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}