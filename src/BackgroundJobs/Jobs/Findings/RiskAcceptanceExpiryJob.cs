using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Enums;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Findings;

/// <summary>
/// Daily pass that expires lapsed risk acceptances and reactivates the findings they covered
/// (Track 3 milestone 3.2.4).
///
/// This is the job that keeps "accepted risk" from silently becoming "forgotten risk". The service
/// does the work and reports what it did; this job's own responsibility is the notifications and
/// nothing else, which is what keeps the expiry logic testable without a mail server.
/// </summary>
public class RiskAcceptanceExpiryJob(
    ILogger logger,
    DalService dalService,
    IFindingLifecycleService lifecycleService,
    IMessagesService messagesService)
    : BaseJob(logger, dalService), IJob
{
    /// <summary>Chat channel notifications land in, matching how other job output reaches users.</summary>
    private const int NotificationChatType = (int)Model.Messages.ChatTypes.Jobs;

    public void Run()
    {
        // Hangfire's job signature is synchronous. Blocking here rather than making the whole
        // service tree synchronous is the established pattern in this project's jobs.
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var result = await lifecycleService.ProcessExpiredAcceptancesAsync(now);

        foreach (var acceptance in result.Expired)
        {
            var reactivated = result.ReactivatedFindings.TryGetValue(acceptance.Id, out var ids) ? ids : [];

            Log.Information(
                "Risk acceptance {Id} ('{Name}') expired on {Expiry}; {Count} findings reactivated",
                acceptance.Id, acceptance.Name, acceptance.ExpiresAt, reactivated.Count);

            var message = $"Risk acceptance '{acceptance.Name}' expired on " +
                          $"{acceptance.ExpiresAt:yyyy-MM-dd}. {reactivated.Count} finding(s) are open again " +
                          "and need re-triage or a renewed acceptance.";

            // The authorizing manager is told first: they signed the acceptance, and the expiry is
            // their decision to renew or let stand.
            await NotifyAsync(acceptance.AuthorizingManagerId, message);

            if (acceptance.CreatedById != null && acceptance.CreatedById != acceptance.AuthorizingManagerId)
                await NotifyAsync(acceptance.CreatedById.Value, message);

            await NotifyFindingOwnersAsync(reactivated, message);
        }

        foreach (var (acceptance, daysBefore) in result.Warnings)
        {
            Log.Information("Risk acceptance {Id} ('{Name}') expires in {Days} days", acceptance.Id,
                acceptance.Name, daysBefore);

            var message = $"Risk acceptance '{acceptance.Name}' expires on " +
                          $"{acceptance.ExpiresAt:yyyy-MM-dd} — {daysBefore} day(s) from now. Renew it or plan " +
                          "the remediation before its findings reopen.";

            await NotifyAsync(acceptance.AuthorizingManagerId, message);
        }

        if (result.Expired.Count == 0 && result.Warnings.Count == 0)
            Log.Debug("Risk-acceptance expiry pass found nothing to do");
    }

    private async Task NotifyFindingOwnersAsync(System.Collections.Generic.List<int> findingIds, string message)
    {
        if (findingIds.Count == 0) return;

        await using var db = DalService.GetContext();

        var owners = db.Vulnerabilities
            .Where(v => findingIds.Contains(v.Id) && v.AnalystId != null)
            .Select(v => v.AnalystId!.Value)
            .Distinct()
            .ToList();

        foreach (var owner in owners) await NotifyAsync(owner, message);
    }

    private async Task NotifyAsync(int userId, string message)
    {
        try
        {
            await messagesService.SendMessageAsync(message, userId, NotificationChatType);
        }
        catch (Exception ex)
        {
            // A notification failure must not abort the pass: the expiry itself is already
            // committed, and losing the message is far better than leaving half the acceptances
            // unprocessed until tomorrow.
            Log.Warning("Could not notify user {User} about a risk-acceptance change: {Message}", userId,
                ex.Message);
        }
    }
}
