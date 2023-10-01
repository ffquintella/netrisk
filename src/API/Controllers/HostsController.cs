using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class HostsController: ApiBaseController
{
    public HostsController(ILogger logger, IHttpContextAccessor httpContextAccessor, IUsersService usersService) 
        : base(logger, httpContextAccessor, usersService)
    {
    }
    
    
}