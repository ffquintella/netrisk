using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Services;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FaceIDController: ApiBaseController
{   
    private readonly IFaceIDService _faceIDService;
    
    public FaceIDController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IFaceIDService faceIDService,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _faceIDService = faceIDService;
    }
    
    [HttpGet]
    [Route("info")]
    public async Task<ActionResult<ServiceInformation>> GetInfo()
    {
        
        return await _faceIDService.GetInfoAsync();

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
        
        return false;

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
        
        return Ok();

    }
    
    /// <summary>
    /// Disable the use of faceId 
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("enable/{userId}")]
    public async Task<ActionResult> DisableUser(int userId)
    {
        
        return Ok();

    }
    
    
}