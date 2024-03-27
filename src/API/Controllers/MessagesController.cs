using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model;
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
    
    /// <summary>
    /// Mark a message as read
    /// </summary>
    /// <returns></returns>
    [HttpPut]
    [Route("{id}/read")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Message>))]
    public async Task<ActionResult<List<Message>>> ReadMessage(int id)
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} read the message", user.Value);
        
        var message = await MessagesService.GetMessageAsync(id);
        message.ReceivedAt = DateTime.Now;
        message.Status = (int)IntStatus.Read;
        await MessagesService.SaveMessageAsync(message);
        return Ok(await MessagesService.GetAllAsync(user.Value));
        
    }
    
    
}