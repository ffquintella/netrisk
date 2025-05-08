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
    
    
}