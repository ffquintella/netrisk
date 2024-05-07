using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class CommentsController(
    Serilog.ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService
    ) : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// Get the user comments
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Comment>))]
    public async Task<ActionResult<List<Comment>>> Get([FromQuery] List<int?>? chats = null)
    {
        var user = GetUser();

        return new List<Comment>();

        //Logger.Information("User:{UserValue} listed messages", user.Value);
        //return Ok(await MessagesService.GetAllAsync(user.Value, chats));

    }
}