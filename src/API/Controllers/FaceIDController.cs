using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Services;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FaceIDController: ApiBaseController
{   
    private  IFaceIDService FaceIDService { get; }
    
    public FaceIDController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IFaceIDService faceIDService,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        FaceIDService = faceIDService;
    }
    
    [HttpGet]
    [Route("info")]
    public async Task<ActionResult<ServiceInformation>> GetInfo()
    {
        
        return await FaceIDService.GetInfoAsync();

    }
    
    /// <summary>
    /// Check if the user is enabled to use the faceid service
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("enabled/{userId}")]
    public async Task<ActionResult<bool>> CheckUserEnabled(int userId)
    {
        try
        {
            var result = await FaceIDService.IsUserEnabledAsync(userId);
        
            return result;
            
        }catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error checking user enabled status");
            return StatusCode(500, "Internal server error");
        }


    }
    
    /// <summary>
    /// Enable the use to use faceId
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("enable/{userId}")]
    public async Task<ActionResult> EnableUser(int userId)
    {
        try
        {
            await FaceIDService.SetUserEnabledStatusAsync(userId, true);
        
            return Ok();
            
        }catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error enabling user faceid status");
            return StatusCode(500, "Internal server error");
        }


    }
    
    /// <summary>
    /// Disable the use of faceId 
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("disable/{userId}")]
    public async Task<ActionResult> DisableUser(int userId)
    {
        
        try
        {
            await FaceIDService.SetUserEnabledStatusAsync(userId, false);
        
            return Ok();
            
        }catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error enabling user faceid status");
            return StatusCode(500, "Internal server error");
        }

    }
    
    
}