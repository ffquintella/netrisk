using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Model;
using WebSite.Models;
using WebSite.Tools;
using WebSiteData.Services;

namespace WebSite.Controllers;

public class IRTEReportController(
    ILogger<IRTEReportController> logger,
    LanguageService languageService,
    ILocalIrpService irpService) : Controller
{
    private ILogger<IRTEReportController> Logger { get; } = logger;
    private LanguageService Localizer { get; } = languageService;
    private ILocalIrpService IrpService { get; } = irpService;

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

        try
        {
            var id = int.Parse(key);

            var taskExecution = await IrpService.GetTaskExecutionAsync(id);
            if (taskExecution == null)
                return RedirectToAction("Error");

            var task = await IrpService.GetTaskAsync(taskExecution.TaskId);
            if (task == null)
                return RedirectToAction("Error");

            var incident = await IrpService.GetIncidentForTaskAsync(task.Id);

            var model = new IRTEReportViewModel()
            {
                TaskExecutionId = id,
                IncidentDescription = incident?.Description ?? "",
                IncidentName = incident?.Name ?? "",
                TaskName = task.Name,
                TaskDescription = task.Description ?? "",
                TaskId = task.Id,
                TaskNotes = task.Notes ?? "",
                TaskConditionToProceed = task.ConditionToProceed ?? "",
                TaskConditionToSkip = task.ConditionToSkip ?? "",
                TaskCriteriaToSucceed = task.SuccessCriteria ?? "",
                TaskCriteriaToFail = task.FailureCriteria ?? "",
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

    public async Task<IActionResult> DoReport(IRTEReportViewModel? vm)
    {
        if (vm == null)
            return BadRequest();

        switch (vm.Result)
        {
            case "succeed":
                await IrpService.QueueTaskStatusChangeAsync(vm.TaskExecutionId, (int)IntStatus.Completed);
                break;
            case "skip":
                await IrpService.QueueTaskStatusChangeAsync(vm.TaskExecutionId, (int)IntStatus.Skipped);
                break;
            case "fail":
                await IrpService.QueueTaskStatusChangeAsync(vm.TaskExecutionId, (int)IntStatus.Failed);
                break;
        }

        return View(vm);
    }

    public IActionResult Error()
    {
        return View();
    }
}
