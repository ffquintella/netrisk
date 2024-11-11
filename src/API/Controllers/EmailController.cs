using System.Net.Mime;
using System.Text;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Model;
using Model.DTO;
using ServerServices.EmailTemplates;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class EmailController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IEmailService emailService,
    ITeamsService teamsService,
    IFixRequestsService fixRequestsService,
    IVulnerabilitiesService vulnerabilitiesService,
    IConfiguration configuration,
    ICommentsService commentsService,
    ILocalizationService localization)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IEmailService EmailService { get; } = emailService;
    private ITeamsService TeamsService { get; } = teamsService;
    private IFixRequestsService FixRequestsService { get; } = fixRequestsService;
    private IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    private ILocalizationService Localization { get; } = localization;
    private IConfiguration Configuration { get; } = configuration;
    private ICommentsService CommentsService { get; } = commentsService;


    [HttpPost]
    [Route("Vulnerability/Update/{fixRequestId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<string>> SendVulnerabilityUpdateMail(int fixRequestId, [FromBody] string newComment)
    {
        var user = GetUser();
        try
        {
            Logger.Information("User:{User} sent vulnerability update", user.Value);

            var fixRequest = await FixRequestsService.GetByIdAsync(fixRequestId);
            var vulnerability = VulnerabilitiesService.GetById(fixRequest.VulnerabilityId, true);
            var localizer = Localization.GetLocalizer();
            
            
            var emailParameters = new VulnerabilityUpdate() {
                VulnerabilityTitle = vulnerability.Title,
                Identifier = Guid.NewGuid().ToString(),
                ReportLink = Configuration["website:protocol"] + "://" + Configuration["website:host"] + ":" + Configuration["website:port"] + "/FixReport?key=" + fixRequest.Identifier,
                Comment = newComment
            };
            
            if (user.Lang == null)
            {
                user.Lang = "en";
            }
            
            
            if(fixRequest.IsTeamFix != null && fixRequest.IsTeamFix.Value)
            {
                if(fixRequest.FixTeamId == null) return BadRequest("FixTeamId is required for group fix request");
                var team = TeamsService.GetById(fixRequest.FixTeamId.Value);
                var userList = await UsersService.GetByTeamIdAsync(fixRequest.FixTeamId.Value);
                foreach (var userD in userList)
                {
                    await EmailService.SendEmailAsync(Encoding.UTF8.GetString(userD.Email), localizer["Vulnerability Update"], "VulnerabilityUpdate", user.Lang.ToLower(), emailParameters);
                }
            }
            else
            {
                if(fixRequest.SingleFixDestination == null) return BadRequest("SingleFixDestination is required for single fix request update email");
                await EmailService.SendEmailAsync(fixRequest.SingleFixDestination, localizer["Vulnerability Update"], "VulnerabilityUpdate", user.Lang.ToLower(), emailParameters);
            }
            
            return Ok("ok");
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while sending vulnerability fix request update email: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }


    [HttpPost]
    [Route("Vulnerability/FixRequest")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<string>> SendVulnerabilityFixRequestMail([FromBody] FixRequestDto fixRequest, [FromQuery] bool sendToGroup = false )
    {

        var user = GetUser();
        var userList = new List<User>();
        var detinationList = new List<string>();
        

        try
        {
            Logger.Information("User:{User} sent vulnerability fix request", user.Value);

            if (sendToGroup)
            {

                if (fixRequest.FixTeamId == null) return BadRequest("FixTeamId is required for group fix request");
                // Get the team
                var team = TeamsService.GetById(fixRequest.FixTeamId.Value);
                if (team.Name.ToLower() == "all")
                {
                    //Send to every one
                    userList = await UsersService.GetAllAsync();
                }
                else
                {
                    //Send to the team
                    userList = await UsersService.GetByTeamIdAsync(fixRequest.FixTeamId.Value);
                }

                foreach (var userD in userList)
                {
                    detinationList.Add(Encoding.UTF8.GetString(userD.Email));
                }
                
            }
            else
            {
                detinationList.Add(fixRequest.Destination);
            }

            var localizer = Localization.GetLocalizer();
            
            var fixRequestEntity = new FixRequest()
            {
                VulnerabilityId = fixRequest.VulnerabilityId,
                RequestingUserId = user.Value,
                CreationDate = DateTime.Now,
                Identifier = fixRequest.Identifier,
                Status = (int) IntStatus.Open
                
            };
            

            if (sendToGroup)
            {
                fixRequestEntity.FixTeamId = fixRequest.FixTeamId;
                fixRequestEntity.IsTeamFix = true;
            }
            else
            {
                fixRequestEntity.SingleFixDestination = fixRequest.Destination;
                fixRequestEntity.IsTeamFix = false;
            }
            
            
            var vulnerability = VulnerabilitiesService.GetById(fixRequest.VulnerabilityId, true);
            
            var serverName = "";
            if(vulnerability.Host != null && vulnerability.Host.HostName != null)
            {
                serverName = vulnerability.Host.HostName;
            }

            double score = 0;
            if (vulnerability.Score != null)
            {
                score = vulnerability.Score.Value;
            }
            
            foreach (var emailDestination in detinationList)
            {
                //Email Parameters
                var emailParameters = new VulnerabilityFound() {
                    VulnerabilityTitle = vulnerability.Title,
                    Server = serverName,
                    Identifier = Guid.NewGuid().ToString(),
                    Description = vulnerability.Description!,
                    Solution = vulnerability.Solution!,
                    Score = score.ToString("F1"),
                    ReportLink = Configuration["website:protocol"] + "://" + Configuration["website:host"] + ":" + Configuration["website:port"] + "/FixReport?key=" + fixRequest.Identifier
                    
                };
                
                if (user.Lang == null)
                {
                    user.Lang = "en";
                }
                
                //Send email
                await EmailService.SendEmailAsync(emailDestination, localizer["WARNING - Vulnerability Found"], "VulnerabilityFound", user.Lang.ToLower(), emailParameters);
                
            }
            

            return Ok("ok");
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating fix request: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
}