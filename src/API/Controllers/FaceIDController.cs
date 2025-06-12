using Contracts.Exceptions;
using Microsoft.AspNetCore.Authentication;
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
            var userAccount = UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);

            if (userAccount == null)
                throw new UserNotFoundException("User not found");

            var loggedUser = await UsersService.FindEnabledActiveUserAsync(userAccount);

            if (loggedUser == null)
                throw new UserNotFoundException("User not found");

            var descriptor = await FaceIDService.SaveFaceIdAsync(userId, faceData, loggedUser.Value);

            return Ok(descriptor);

        }
        catch (FaceDetectionException e)
        {
            Logger.Error(e, "Error no face found while saving user faceid");
            return StatusCode(408, "No face found");
        }
        catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error saving user faceid");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("transactions/{userId}/start")]
    public async Task<ActionResult<FaceTransactionData>> StartTransaction(int userId)
    {
        try
        {
            var loggedAccount = UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
            if (loggedAccount == null)
                throw new UserNotFoundException("User not found");
            
            var requestedUser = await UsersService.GetUserByIdAsync(userId);
            
            if (requestedUser == null)
                throw new UserNotFoundException("User not found");
            
            if(!string.Equals(requestedUser.Login, loggedAccount, StringComparison.CurrentCultureIgnoreCase))
                return Unauthorized("You are not allowed to start a transaction for this user");
            
            if (!await FaceIDService.IsUserEnabledAsync(userId)) 
                return BadRequest("User is not enabled for FaceID");
            
            if (!await FaceIDService.UserHasFaceSetAsync(userId))
                return BadRequest("User is not enabled for FaceID");
            
            var transaction = await FaceIDService.StartTransactionAsync(userId);
            
            return Ok(transaction);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error starting transaction for user {UserId}", userId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    [Authorize(Policy = "RequireValidUser")]
    [Route("transactions/{userId}/commit")]
    public async Task<ActionResult<FaceToken>> CommitTransaction(int userId, [FromBody] FaceTransactionData faceTData, [FromQuery] string transactionObjectType = "", [FromQuery] string? transactionObjectId = null)
    {
        try
        {
            var result = await FaceIDService.CommitTransactionAsync(userId, faceTData, transactionObjectType);
            return result;
        }
        catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (FaceDetectionException e)
        {
            Logger.Error(e, "Error no face found while committing transaction for user {UserId}", userId);
            return StatusCode(408, "No face found");
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error commiting transaction for user {UserId}", userId);
            return StatusCode(500, "Internal server error");
        }
    }
    
    [HttpPost]
    [Authorize(Policy = "RequireValidUser")]
    [Route("transactions/validate/{transactionId}")]
    public async Task<ActionResult<bool>> ValidateTransactionToken(string transactionId, [FromBody] FaceToken token)
    {
        try
        {
            var loggedAccount = UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
            if (loggedAccount == null)
                throw new UserNotFoundException("User not found");

            var requestedUser = await UsersService.GetUserAsync(loggedAccount);

            if (requestedUser == null)
                throw new UserNotFoundException("User not found");

            var result = await FaceIDService.FaceTokenIsValidAsync(requestedUser.Value, token, transactionId);
            return result;
        }
        catch (UserNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (ArgumentException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error verifing transaction token");
            return StatusCode(500, "Internal server error");
        }
    }

}