using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Messages;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations;

/// <summary>
/// Announces integration sync runs into the in-app notification centre (Track 4).
///
/// The channel is the <see cref="ChatTypes.Jobs"/> message chat, which is what the desktop client's
/// notification badge already polls every ten seconds and what <c>NotificationsViewModel</c> already
/// lists. That matters more than it looks: a posture sync is a long-running server-side process with no
/// push channel to the client — there is no SignalR anywhere in this product — so a message row is the
/// only way a scheduled run can reach a GUI that was not watching when it started.
/// </summary>
public interface IIntegrationSyncNotifier
{
    /// <summary>Announces that a run has begun.</summary>
    Task StartedAsync(IntegrationKind kind, string connectionName, CancellationToken ct = default);

    /// <summary>
    /// Announces how a run ended. <paramref name="error"/> non-null makes it an error notification
    /// regardless of <paramref name="status"/>.
    /// </summary>
    Task FinishedAsync(IntegrationKind kind, string connectionName, IntegrationSyncStatus status,
        string? summary, string? error, CancellationToken ct = default);
}

/// <inheritdoc />
public class IntegrationSyncNotifier(
    ILogger logger,
    IDalService dalService,
    IMessagesService messages)
    : ServiceBase(logger, dalService), IIntegrationSyncNotifier
{
    public Task StartedAsync(IntegrationKind kind, string connectionName, CancellationToken ct = default) =>
        SendAsync(MessageType.Information,
            $"{Label(kind)} synchronization started: {connectionName}.", ct);

    public Task FinishedAsync(IntegrationKind kind, string connectionName, IntegrationSyncStatus status,
        string? summary, string? error, CancellationToken ct = default)
    {
        var type = error != null || status == IntegrationSyncStatus.Failed
            ? MessageType.Error
            : status == IntegrationSyncStatus.PartiallySucceeded
                ? MessageType.Warning
                : MessageType.Information;

        var outcome = status switch
        {
            IntegrationSyncStatus.Succeeded => "finished",
            IntegrationSyncStatus.PartiallySucceeded => "finished with errors",
            IntegrationSyncStatus.Failed => "failed",
            _ => "ended"
        };

        var text = $"{Label(kind)} synchronization {outcome}: {connectionName}.";

        // The error first and the counts second when there is an error: the counts of a failed run are
        // the least interesting thing about it, and a notification is read from the front.
        if (!string.IsNullOrWhiteSpace(error)) text += " " + error.Trim();
        else if (!string.IsNullOrWhiteSpace(summary)) text += " " + summary.Trim();

        return SendAsync(type, text, ct);
    }

    /// <summary>
    /// Fans the message out to every enabled administrator.
    ///
    /// Administrators rather than the person who clicked, and the same set for a manual run as for a
    /// scheduled one. An integration sync has no owner — the scheduled pass has no user at all — and
    /// `users.admin` is the same audience the existing SLA digest falls back to for ownerless findings,
    /// so this adds no new notion of who counts as an operator.
    /// </summary>
    private async Task SendAsync(MessageType type, string text, CancellationToken ct)
    {
        try
        {
            List<int> admins;

            await using (var db = DalService.GetContext())
            {
                admins = await db.Users
                    .Where(u => u.Admin && u.Enabled == true)
                    .Select(u => u.Value)
                    .ToListAsync(ct);
            }

            if (admins.Count == 0)
            {
                Logger.Warning("An integration sync notification was not delivered because no enabled "
                               + "administrator exists: {Message}", text);
                return;
            }

            foreach (var admin in admins)
                await messages.SendMessageAsync(text, admin, (int)ChatTypes.Jobs, (int)type);
        }
        catch (Exception ex)
        {
            // Swallowed, like every other notification in Track 4: a sync that could not be announced
            // must still be a sync that ran. This is called from the completion path, which is also the
            // path that records the outcome — a throw here would lose the outcome to protect the
            // announcement of it.
            Logger.Warning(ex, "Could not deliver the integration sync notification: {Message}", text);
        }
    }

    /// <summary>The provider name an operator would recognise, for the notification's first words.</summary>
    private static string Label(IntegrationKind kind) => kind switch
    {
        IntegrationKind.IssueTracker => "Issue tracker",
        IntegrationKind.TrendMicroVisionOne => "Trend Micro Vision One",
        IntegrationKind.SecurityScorecard => "SecurityScorecard",
        IntegrationKind.Scim => "SCIM provisioning",
        IntegrationKind.JiraServiceManagement => "Jira Service Management",
        IntegrationKind.JiraAssets => "Jira Assets",
        _ => "Integration"
    };
}
