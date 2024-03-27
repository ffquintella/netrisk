using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class MessagesController(
    Serilog.ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    
    
    
}