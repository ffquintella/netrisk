using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Model;
using ServerServices.Interfaces;
using WebSite.Models;
using WebSite.Tools;

namespace WebSite.Controllers;

public class IRTEReportController(
    ILogger<PasswordController> logger,
    IMessagesService messagesService,
    LanguageService languageService,
    IIncidentResponsePlansService incidentResponsePlansService,
    IUsersService usersService) : Controller
{
    
    private ILogger<PasswordController> Logger { get; } = logger;
    private IMessagesService MessagesService { get; } = messagesService;
    private LanguageService Localizer { get; } = languageService;
    private IUsersService UsersService { get; } = usersService;
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = incidentResponsePlansService;

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
            return RedirectToAction("Error");
        }
        else
        {
            try
            {

                var id = Int32.Parse(key);
                
                var taskExecution = await IncidentResponsePlansService.GetTaskExecutionByIdAsync(id);
                
                if (taskExecution == null)
                {
                    return RedirectToAction("Error");
                }
                
                var task = await IncidentResponsePlansService.GetTaskByIdAsync(taskExecution.TaskId);
                
                if (task == null)
                {
                    return RedirectToAction("Error");
                }

                var incident = await IncidentResponsePlansService.GetIncidentByTaskIdAsync(task.Id);

                var model = new IRTEReportViewModel()
                {
                    TaskExecutionId = id,
                    IncidentDescription = incident.Description,
                    IncidentName = incident.Name,
                    TaskName = task.Name,
                    TaskDescription = task.Description ?? "",
                    TaskId = task.Id,
                    TaskNotes = task.Notes ?? "",
                    TaskConditionToProceed = task.ConditionToProceed ?? "",
                    TaskConditionToSkip = task.ConditionToSkip ?? "",
                    TaskCriteriaToSucceed = task.SuccessCriteria ?? "",
                    TaskCriteriaToFail  = task.FailureCriteria ?? "",
                    TaskCriteriaToComplete = task.CompletionCriteria ?? "",
                    TaskVerificationCriteria = task.VerificationCriteria ?? ""
                    
                };
                
                return View(model);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error on Index");
                return RedirectToAction("Error");
            }
        }
    }

    public async Task<IActionResult> DoReport(IRTEReportViewModel? vm)
    {

        switch (vm.Result)
        {
            case "succeed":
                await IncidentResponsePlansService.ChangeExecutionTaskSatusByIdAsync(vm.TaskExecutionId, (int)IntStatus.Completed);
                break;
            case "skip":
                await IncidentResponsePlansService.ChangeExecutionTaskSatusByIdAsync(vm.TaskExecutionId, (int)IntStatus.Skipped);
                break;
            case "fail":
                await IncidentResponsePlansService.ChangeExecutionTaskSatusByIdAsync(vm.TaskExecutionId, (int)IntStatus.Failed);
                break;
        }
        
        return View(vm);
    }

    public async Task<IActionResult> Error()
    {
        return View();
    }
    

    
}