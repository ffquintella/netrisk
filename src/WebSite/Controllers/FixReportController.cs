using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SyncContracts;
using WebSite.Models;
using System.Text.Json;
using Model;
using WebSite.Tools;
using WebSiteData.Services;

namespace WebSite.Controllers;

public class FixReportController(
    ILogger<FixReportController> logger,
    ILocalFixReportService fixReportService,
    ILocalUserService userService,
    LanguageService languageService)
    : Controller
{
    private ILogger<FixReportController> Logger { get; } = logger;
    private ILocalFixReportService FixReportService { get; } = fixReportService;
    private ILocalUserService UserService { get; } = userService;
    private LanguageService Localizer { get; } = languageService;

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

        var fixRequest = await FixReportService.GetByIdentifierAsync(key);
        if (fixRequest == null)
        {
            Logger.LogError("FixRequest with identifier {key} not found", key);
            return RedirectToAction("Find");
        }

        var description = fixRequest.VulnDescription ?? "No description available";
        var solution = fixRequest.VulnSolution ?? "No solution available";
        var hostName = fixRequest.HostName ?? "No host name available";

        var answers = new List<SelectListItem>()
        {
            new SelectListItem(Localizer["Fix"], "1"),
            new SelectListItem(Localizer["Ask for more details"], "2"),
            new SelectListItem(Localizer["Reject Fix"], "3"),
            new SelectListItem(Localizer["Fixed"], "4"),
        };

        var comments = await FixReportService.GetCommentsAsync(fixRequest.Id);

        var fixReportViewModel = new DoFixReportViewModel()
        {
            Key = key,
            Title = fixRequest.VulnTitle,
            Description = description,
            Solution = solution,
            Score = fixRequest.VulnScore?.ToString("F1") ?? "",
            HostName = hostName,
            IsTeamFix = fixRequest.IsTeamFix,
            FixDate = DateOnly.FromDateTime(DateTime.Now),
            Answers = answers,
            Status = fixRequest.Status,
            Comments = comments
        };

        if (fixRequest.IsTeamFix && fixRequest.FixTeamId != null)
        {
            var users = await FixReportService.GetTeamUsersAsync(fixRequest.FixTeamId.Value);
            foreach (var user in users)
            {
                fixReportViewModel.Fixers.Add(new SelectListItem(user.Login, user.UserValue.ToString()));
            }
        }
        else
        {
            fixReportViewModel.FixerEmail = fixRequest.SingleFixDestination ?? "";
        }

        TempData["fixReportViewModel"] = JsonSerializer.Serialize(fixReportViewModel);

        return RedirectToAction("DoReport");
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
        if (vm.FluxControl == "answering")
        {
            var fixRequest = await FixReportService.GetByIdentifierAsync(vm.Key);
            if (fixRequest == null)
            {
                Logger.LogError("FixRequest {key} not found on report", vm.Key);
                return StatusCode(500);
            }

            var (newStatus, newFixStatusLabel) = MapAnswer(vm.AnswerId);

            if (vm.Comment != "")
            {
                if (vm.FixerId != "")
                {
                    var user = await UserService.GetByIdAsync(int.Parse(vm.FixerId));
                    await FixReportService.QueueCommentAsync(new CommentCreateDto
                    {
                        FixRequestId = fixRequest.Id,
                        UserId = int.Parse(vm.FixerId),
                        IsAnonymous = false,
                        CommenterName = user?.Name ?? "",
                        Text = vm.Comment,
                        Date = DateTime.UtcNow
                    });
                }
                else
                {
                    await FixReportService.QueueCommentAsync(new CommentCreateDto
                    {
                        FixRequestId = fixRequest.Id,
                        UserId = null,
                        IsAnonymous = true,
                        CommenterName = vm.FixerEmail,
                        Text = vm.Comment,
                        Date = DateTime.UtcNow
                    });
                }
            }

            await FixReportService.QueueStatusChangeAsync(new FixRequestStatusChangeDto
            {
                FixRequestId = fixRequest.Id,
                NewStatus = newStatus,
                NewStatusLabel = newFixStatusLabel,
                FixDate = vm.FixDate.ToDateTime(new TimeOnly(0, 0)),
                IsTeamFix = vm.IsTeamFix,
                LastReportingUserId = vm.IsTeamFix && vm.FixerId != "" ? int.Parse(vm.FixerId) : null,
                SingleFixDestination = vm.IsTeamFix ? null : vm.FixerEmail,
                RequestingUserId = fixRequest.RequestingUserId,
                VulnerabilityId = fixRequest.VulnerabilityId
            });

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

    private static (int Status, string Label) MapAnswer(string answerId) => answerId switch
    {
        "0" => ((int)IntStatus.Open, "Open"),
        "1" => ((int)IntStatus.AwaitingFix, "Awaiting Fix"),
        "2" => ((int)IntStatus.AwaitingInternalResponse, "Awaiting Internal Response"),
        "3" => ((int)IntStatus.FixNotPossible, "Fix Not Possible"),
        "4" => ((int)IntStatus.Fixed, "Fixed"),
        _ => ((int)IntStatus.AwaitingFix, "Awaiting Fix"),
    };
}
