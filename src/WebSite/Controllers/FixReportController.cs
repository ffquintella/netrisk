using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Model.Exceptions;
using ServerServices.Interfaces;
using WebSite.Models;

namespace WebSite.Controllers;

public class FixReportController : Controller
{
    private ILogger<PasswordController> Logger { get; }
    private IFixRequestsService FixRequestsService { get; }
    private IUsersService UsersService { get; }

    public FixReportController(ILogger<PasswordController> logger,
        IFixRequestsService fixRequestsService,
        IUsersService usersService)
    {
        Logger = logger;
        FixRequestsService = fixRequestsService;
        UsersService = usersService;
    }
    
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
            
                var fixReportViewModel = new DoFixReportViewModel
                {
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
        return View(vm);
    }
}