using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.FaceID;
using Model.Services;
using ServerServices.Interfaces;
using Tools.User;
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
    [Authorize(Policy = "RequireAdminOnly")]
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
    
    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("faceSet/{userId}")]
    public async Task<ActionResult<bool>> CheckUserHasFaceSet(int userId)
    {
        try
        {
            var result = await FaceIDService.UserHasFaceSetAsync(userId);
        
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
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("enable/{userId}")]
    public async Task<ActionResult> EnableUser(int userId)
    {
        try
        {
            var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
            
            if(userAccount == null) 
                throw new UserNotFoundException("User not found");
            
            var loggedUser = await UsersService.FindEnabledActiveUserAsync(userAccount);
            
            if(loggedUser == null)
                throw new UserNotFoundException("User not found");

            
            await FaceIDService.SetUserEnabledStatusAsync(userId, true, loggedUser.Value);
        
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
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("disable/{userId}")]
    public async Task<ActionResult> DisableUser(int userId)
    {
        
        try
        {
            var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
            
            if(userAccount == null) 
                throw new UserNotFoundException("User not found");
            
            var loggedUser = await UsersService.FindEnabledActiveUserAsync(userAccount);
            
            if(loggedUser == null)
                throw new UserNotFoundException("User not found");

            
            await FaceIDService.SetUserEnabledStatusAsync(userId, false, loggedUser.Value);
        
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
    [HttpPost]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("save/{userId}")]
    public async Task<ActionResult> SaveFaceAsync(int userId, [FromBody] FaceData faceData)
    {
        try
        {
            var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
            
            if(userAccount == null) 
                throw new UserNotFoundException("User not found");
            
            var loggedUser = await UsersService.FindEnabledActiveUserAsync(userAccount);
            
            if(loggedUser == null)
                throw new UserNotFoundException("User not found");
            
            var descriptor = await FaceIDService.SaveFaceIdAsync(userId, faceData, loggedUser.Value);
        
            return Ok(descriptor);
            
        }catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error saving user faceid");
            return StatusCode(500, "Internal server error");
        }
    }


}