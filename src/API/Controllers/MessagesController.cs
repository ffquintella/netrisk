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
    public async Task<ActionResult<List<Message>>> Get([FromQuery] List<int?>? chats = null)
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} listed messages", user.Value);
        return Ok(await MessagesService.GetAllAsync(user.Value, chats));
        
    }
    
    /// <summary>
    /// Counts how many messages the user has
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("count")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    public async Task<ActionResult<int>> GetCount([FromQuery] List<int?>? chats = null)
    {
        var user = GetUser();
        
        var messages = await MessagesService.GetAllAsync(user.Value, chats);
        return Ok(messages.Count);
        
    }
    
    /// <summary>
    /// Check if the user has any unread messages
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("has_unread")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(bool))]
    public async Task<ActionResult<bool>> HasUnreadMessages([FromQuery] List<int?>? chats = null)
    {
        var user = GetUser();
        var hasUnreadMessages = await MessagesService.HasUnreadMessagesAsync(user.Value, chats);
        return Ok(hasUnreadMessages);
        
    }
    
    /// <summary>
    /// Mark a message as read
    /// </summary>
    /// <returns></returns>
    [HttpPatch]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Message>))]
    public async Task<ActionResult<string>> ReadMessage(int id, [FromQuery] string operation)
    {
        var user = GetUser();
        if(operation != "read")
            return BadRequest("Invalid operation");
        
        Logger.Information("User:{UserValue} read the message", user.Value);
        
        var message = await MessagesService.GetMessageAsync(id);
        message.ReceivedAt = DateTime.Now;
        message.Status = (int)IntStatus.Read;
        await MessagesService.SaveMessageAsync(message);
        return Ok("Message read");
        
    }
    
    
}