using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Vulnerability;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FixRequestController: ApiBaseController
{
    private IFixRequestsService FixRequestsService { get; }
    private ICommentsService CommentsService { get; }
    
    public FixRequestController(ILogger logger, IHttpContextAccessor httpContextAccessor, IUsersService usersService, 
        IFixRequestsService fixRequestsService, ICommentsService commentsService) 
        : base(logger, httpContextAccessor, usersService)
    {
        FixRequestsService = fixRequestsService;
        CommentsService = commentsService;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FixRequest>>> GetAll()
    {

        var user = GetUser();
        if(!user.Admin) return Unauthorized("Only admins can list all fix requests");

        try
        {
            Logger.Information("User:{User} listed all fix requests", user.Value);
            var requests = await FixRequestsService.GetAllFixRequestAsync();

            return Ok(requests);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing fix requests: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPost]
    [PermissionAuthorize("vulnerabilities_create")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FixRequest>>> Create([FromBody] FixRequestDto fixRequest, [FromQuery] bool sendToGroup = false)
    {

        var user = GetUser();
        //if(!user.Admin) return Unauthorized("Only admins can create fix requests");

        try
        {
            Logger.Information("User:{User} created a fix request", user.Value);

            var coment = new Comment()
            {
                Text = fixRequest.Comments,
                Date = DateTime.Today,
                CommenterName = user.Name,
                IsAnonymous = 0,
                User = user
            };
            
            
            /*var comment = await CommentsService.CreateCommentsAsync(
                user.Value,
                DateTime.Now, 
                null,
                "FixRequest",
                false, 
                user.Name,
                fixRequest.Comments,
                null,
                null,
                fixRequest.VulnerabilityId,
                null
                );
            */
            var comments = new List<Comment>();
            
            
            var comment = new Comment()
            {
                Text = fixRequest.Comments,
                Date = DateTime.Today,
                CommenterName = user.Name,
                IsAnonymous = 0,
                User = user
            };
            
            
            comments.Add(comment);
            
            
            var fr = new FixRequest()
            {
                Comments = [],
                VulnerabilityId = fixRequest.VulnerabilityId,
                CreationDate = DateTime.Now,
                IsTeamFix = sendToGroup,
                FixTeamId = fixRequest.FixTeamId,
                Id = 0,
                Identifier = Guid.NewGuid().ToString(),
                LastInteraction = DateTime.Today
            };
            
            var requests = await FixRequestsService.CreateFixRequestAsync(fr);

            return Ok(requests);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating fix requests: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }

    
}