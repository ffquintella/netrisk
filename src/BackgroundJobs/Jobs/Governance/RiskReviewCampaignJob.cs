using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// Generates the periodic business review campaigns and chases the overdue ones
/// (Track 8 milestone 8.6.3).
///
/// Runs daily and is idempotent by construction: the unique (entity, period) index means the second
/// run of a day converges on the campaigns the first created rather than making a new set. That is
/// what lets this be a plain daily job instead of something scheduled on quarter boundaries, which
/// would silently do nothing if the host were down that morning.
/// </summary>
public class RiskReviewCampaignJob(
    ILogger logger,
    DalService dalService,
    IRiskReviewCampaignsService campaigns,
    IEntityRiskReviewersService reviewers,
    INotificationEventPublisher notifications,
    IMessagesService messagesService)
    : BaseJob(logger, dalService), IJob
{
    private const int NotificationChatType = (int)Model.Messages.ChatTypes.Jobs;

    /// <summary>Days between overdue reminders, so an overdue campaign is chased weekly, not daily.</summary>
    internal const int OverdueReminderIntervalDays = 7;

    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var created = await campaigns.GenerateDueCampaignsAsync(now);

        foreach (var campaign in created)
        {
            var itemCount = campaign.Items.Count;

            // An empty campaign is not worth anybody's inbox: the entity has no open risks, which is
            // good news rather than an action item.
            if (itemCount == 0)
            {
                Log.Debug("Campaign {Id} for entity {Entity} has no items; not notifying", campaign.Id,
                    campaign.EntityId);
                continue;
            }

            var appointed = await reviewers.GetByEntityAsync(campaign.EntityId);

            foreach (var reviewer in appointed)
            {
                await notifications.RiskReviewCampaignAssignedAsync(campaign, reviewer.UserId, itemCount);

                await NotifyAsync(reviewer.UserId,
                    $"A periodic risk review has been assigned to you: '{campaign.Name}' covering " +
                    $"{itemCount} risk(s), due {campaign.DueDate:yyyy-MM-dd}.");
            }
        }

        var overdue = await campaigns.MarkOverdueAsync(now);

        // MarkOverdue only transitions Open → Overdue, so an already-overdue campaign is not in that
        // list and would never be chased again. TakeOverdueReminders is what makes the reminder
        // recur, and it does the bookkeeping so this job opens no database context of its own.
        var reminders = await campaigns.TakeOverdueRemindersAsync(now, OverdueReminderIntervalDays);

        foreach (var reminder in reminders)
        {
            await notifications.RiskReviewCampaignOverdueAsync(reminder.Campaign, reminder.PendingItems);

            foreach (var userId in reminder.ReviewerUserIds)
                await NotifyAsync(userId,
                    $"The risk review '{reminder.Campaign.Name}' was due on " +
                    $"{reminder.Campaign.DueDate:yyyy-MM-dd} and {reminder.PendingItems} risk(s) still " +
                    "have no decision.");
        }

        Log.Information(
            "Risk-review campaign pass: {Created} created, {Overdue} newly overdue, {Reminded} chased",
            created.Count, overdue.Count, reminders.Count);
    }

    private async Task NotifyAsync(int userId, string message)
    {
        try
        {
            await messagesService.SendMessageAsync(message, userId, NotificationChatType);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not notify reviewer {User} about a campaign: {Message}", userId, ex.Message);
        }
    }
}
