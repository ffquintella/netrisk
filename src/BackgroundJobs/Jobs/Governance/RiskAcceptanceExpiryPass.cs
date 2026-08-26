using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// Daily pass that expires lapsed <em>risk</em> acceptances and reopens their risks for review
/// (Track 8 milestone 8.1.3).
///
/// Distinct from <c>Findings.RiskAcceptanceExpiryJob</c>, which handles the finding-level half of the
/// same table. Two jobs rather than one because the two halves reactivate different things — findings
/// come back into the vulnerability register, a risk gets flagged for management review — and because
/// each service only ever touches its own rows, so neither can expire the other's twice.
///
/// The job's own responsibility is the notifications; the expiry itself is in the service, which is
/// what keeps it testable without a mail server.
/// </summary>
public class RiskAcceptanceExpiryPass(
    ILogger logger,
    DalService dalService,
    IRiskAcceptancesService acceptances,
    INotificationEventPublisher notifications,
    IMessagesService messagesService)
    : BaseJob(logger, dalService), IJob
{
    /// <summary>Chat channel job output lands in, matching how other jobs reach users.</summary>
    private const int NotificationChatType = (int)Model.Messages.ChatTypes.Jobs;

    public void Run()
    {
        // Hangfire's job signature is synchronous. Blocking here rather than making the whole service
        // tree synchronous is the established pattern in this project's jobs.
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var result = await acceptances.ProcessExpiryAsync(now);

        foreach (var acceptance in result.Expired)
        {
            // The service loads the risk with the acceptance, so this job opens no database context
            // of its own. That is not just tidiness: a job whose only I/O is notifications can be
            // unit-tested without a database, and this one carries the logic an operator relies on.
            var risk = acceptance.Risk;

            Log.Information(
                "Risk acceptance {Id} ('{Name}') expired on {Expiry}; risk {Risk} is flagged for review",
                acceptance.Id, acceptance.Name, acceptance.ExpiresAt, acceptance.RiskId);

            await notifications.RiskAcceptanceExpiredAsync(acceptance, risk);

            var message = $"The risk acceptance '{acceptance.Name}' expired on " +
                          $"{acceptance.ExpiresAt:yyyy-MM-dd}. " +
                          (risk == null
                              ? "Whatever it covered needs re-triage or a renewal."
                              : $"Risk '{risk.Subject}' is flagged for management review again.");

            // The authorizing manager first: they signed it, and renewing or letting it stand is
            // their decision.
            await NotifyAsync(acceptance.AuthorizingManagerId, message);

            if (acceptance.RequestedById is { } requester && requester != acceptance.AuthorizingManagerId)
                await NotifyAsync(requester, message);

            if (risk?.Owner is { } owner && owner != acceptance.AuthorizingManagerId)
                await NotifyAsync(owner, message);
        }

        foreach (var (acceptance, daysBefore) in result.Warnings)
        {
            Log.Information("Risk acceptance {Id} ('{Name}') expires in {Days} days", acceptance.Id,
                acceptance.Name, daysBefore);

            await notifications.RiskAcceptanceExpiringAsync(acceptance, daysBefore, 0);

            await NotifyAsync(acceptance.AuthorizingManagerId,
                $"The risk acceptance '{acceptance.Name}' expires on " +
                $"{acceptance.ExpiresAt:yyyy-MM-dd} — {daysBefore} day(s) from now. Renew it or plan " +
                "the treatment before the risk reopens.");
        }

        if (result.Expired.Count == 0 && result.Warnings.Count == 0)
            Log.Debug("Risk-acceptance expiry pass found nothing to do");
    }

    private async Task NotifyAsync(int userId, string message)
    {
        try
        {
            await messagesService.SendMessageAsync(message, userId, NotificationChatType);
        }
        catch (Exception ex)
        {
            // The expiry is already committed. Losing a message is far better than leaving half the
            // acceptances unprocessed until tomorrow.
            Log.Warning("Could not notify user {User} about a risk-acceptance change: {Message}", userId,
                ex.Message);
        }
    }
}
