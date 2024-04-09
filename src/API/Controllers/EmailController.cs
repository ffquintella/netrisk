using System.Net.Mime;
using System.Text;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
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
    ILocalizationService localization)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public IEmailService EmailService { get; } = emailService;
    public ITeamsService TeamsService { get; } = teamsService;
    
    public ILocalizationService Localization { get; } = localization;


    [HttpPost]
    [Route("Vulnerability/FixRequest")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<string>> SendVulnerabilityFixRequestMail([FromBody] FixRequest fixRequest, [FromQuery] bool sendToGroup = false )
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
            
            foreach (var emailDestination in detinationList)
            {
                //Email Parameters
                var emailParameters = new VulnerabilityFound() {
                };
                
                //Send email
                EmailService.SendEmailAsync(emailDestination, localizer["WARNING - Vulnerability Found"], "VulnerabilityFound", user.Lang.ToLower(), emailParameters);
                
            }
            

            return Ok("ok");
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting configuration: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
}