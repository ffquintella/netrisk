using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RiskPortal.Models;
using RiskPortal.Services;

namespace RiskPortal.Pages;

/// <summary>
/// The reviewer's dashboard: the campaigns assigned to them, what is overdue, and how far through
/// each one they are (Track 8 milestone 8.6.4).
/// </summary>
public class IndexModel(IPortalApiClient api, IPortalSession session) : PageModel
{
    public List<CampaignSummary> Campaigns { get; private set; } = [];

    /// <summary>Whether completed campaigns are shown as well as open ones.</summary>
    [BindProperty(SupportsGet = true)]
    public bool ShowCompleted { get; set; }

    public AlertBag Alerts { get; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (session.Token is null) return RedirectToPage("/SignIn");

        Campaigns = await api.GetMyCampaignsAsync(session.Token, openOnly: !ShowCompleted,
            HttpContext.RequestAborted);

        if (TempData["Error"] is string error) Alerts.Error = error;
        if (TempData["Notice"] is string notice) Alerts.Notice = notice;

        return Page();
    }
}
