using DAL.Entities;
using ILogger = Serilog.ILogger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices;
using ServerServices.Interfaces;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class AssetsController: ApiBaseController
{
    private IAssetsService _assets;
    
    public AssetsController(ILogger logger, 
        IAssetsService assetsService,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _assets = assetsService;
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Asset>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Asset>> GetAll([FromQuery] string? status = null)
    {

        var user = GetUser();
        var assets = new List<Asset>();

        try
        {
            Logger.Information($"User:{user.Value} listed all assets");
            assets = _assets.GetAssets();

            return Ok(assets);
        }
        catch (UserNotAuthorizedException ex)
        {
            
            Logger.Warning($"The user {user.Name} is not authorized to see assets message: {ex.Message}");
            return this.Unauthorized();
        }
        
        
    }
}