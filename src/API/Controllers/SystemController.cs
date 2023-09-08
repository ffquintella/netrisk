using System.Diagnostics.Tracing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class SystemController : ApiBaseController
{
    private ISystemService _systemService;
    
    public SystemController(ILogger logger, 
        IHttpContextAccessor httpContextAccessor, 
        IUsersService usersService,
        ISystemService systemService
        ) : base(logger, httpContextAccessor, usersService)
    {
        _systemService = systemService;
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("Ping")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public ActionResult<string> Ping()
    {
        Logger.Debug("Ping requested");
        return "Pong";
    }
    
    [HttpGet]
    [AllowAnonymous]
    [Route("ClientVersion")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public async Task<string> Version()
    {
        Logger.Debug("Client Version Requested");
        
        var clientInformation = await _systemService.GetClientInformation("windows");

        return clientInformation.Version;

    }
    
    [HttpGet]
    [AllowAnonymous]
    [Route("ClientDownloadLocation/{osFamily}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public async Task<string> ClientDownloadLocation(string osFamily)
    {
        Logger.Debug("Client Download Location Requested");
        
        var clientInformation = await _systemService.GetClientInformation(osFamily);

        if (osFamily.ToLower() != "windows" && osFamily.ToLower() != "linux" && osFamily.ToLower() != "mac")
            throw new InvalidParameterException("osFamily","OS Family not supported");
            

        return clientInformation.DownloadLocation[osFamily.ToLower()];

    }
    
    [HttpGet]
    [AllowAnonymous]
    [Route("UpdateScript/{osFamily}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public async Task<ActionResult<string>> UpdateScript(string osFamily)
    {
        Logger.Debug("Update Script Requested");

        if(string.IsNullOrEmpty(osFamily))
            throw new Exception("OS Family not specified");

        try
        {
            return await _systemService.GetUpdateScriptAsync(osFamily);
        }
        catch (InvalidParameterException ex)
        {
            Logger.Warning("Bad Request: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Internal Server Error: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
}