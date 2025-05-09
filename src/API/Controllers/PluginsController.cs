using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Services;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class PluginsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IPluginsService pluginsService,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IPluginsService PluginsService { get; } = pluginsService;
    
    [HttpGet]
    [Route("info")]
    public async Task<ActionResult<ServiceInformation>> GetInfo()
    {
        
        return await PluginsService.GetInfoAsync();

    }
    
    [HttpGet]
    [Route("reload")]
    public async Task<ActionResult<bool>> Reload()
    {
        await PluginsService.LoadPluginsAsync();
        return true;
    }
}