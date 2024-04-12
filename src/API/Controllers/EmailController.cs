using System.Net.Mime;
using System.Text;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Model;
using Model.Vulnerability;
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
    ILocalizationService localization)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IEmailService EmailService { get; } = emailService;
    private ITeamsService TeamsService { get; } = teamsService;
    private IFixRequestsService FixRequestsService { get; } = fixRequestsService;
    private IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    private ILocalizationService Localization { get; } = localization;
    private IConfiguration Configuration { get; } = configuration;


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
                // Get the team
                var team = TeamsService.GetById(fixRequest.FixTeamId);
                if (team.Name.ToLower() == "all")
                {
                    //Send to every one
                    userList = await UsersService.GetAllAsync();
                }
                else
                {
                    //Send to the team
                    userList = await UsersService.GetByTeamIdAsync(fixRequest.FixTeamId);
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
                Comments = fixRequest.Comments,
                RequestingUserId = user.Value,
                CreationDate = DateTime.Now,
                Identifier = Guid.NewGuid().ToString(),
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
                
            var result = await FixRequestsService.CreateFixRequestAsync(fixRequestEntity);
            
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
                    Identifier = result.Identifier,
                    Description = vulnerability.Description!,
                    Solution = vulnerability.Solution!,
                    Score = score.ToString("F1"),
                    ReportLink = Configuration["website:protocol"] + "://" + Configuration["website:host"] + ":" + Configuration["website:port"] + "/FixReport?key=" + result.Identifier
                    
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