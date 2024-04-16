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
using WebSite.Tools;

namespace WebSite.Controllers;

public class FixReportController(
    ILogger<PasswordController> logger,
    IFixRequestsService fixRequestsService,
    ITeamsService teamsService,
    ICommentsService commentsService,
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
                    new SelectListItem(Localizer["Fixed"], "3"),
                };
                
                var comments = await CommentsService.GetFixRequestCommentsAsync(fixRequest.Id);
                
                var fixReportViewModel = new DoFixReportViewModel ()
                {
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
            }catch (DataNotFoundException ex)
            {
                Logger.LogError("FixRequest with identifier {key} not found", key);
                return RedirectToAction("Find");
            }

        }
        
        return View();
    }

    public IActionResult Find(FindFixRequestViewModel vm)
    {
        if (vm.Id != string.Empty)
        {
            return RedirectToAction("Index", new { key = vm.Id });
        }
        
        return View();
    }
    
    public IActionResult DoReport(DoFixReportViewModel? vm)
    {
        if (vm == null || vm.Title == "")
        {
            vm = JsonSerializer.Deserialize<DoFixReportViewModel>((string)TempData["fixReportViewModel"]!);
        }
        
        
        return View(vm);
    }
}