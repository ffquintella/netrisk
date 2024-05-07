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
    ICommentsService commentsService,
    IUsersService usersService
    ) : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private ICommentsService CommentsService { get; } = commentsService;
    
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

        //return new List<Comment>();

        Logger.Information("User:{UserValue} listed comments", user.Value);
        return Ok(await CommentsService.GetUserCommentsAsync(user.Value));

    }
    
    /// <summary>
    /// Get the user comments
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Route("fixrequest/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Comment>))]
    public async Task<ActionResult<List<Comment>>> Get(int id)
    {
        var user = GetUser();

        //return new List<Comment>();

        Logger.Information("User:{UserValue} got fix request comments", user.Value);
        return Ok(await CommentsService.GetFixRequestCommentsAsync(id));

    }


}