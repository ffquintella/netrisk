using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        
        var clientInformation = await _systemService.GetClientInformation();

        return clientInformation.Version;

    }
}