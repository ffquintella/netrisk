using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// The daily push that Track 8 milestone 8.5.1 adds to a cadence that was pull-only.
///
/// Everything it needs already existed: <c>ReviewLevel</c> seeds a review interval per severity band
/// (Very High 30 days … Insignificant 360), <c>MgmtReview.NextReview</c> stores the date, and
/// <c>RisksService.GetToReview</c>/<c>GetRisksNeedingReview</c> find the stale ones. What was missing
/// was anything that told a human. An overdue review was visible only if somebody opened the right
/// screen, which is the definition of a control nobody exercises.
///
/// Three sweeps, in one job because they share the same "who needs to know" resolution:
/// overdue or never-reviewed risks, risks flagged by an event (a new Critical vulnerability or
/// incident), and treatment tasks that are due or late.
/// </summary>
public class RiskReviewCadenceJob(
    ILogger logger,
    DalService dalService,
    IRisksService risksService,
    IMgmtReviewsService mgmtReviews,
    IMitigationTasksService mitigationTasks,
    INotificationEventPublisher notifications,
    IMessagesService messagesService)
    : BaseJob(logger, dalService), IJob
{
    private const int NotificationChatType = (int)Model.Messages.ChatTypes.Jobs;

    /// <summary>
    /// How far ahead a treatment task is announced. Seven days is enough notice to do something and
    /// short enough that the message is still about this week.
    /// </summary>
    internal const int TaskLookaheadDays = 7;

    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var overdue = await NotifyOverdueReviewsAsync(now);
        var flagged = await NotifyEventTriggeredReviewsAsync();
        var tasks = await NotifyDueTasksAsync(now);

        Log.Information(
            "Risk review cadence pass: {Overdue} overdue review(s), {Flagged} event-triggered, " +
            "{Tasks} treatment task(s) due", overdue, flagged, tasks);
    }

    /// <summary>
    /// Risks whose latest review is older than their severity band's cadence, plus risks that have
    /// never been reviewed at all. The second group matters more and is easy to miss: a risk with no
    /// review has no <c>NextReview</c> date, so a query written around that column skips it entirely.
    ///
    /// The resolution lives in <c>MgmtReviewsService.GetOverdueReviewsAsync</c>, so this job opens no
    /// database context and is unit-testable without one.
    /// </summary>
    private async Task<int> NotifyOverdueReviewsAsync(DateTime now)
    {
        var overdue = await mgmtReviews.GetOverdueReviewsAsync(now);

        foreach (var item in overdue)
        {
            await notifications.RiskReviewOverdueAsync(new DAL.Entities.Risk
            {
                Id = item.RiskId,
                Subject = item.Subject,
                ReferenceId = item.ReferenceId,
                Status = item.Status,
                EntityId = item.EntityId,
                Assessment = string.Empty,
                Notes = string.Empty,
                RiskCatalogMapping = string.Empty,
                ThreatCatalogMapping = string.Empty
            }, item.Score, item.DaysOverdue, item.LastReviewedAt, item.CadenceDays);

            var message = item.LastReviewedAt == null
                ? $"Risk '{item.Subject}' has never had a management review and was submitted " +
                  $"{item.DaysOverdue + item.CadenceDays} day(s) ago."
                : $"Risk '{item.Subject}' was last reviewed on " +
                  $"{item.LastReviewedAt:yyyy-MM-dd}; its review is {item.DaysOverdue} day(s) overdue.";

            await NotifyPeopleAsync(message, item.OwnerId, item.ManagerId);
        }

        return overdue.Count;
    }

    /// <summary>
    /// Risks flagged out of cadence — DORA Art. 6(5)'s "after major incidents". The flag is set by
    /// <c>RisksService.RequestReviewAsync</c>, which is called when an acceptance lapses or is
    /// revoked and by the event hooks on the register.
    /// </summary>
    private async Task<int> NotifyEventTriggeredReviewsAsync()
    {
        var flagged = await risksService.GetReviewRequestedAsync();

        foreach (var risk in flagged)
            await NotifyPeopleAsync(
                $"Risk '{risk.Subject}' has been flagged for review: " +
                (risk.ReviewRequestedReason ?? "an event on the risk warrants an out-of-cadence look."),
                risk.Owner, risk.Manager);

        return flagged.Count;
    }

    /// <summary>
    /// Treatment tasks due within the lookahead or already late (8.5.3). Idempotent through
    /// <c>LastNotifiedDaysBefore</c>: without it a task that is 40 days overdue produces 40 identical
    /// messages, and the owner stops reading them on day three.
    /// </summary>
    private async Task<int> NotifyDueTasksAsync(DateTime now)
    {
        var due = await mitigationTasks.GetDueOrOverdueAsync(now, TaskLookaheadDays);
        if (due.Count == 0) return 0;

        var notified = 0;

        foreach (var task in due)
        {
            if (task.DueDate is null) continue;

            var daysUntilDue = (int)System.Math.Floor((task.DueDate.Value - now).TotalDays);

            // The threshold is the notice remaining, so it decreases as the due date approaches and
            // goes negative once late. Re-notify only when it has moved to a smaller number.
            if (task.LastNotifiedDaysBefore is { } already && already <= daysUntilDue) continue;

            // The service loads the mitigation with the task, so the risk id needs no second query.
            var riskId = task.Mitigation?.RiskId ?? 0;

            await notifications.MitigationTaskDueAsync(task, riskId, daysUntilDue);

            if (task.OwnerId is { } owner)
                await NotifyAsync(owner, daysUntilDue < 0
                    ? $"Treatment task '{task.Title}' was due on {task.DueDate:yyyy-MM-dd} and is " +
                      $"{-daysUntilDue} day(s) overdue."
                    : $"Treatment task '{task.Title}' is due on {task.DueDate:yyyy-MM-dd}.");

            await mitigationTasks.MarkNotifiedAsync(task.Id, daysUntilDue);

            notified++;
        }

        return notified;
    }

    private async Task NotifyPeopleAsync(string message, params int?[] userIds)
    {
        foreach (var userId in userIds.Where(id => id is > 0).Select(id => id!.Value).Distinct())
            await NotifyAsync(userId, message);
    }

    private async Task NotifyAsync(int userId, string message)
    {
        try
        {
            await messagesService.SendMessageAsync(message, userId, NotificationChatType);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not notify user {User} about a review: {Message}", userId, ex.Message);
        }
    }
}
