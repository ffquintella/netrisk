using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
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
    /// Creates a comment on a fix request, risk, vulnerability or host (T375). The author is the authenticated
    /// caller for every type: <c>UserId</c>, <c>Date</c> and <c>CommenterName</c> in the body are not read, and the
    /// caller must be allowed to read the record (403 without its permission; 404 when missing, out of scope, or a
    /// fix request the caller takes no part in).
    /// </summary>
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Comment))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Comment>> CreateAsync([FromBody] Comment? comment)
    {
        var user = GetUser();

        if (comment == null)
            return BadRequest(new { error = "invalid_parameter", ParameterName = "comment", Message = "A comment is required." });

        try
        {
            var created = await CommentsService.CreateUserCommentAsync(user, comment.Type, comment.ReplyTo,
                comment.IsAnonymous, comment.Text, comment.FixRequestId, comment.RiskId, comment.VulnerabilityId,
                comment.HostId);

            Logger.Information("User:{UserValue} created comment {CommentId} on {Type}", user.Value, created.Id,
                created.Type);
            return Ok(created);
        }
        catch (InvalidParameterException ex)
        {
            Logger.Warning("User:{UserValue} sent an invalid comment: {Message}", user.Value, ex.Message);
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException)
        {
            // Missing and outside the caller's entity scope are the same answer.
            Logger.Warning("User:{UserValue} commented on a record they cannot see", user.Value);
            return NotFound();
        }
        catch (UserNotAuthorizedException ex)
        {
            // 403, not 401: the session is valid, and to the clients a 401 means "not signed in" (AuthChallengeHandler).
            Logger.Warning("User:{UserValue} was refused a comment: {Message}", user.Value, ex.Message);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while user:{UserValue} created a comment", user.Value);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    /// <summary>
    /// Get the comments left on a host: <c>hosts</c>, and the host inside the caller's entity scope (404 otherwise).
    /// </summary>
    [PermissionAuthorize("hosts")]
    [HttpGet]
    [Route("host/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Comment>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Comment>>> GetHostComments(int id)
    {
        var user = GetUser();

        try
        {
            var comments = await CommentsService.GetHostCommentsAsync(id);
            Logger.Information("User:{UserValue} got host {HostId} comments", user.Value, id);
            return Ok(comments);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("User:{UserValue} asked for the comments of host {HostId}, which they cannot see",
                user.Value, id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while user:{UserValue} got host {HostId} comments", user.Value, id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get the comments on a fix request, for its requester, fix team, the finding's analyst or a holder of
    /// <c>vulnerabilities</c>. Missing, out of scope and not taking part are all 404.
    /// </summary>
    [HttpGet]
    [Route("fixrequest/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Comment>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Comment>>> Get(int id)
    {
        var user = GetUser();

        try
        {
            var comments = await CommentsService.GetFixRequestCommentsForUserAsync(user, id);
            Logger.Information("User:{UserValue} got fix request {FixRequestId} comments", user.Value, id);
            return Ok(comments);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while user:{UserValue} got fix request {FixRequestId} comments",
                user.Value, id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }


}