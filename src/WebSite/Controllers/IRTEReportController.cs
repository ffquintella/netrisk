using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Model;
using SyncContracts;
using WebSite.Models;
using WebSite.Tools;
using WebSiteData.Services;

namespace WebSite.Controllers;

/// <summary>
/// The public page an incident-response task assignee reaches from the task e-mail, to read the task
/// and report its outcome. The assignee has no website session, so the e-mailed key is the credential
/// (NR-2026-037): every action resolves the execution from the key and never from an id the request
/// carries, and every way a key can fail ends on the same Error page.
/// </summary>
public class IRTEReportController(
    ILogger<IRTEReportController> logger,
    LanguageService languageService,
    ILocalIrpService irpService) : Controller
{
    private ILogger<IRTEReportController> Logger { get; } = logger;
    private LanguageService Localizer { get; } = languageService;
    private ILocalIrpService IrpService { get; } = irpService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? key = null)
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

        // Shape first: bounds the input before it is hashed or queried, and refuses an integer id.
        if (!IrpTaskReportLink.IsWellFormedKey(key))
            return RejectKey();

        try
        {
            var taskExecution = await IrpService.GetTaskExecutionByReportKeyAsync(key!);
            if (taskExecution == null)
                return RejectKey();

            var task = await IrpService.GetTaskAsync(taskExecution.TaskId);
            if (task == null)
                return RejectKey();

            var incident = await IrpService.GetIncidentForTaskAsync(task.Id);

            var model = new IRTEReportViewModel()
            {
                Key = key!,
                IncidentDescription = incident?.Description ?? "",
                IncidentName = incident?.Name ?? "",
                TaskName = task.Name,
                TaskDescription = task.Description ?? "",
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoReport(IRTEReportViewModel? vm)
    {
        if (vm == null)
            return BadRequest();

        if (!IrpTaskReportLink.IsWellFormedKey(vm.Key))
            return RejectKey();

        var newStatus = MapResult(vm.Result);
        if (newStatus == null)
            return View(new IRTEReportViewModel());

        try
        {
            // The service re-resolves the key and derives the execution from it; nothing else the form
            // posted reaches the queued change.
            if (!await IrpService.QueueTaskReportAsync(vm.Key, newStatus.Value))
                return RejectKey();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error on DoReport");
            return RedirectToAction("Error");
        }

        return View(new IRTEReportViewModel { Result = vm.Result });
    }

    public IActionResult Error()
    {
        return View();
    }

    private static int? MapResult(string? result) => result switch
    {
        "succeed" => (int)IntStatus.Completed,
        "skip" => (int)IntStatus.Skipped,
        "fail" => (int)IntStatus.Failed,
        _ => null
    };

    /// <summary>
    /// One response for a key that is malformed, unknown, expired, used or bound to nothing on this
    /// site, so the page is no oracle for which. The key itself is never logged.
    /// </summary>
    private RedirectToActionResult RejectKey()
    {
        Logger.LogWarning("Rejected an incident task report request: the report key did not resolve");
        return RedirectToAction("Error");
    }
}
