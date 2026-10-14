using System.Diagnostics.Tracing;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.ClientData;
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
    public string Version()
    {
        Logger.Debug("Client Version Requested");

        var version = Assembly.GetExecutingAssembly().GetName().Version;

        string strVersion = "";
        
        if(version != null)
            strVersion = version.ToString();
        
        
        return strVersion;

    }
    
    [HttpGet]
    [AllowAnonymous]
    [Route("ClientDownloadLocation/{osFamily}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(string))]
    public async Task<ActionResult<string>> ClientDownloadLocation(string osFamily)
    {
        Logger.Debug("Client Download Location Requested");

        // Checked before the lookup, and answered as a 400 rather than thrown: nothing maps an
        // exception thrown here to a status code, so a throw reached the client as a 500.
        if (ClientPlatforms.IsRetired(osFamily))
        {
            Logger.Warning("Client download location refused for a retired client platform ({Platform})",
                ClientPlatforms.MacIntel);
            return BadRequest(ClientPlatforms.MacIntelUnsupportedMessage);
        }

        if (!ClientPlatforms.IsSupported(osFamily))
            return BadRequest(ClientPlatforms.UnknownPlatformMessage);

        var clientInformation = await _systemService.GetClientInformation(osFamily);

        return clientInformation.DownloadLocation[ClientPlatforms.Normalize(osFamily)];
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