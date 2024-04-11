using System.Globalization;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Model.Exceptions;
using ServerServices.Interfaces;
using Tools;
using WebSite.Models;

namespace WebSite.Controllers;

public class FixReportController(
    ILogger<PasswordController> logger,
    IFixRequestsService fixRequestsService,
    IUsersService usersService)
    : Controller
{
    private ILogger<PasswordController> Logger { get; } = logger;
    private IFixRequestsService FixRequestsService { get; } = fixRequestsService;
    private IUsersService UsersService { get; } = usersService;

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
            
                TempData["VRequest"] = RandomGenerator.RandomString(8);

                string hostName = "No host name available";
                if(fixRequest.Vulnerability.Host != null && fixRequest.Vulnerability.Host.HostName != null)
                {
                    hostName = fixRequest.Vulnerability.Host.HostName;
                }
                
                var fixReportViewModel = new DoFixReportViewModel
                {
                    Title = fixRequest.Vulnerability.Title,
                    Description = description,
                    Solution = solution,
                    VRequest = (string)TempData["VRequest"]!,
                    Score = fixRequest.Vulnerability.Score!.Value.ToString("F1"),
                    HostName = hostName
                };

                return RedirectToAction("DoReport", fixReportViewModel);
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
    
    public IActionResult DoReport(DoFixReportViewModel vm)
    {
        if (TempData["VRequest"] == null)
        {
            TempData["ErrorMessage"] = "Invalid request.";
            RedirectToAction("Error", "Home");
        }
        var vrequest = (string)TempData["VRequest"]!;
        
        if(vrequest != vm.VRequest)
        {
            TempData["ErrorMessage"] = "Invalid request.";
            RedirectToAction("Error", "Home");
        }
        
        return View(vm);
    }
}