using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Findings;

/// <summary>
/// Daily SLA notification pass (Track 3 milestone 3.4.3).
///
/// Digest-style on purpose: one message per owner listing everything of theirs that is breached or
/// approaching, rather than one per finding. Per-finding alerting is the known anti-pattern here —
/// it trains people to filter the alerts, at which point the notification has negative value.
///
/// The service decides who hears about what and guards idempotence; this job sends and then records
/// what was actually delivered. Recording after sending is deliberate: a send that failed must be
/// retried tomorrow, not marked done.
/// </summary>
public class SlaBreachNotificationJob(
    ILogger logger,
    DalService dalService,
    ISlaService slaService,
    IMessagesService messagesService,
    IEmailService emailService)
    : BaseJob(logger, dalService), IJob
{
    private const int NotificationChatType = (int)Model.Messages.ChatTypes.Jobs;

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var digests = await slaService.BuildNotificationDigestsAsync(now);

        if (digests.Count == 0)
        {
            Log.Debug("SLA notification pass found nothing to report");
            return;
        }

        var delivered = new System.Collections.Generic.List<ServerServices.Interfaces.SlaDigest>();

        foreach (var digest in digests)
        {
            var breached = digest.Breached.ToList();
            var approaching = digest.Approaching.ToList();

            var summary = Compose(digest, breached.Count, approaching.Count);

            var sent = false;

            // The in-app message is the reliable channel and always attempted; email is best-effort
            // on top of it. A digest counts as delivered if either landed — otherwise a broken SMTP
            // configuration would make the job resend the same list every day forever.
            if (digest.RecipientUserId != null)
                sent |= await SendMessageAsync(digest.RecipientUserId.Value, summary);
            else
                sent |= await NotifyUnownedAsync(summary);

            if (!string.IsNullOrWhiteSpace(digest.RecipientEmail))
                sent |= await SendEmailAsync(digest, summary);

            if (sent) delivered.Add(digest);

            Log.Information(
                "SLA digest for user {User}: {Breached} breached, {Approaching} approaching, delivered {Delivered}",
                digest.RecipientUserId, breached.Count, approaching.Count, sent);
        }

        if (delivered.Count > 0) await slaService.RecordNotificationsAsync(delivered, now);
    }

    /// <summary>
    /// Breached first and named individually; approaching findings summarised. A digest somebody
    /// actually reads is one where the urgent part is at the top and short.
    /// </summary>
    private static string Compose(ServerServices.Interfaces.SlaDigest digest, int breachedCount,
        int approachingCount)
    {
        var text = new StringBuilder();

        text.AppendLine(digest.RecipientUserId == null
            ? "Findings with no owner are past or approaching their remediation deadline:"
            : "Your findings are past or approaching their remediation deadline:");

        if (breachedCount > 0)
        {
            text.AppendLine();
            text.AppendLine($"Breached ({breachedCount}):");
            foreach (var item in digest.Breached.Take(25))
                text.AppendLine($"  #{item.FindingId} {item.Title} — due {item.DueDate:yyyy-MM-dd}, " +
                                $"{item.DaysOverdue} day(s) overdue" +
                                (item.AssetName == null ? "" : $" on {item.AssetName}"));

            if (breachedCount > 25) text.AppendLine($"  … and {breachedCount - 25} more.");
        }

        if (approachingCount > 0)
        {
            text.AppendLine();
            text.AppendLine($"Approaching ({approachingCount}):");
            foreach (var item in digest.Approaching.Take(25))
                text.AppendLine($"  #{item.FindingId} {item.Title} — due {item.DueDate:yyyy-MM-dd} " +
                                $"(T-{item.ThresholdDays})" +
                                (item.AssetName == null ? "" : $" on {item.AssetName}"));

            if (approachingCount > 25) text.AppendLine($"  … and {approachingCount - 25} more.");
        }

        return text.ToString();
    }

    private async Task<bool> SendMessageAsync(int userId, string summary)
    {
        try
        {
            await messagesService.SendMessageAsync(summary, userId, NotificationChatType);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not send the SLA digest to user {User}: {Message}", userId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Unowned findings go to the administrators. An unowned breached critical is precisely the one
    /// nobody would otherwise hear about, so dropping this digest is not an option.
    /// </summary>
    private async Task<bool> NotifyUnownedAsync(string summary)
    {
        try
        {
            await using var db = DalService.GetContext();

            var admins = db.Users.Where(u => u.Admin && u.Enabled == true).Select(u => u.Value).ToList();
            if (admins.Count == 0)
            {
                Log.Warning("SLA digest for unowned findings could not be delivered: no enabled administrators");
                return false;
            }

            var sent = false;
            foreach (var admin in admins) sent |= await SendMessageAsync(admin, summary);

            return sent;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not deliver the unowned-findings SLA digest: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> SendEmailAsync(ServerServices.Interfaces.SlaDigest digest, string summary)
    {
        try
        {
            // Reuses the vulnerability-update template rather than adding a Track-3-only one: the
            // digest body is plain text and the template's job here is the wrapper, not the content.
            await emailService.SendEmailAsync(digest.RecipientEmail!,
                "NetRisk: findings approaching or past their SLA deadline",
                "VulnerabilityUpdate", "en", new
                {
                    Name = digest.RecipientName ?? "",
                    Message = summary
                });

            return true;
        }
        catch (Exception ex)
        {
            // Email is best-effort. A misconfigured SMTP server must not stop the in-app digest from
            // counting as delivered.
            Log.Warning("Could not email the SLA digest to {Email}: {Message}", digest.RecipientEmail, ex.Message);
            return false;
        }
    }
}
