using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Plugins;
using Model.Services;
using ServerServices.Interfaces;
using ServerServices.Plugins;
using ServerServices.Services;
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
    [Route("")]
    public async Task<ActionResult<List<PluginInfo>>> List()
    {

        return await PluginsService.GetPluginsAsync();

    }
    
    [HttpGet]
    [Route("info")]
    public async Task<ActionResult<ServiceInformation>> GetInfo()
    {
        
        return await PluginsService.GetInfoAsync();

    }
    
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpGet]
    [Route("reload")]
    public async Task<ActionResult<bool>> Reload()
    {
        await PluginsService.LoadPluginsAsync();
        return true;
    }
    
    [HttpGet]
    [Route("exists/{pluginName}")]
    public async Task<ActionResult<bool>> PluginExists(string pluginName)
    {
        return await PluginsService.PluginExistsAsync(pluginName);
    }
    
    [HttpGet]
    [Route("is_enabled/{pluginName}")]
    public async Task<ActionResult<bool>> PluginIsEnabled(string pluginName)
    {
        if(! await PluginsService.PluginExistsAsync(pluginName))
        {
            return NotFound();
        }
        
        return await PluginsService.PluginIsEnabledAsync(pluginName);
    }
    
    /// <summary>
    /// Installs a plugin package (.zip) uploaded from the administration screen: the archive is
    /// unpacked into the host's <c>Plugins/&lt;package&gt;</c> directory and the plugin surface is
    /// reloaded, so no redeploy is needed.
    /// </summary>
    /// <remarks>
    /// Administrator-only, and that is the whole access control: a plugin runs in the API process
    /// with the API's authority, so being able to upload one is being able to run code on the
    /// server. It is the same privilege as copying a DLL into the directory by hand, which is what
    /// this replaces. A newly installed plugin arrives disabled.
    /// </remarks>
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpPost]
    [Route("upload")]
    [RequestSizeLimit(PluginPackageInstaller.MaxPackageBytes)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PluginInstallResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(PluginInstallResult))]
    public async Task<ActionResult<PluginInstallResult>> Upload(IFormFile? file)
    {
        var user = GetUser();

        if (file == null || file.Length == 0)
        {
            Logger.Warning("User:{UserValue} attempted a plugin upload with no file", user.Value);
            return BadRequest(new PluginInstallResult
            {
                Success = false,
                Message = "No file was uploaded."
            });
        }

        Logger.Information("User:{UserValue} is uploading plugin package '{FileName}' ({Length} bytes)",
            user.Value, file.FileName, file.Length);

        await using var stream = file.OpenReadStream();

        var result = await PluginsService.InstallPluginPackageAsync(stream, file.FileName);

        if (!result.Success)
        {
            Logger.Warning("Plugin package '{FileName}' from user:{UserValue} was refused: {Message}",
                file.FileName, user.Value, result.Message);
            return BadRequest(result);
        }

        Logger.Information("User:{UserValue} installed plugin package {Package}", user.Value, result.PackageName);

        return Ok(result);
    }

    [Authorize(Policy = "RequireAdminOnly")]
    [HttpGet]
    [Route("enable/{pluginName}")]
    public async Task<ActionResult> EnablePlugin(string pluginName)
    {
        if(! await PluginsService.PluginExistsAsync(pluginName))
        {
            return NotFound();
        }
        
        await PluginsService.SetPluginEnabledStatusAsync(pluginName, true);
        
        return Ok();
    }
    
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpGet]
    [Route("disable/{pluginName}")]
    public async Task<ActionResult> DisablePlugin(string pluginName)
    {
        if(! await PluginsService.PluginExistsAsync(pluginName))
        {
            return NotFound();
        }
        
        await PluginsService.SetPluginEnabledStatusAsync(pluginName, false);
        return Ok();
    }
    
}