using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Model.Governance;
using RiskPortal.Models;
using RiskPortal.Services;

namespace RiskPortal.Pages.Campaign;

/// <summary>
/// The review screen: one campaign's risks, in the reviewer's own priority order, each with a
/// decision (Track 8 milestone 8.6.4).
///
/// Every decision goes straight through to the API and materializes there as a first-class record —
/// an 8.1 acceptance, 8.5.3 treatment tasks, a management review. The portal holds no draft state of
/// its own, which is deliberate: a reviewer working through twenty risks on a phone will lose
/// connectivity, and a decision that is only in a browser tab is a decision that did not happen.
/// </summary>
public class IndexModel(IPortalApiClient api, IPortalSession session, ILogger<IndexModel> logger)
    : PageModel
{
    public CampaignDetail? Detail { get; private set; }

    public AlertBag Alerts { get; } = new();

    /// <summary>Which risk's decision form is expanded. Kept in the URL so a reload does not collapse it.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Open { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (session.Token is null) return RedirectToPage("/SignIn");

        if (TempData["Error"] is string error) Alerts.Error = error;
        if (TempData["Notice"] is string notice) Alerts.Notice = notice;

        Detail = await api.GetCampaignAsync(session.Token, id, HttpContext.RequestAborted);

        if (Detail is null)
        {
            TempData["Error"] = "That review is not available to you. It may belong to a business " +
                                "entity you are not appointed to review.";
            return RedirectToPage("/Index");
        }

        return Page();
    }

    /// <summary>
    /// Persists a drag-to-rank ordering. The ids arrive as one comma-separated field so the whole
    /// ordering is one form value, which is what lets the page work with JavaScript disabled — the
    /// move-up/move-down buttons post the same field.
    /// </summary>
    public async Task<IActionResult> OnPostRankAsync(int id, string? order)
    {
        if (session.Token is null) return RedirectToPage("/SignIn");

        var itemIds = ParseOrder(order);

        if (itemIds.Count == 0)
        {
            TempData["Error"] = "No ordering was submitted, so nothing was changed.";
            return RedirectToPage(new { id });
        }

        var result = await api.SaveRankingAsync(session.Token, id, itemIds, HttpContext.RequestAborted);

        if (result.Succeeded) TempData["Notice"] = "Your priority order was saved.";
        else TempData["Error"] = result.Message;

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Records one decision. The three branches carry different payloads, and each is validated here
    /// before the request goes out — not because the server does not validate (it does, and its
    /// refusal is what the reviewer sees when this misses something) but because a round trip to be
    /// told "a justification is required" is a worse experience than being told immediately.
    /// </summary>
    public async Task<IActionResult> OnPostDecideAsync(int id, int itemId, string decision,
        string? notes, string? justification, DateTime? expiresAt, string? taskTitle,
        int? taskOwnerId, DateTime? taskDueDate, int? escalateToUserId)
    {
        if (session.Token is null) return RedirectToPage("/SignIn");

        if (!Enum.TryParse<RiskReviewDecision>(decision, ignoreCase: true, out var parsed) ||
            parsed == RiskReviewDecision.Pending)
        {
            TempData["Error"] = "Choose Accept, Request mitigation, or Escalate.";
            return RedirectToPage(new { id, open = itemId });
        }

        var request = new CampaignDecisionRequest { Decision = parsed, Notes = notes };

        switch (parsed)
        {
            case RiskReviewDecision.Accepted:
                if (string.IsNullOrWhiteSpace(justification))
                {
                    TempData["Error"] = "An acceptance needs a written business justification. It is " +
                                        "the field an auditor reads.";
                    return RedirectToPage(new { id, open = itemId });
                }

                if (expiresAt is null || expiresAt.Value.Date <= DateTime.UtcNow.Date)
                {
                    TempData["Error"] = "An acceptance needs an expiry date in the future. Accepting a " +
                                        "risk indefinitely is how 'accepted' becomes 'forgotten'.";
                    return RedirectToPage(new { id, open = itemId });
                }

                request.Acceptance = new RiskAcceptanceRequest
                {
                    BusinessJustification = justification.Trim(),
                    // Treated as end-of-day UTC: a reviewer picking a date means "until the end of
                    // that day", and midnight would expire it a day early.
                    ExpiresAt = DateTime.SpecifyKind(expiresAt.Value.Date.AddDays(1).AddSeconds(-1),
                        DateTimeKind.Utc)
                };
                break;

            case RiskReviewDecision.MitigationRequested:
                if (string.IsNullOrWhiteSpace(taskTitle))
                {
                    TempData["Error"] = "Requesting mitigation needs at least one task with a title. " +
                                        "'Please mitigate this' is not a plan of action.";
                    return RedirectToPage(new { id, open = itemId });
                }

                request.Tasks =
                [
                    new MitigationTaskRequest
                    {
                        Title = taskTitle.Trim(),
                        Description = notes,
                        OwnerId = taskOwnerId,
                        DueDate = taskDueDate is null
                            ? null
                            : DateTime.SpecifyKind(taskDueDate.Value.Date, DateTimeKind.Utc)
                    }
                ];
                break;

            case RiskReviewDecision.Escalated:
                if (escalateToUserId is null or <= 0)
                {
                    TempData["Error"] = "An escalation needs a named senior approver. Escalating to " +
                                        "nobody leaves the risk where it was with a note saying it moved.";
                    return RedirectToPage(new { id, open = itemId });
                }

                request.EscalateToUserId = escalateToUserId;
                break;
        }

        var result = await api.DecideAsync(session.Token, id, itemId, request,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            logger.LogInformation("Portal decision {Decision} recorded on campaign {Campaign} item {Item}",
                parsed, id, itemId);

            TempData["Notice"] = parsed switch
            {
                RiskReviewDecision.Accepted =>
                    "Accepted. The acceptance is recorded with your justification and its expiry date, " +
                    "and the risk will come back for review when it lapses.",
                RiskReviewDecision.MitigationRequested =>
                    "Mitigation requested. The task is assigned and its owner will be notified.",
                _ => "Escalated. The approver you named will be notified."
            };

            return RedirectToPage(new { id });
        }

        TempData["Error"] = result.Message;
        return RedirectToPage(new { id, open = itemId });
    }

    /// <summary>
    /// Parses the ordering field, dropping anything that is not a positive integer.
    ///
    /// The server rejects ids that are not in the campaign, so a malformed field cannot reorder
    /// somebody else's campaign; dropping the junk here just means a mangled submission produces a
    /// clear "nothing was submitted" rather than a confusing server-side refusal.
    /// </summary>
    public static List<int> ParseOrder(string? order)
    {
        if (string.IsNullOrWhiteSpace(order)) return [];

        return order
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Where(value => value > 0)
            .Distinct()
            .ToList();
    }
}
