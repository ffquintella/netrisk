using DAL.Entities;
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
    IMessagesService messagesService,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    
    
    private IMessagesService MessagesService { get; } = messagesService;
    
    /// <summary>
    /// Get the user messages
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Message>))]
    public async Task<ActionResult<List<Message>>> Get()
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} listed messages", user.Value);
        return Ok(await MessagesService.GetAllAsync(user.Value));
        
    }
    
    
}