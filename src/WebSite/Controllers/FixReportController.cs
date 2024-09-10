using System.Globalization;
using System.Text;
using System.Text.Unicode;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MigraDoc.DocumentObjectModel;
using Model.Exceptions;
using ServerServices.Interfaces;
using Tools;
using WebSite.Models;
using System.Text.Json;
using Model;
using WebSite.Tools;

namespace WebSite.Controllers;

public class FixReportController(
    ILogger<PasswordController> logger,
    IFixRequestsService fixRequestsService,
    ITeamsService teamsService,
    ICommentsService commentsService,
    IMessagesService messagesService,
    LanguageService languageService,
    IUsersService usersService)
    : Controller
{
    private ILogger<PasswordController> Logger { get; } = logger;
    private IFixRequestsService FixRequestsService { get; } = fixRequestsService;
    private IUsersService UsersService { get; } = usersService;
    private ITeamsService TeamsService { get; } = teamsService;
    private LanguageService Localizer { get; } = languageService;
    private ICommentsService CommentsService { get; } = commentsService;
    
    private IMessagesService MessagesService { get; } = messagesService;


    public async Task<IActionResult> Index([FromQuery] string key = "")
    {
        var defaultCultures = new List<CultureInfo>()
        {
            new CultureInfo("pt-BR"),
            new CultureInfo("en-US"),
        };

        var cinfo = CultureInfo.GetCultures(CultureTypes.AllCultures);
        var cultureItems = cinfo.Where(x => defaultCultures.Contains(x))
            .Select(c => new SelectListItem { Value = c.Name, Text = c.DisplayName })
            .ToList();
        ViewData["Cultures"] = cultureItems;


        if (key == "")
        {
            return RedirectToAction("Find");
        }
        else
        {
            try
            {
                var fixRequest = await FixRequestsService.GetFixRequestAsync(key);
                //var vulnerability = await VulnerabilitiesService.GetById(fixRequest.VulnerabilityId);
                
                string description = "No description available";
                if(fixRequest.Vulnerability.Description != null)
                {
                    description = fixRequest.Vulnerability.Description;
                }
                string solution = "No solution available";
                if(fixRequest.Vulnerability.Solution != null)
                {
                    solution = fixRequest.Vulnerability.Solution;
                }

                string hostName = "No host name available";
                if(fixRequest.Vulnerability.Host != null && fixRequest.Vulnerability.Host.HostName != null)
                {
                    hostName = fixRequest.Vulnerability.Host.HostName;
                }
                
                
                var answers  = new List<SelectListItem>()
                {
                    new SelectListItem(Localizer["Fix"], "1"),
                    new SelectListItem(Localizer["Ask for more details"], "2"),
                    new SelectListItem(Localizer["Reject Fix"], "3"),
                    new SelectListItem(Localizer["Fixed"], "4"),
                };
                
                var comments = await CommentsService.GetFixRequestCommentsAsync(fixRequest.Id);
                
                var fixReportViewModel = new DoFixReportViewModel ()
                {
                    Key = key,
                    Title = fixRequest.Vulnerability.Title,
                    Description = description,
                    Solution = solution,
                    Score = fixRequest.Vulnerability.Score!.Value.ToString("F1"),
                    HostName = hostName,
                    IsTeamFix = fixRequest.IsTeamFix!.Value,
                    FixDate = DateOnly.FromDateTime(DateTime.Now),
                    Answers = answers,
                    Status = fixRequest.Status,
                    Comments = comments
                };
                
                if(fixRequest.IsTeamFix!.Value)
                {
                    var team = TeamsService.GetById(fixRequest.FixTeamId!.Value, true);
                    foreach (var user in team.Users)
                    {
                        fixReportViewModel.Fixers.Add(new SelectListItem(Encoding.UTF8.GetString(user.Username), user.Value.ToString()));
                    }
                }
                else
                {
                    fixReportViewModel.FixerEmail = fixRequest.SingleFixDestination!;
                }
   
                TempData["fixReportViewModel"] = JsonSerializer.Serialize(fixReportViewModel);

                return RedirectToAction("DoReport");
            }catch (DataNotFoundException )
            {
                Logger.LogError("FixRequest with identifier {key} not found", key);
                return RedirectToAction("Find");
            }

        }
        
        //return View();
    }

    public IActionResult Find(FindFixRequestViewModel vm)
    {
        if (vm.Id != string.Empty)
        {
            return RedirectToAction("Index", new { key = vm.Id });
        }
        
        return View();
    }
    
    public async Task<IActionResult> DoReport(DoFixReportViewModel? vm)
    {
        if (vm == null)
        {
            Logger.LogError("Null fixreport view model");
            return StatusCode(500);
        }
        if ( vm.FluxControl == "answering")
        {
            var fixRequest = await FixRequestsService.GetFixRequestAsync(vm.Key);
            
            string newFixStatus = "";
                    
            switch (vm.AnswerId)
            {
                case "0":
                    newFixStatus = "Open";
                    break;
                case "1":
                    newFixStatus = "Awaiting Fix";
                    break;
                case "2":
                    newFixStatus = "Awaiting Internal Response";
                    break;
                case "3":
                    newFixStatus = "Fix Not Possible";
                    break;
                case "4":
                    newFixStatus = "Fixed";
                    break;
                default:
                    newFixStatus = "Awaiting Fix";
                    break;
            }
            
            if ( vm.Comment != "")
            {
                if(vm.FixerId != "")
                {
                    var user = await UsersService.GetUserByIdAsync(int.Parse(vm.FixerId));
                    
                    await CommentsService.CreateCommentsAsync(
                        int.Parse(vm.FixerId),
                        DateTime.Now,
                        null,
                        "FixRequest",
                        false,
                        user!.Name,
                        vm.Comment,
                        fixRequest.Id,
                        null,
                        null,
                        null
                    );
                }
                else
                {
                    await CommentsService.CreateCommentsAsync(
                        null,
                        DateTime.Now,
                        null,
                        "FixRequest",
                        true,
                        vm.FixerEmail,
                        vm.Comment,
                        fixRequest.Id,
                        null,
                        null,
                        null
                    );
                }
                
            }
            
            int newStatus = 0;

            switch (vm.AnswerId)
            {
                case "0":
                    newStatus = (int)IntStatus.Open;
                    break;
                case "1":
                    newStatus = (int)IntStatus.AwaitingFix;
                    break;
                case "2":
                    newStatus = (int)IntStatus.AwaitingInternalResponse;
                    break;
                case "3":
                    newStatus = (int)IntStatus.FixNotPossible;
                    break;
                case "4":
                    newStatus = (int)IntStatus.Fixed;
                    break;
                default:
                    newStatus = (int)IntStatus.AwaitingFix;
                    break;
            }

            if(vm.IsTeamFix)
            {
                fixRequest.LastReportingUserId = int.Parse(vm.FixerId);
            }

            
            fixRequest.Status = newStatus;
            fixRequest.LastInteraction = DateTime.Now;
            fixRequest.FixDate = vm.FixDate.ToDateTime(new TimeOnly(0, 0));
            if (fixRequest.IsTeamFix == false)
            {
                fixRequest.SingleFixDestination = vm.FixerEmail;
            }
            await FixRequestsService.SaveFixRequestAsync(fixRequest);

            await MessagesService.SendMessageAsync("Your fix request #: "+ fixRequest.Id + " of vulnerability #: " + fixRequest.VulnerabilityId+" has been updated status: " + newFixStatus, fixRequest.RequestingUserId!.Value, 1, 1);
            

            
            vm.FluxControl = "donne";
        }
        
        if (vm.FluxControl == "")
        {
            vm = JsonSerializer.Deserialize<DoFixReportViewModel>((string)TempData["fixReportViewModel"]!);
            
            if (vm == null)
            {
                Logger.LogError("Null fixreport view model");
                return StatusCode(500);
            }
            
            vm.FluxControl = "answering";
        }




        return View(vm);
    }
}