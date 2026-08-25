namespace DAL.Enums;

/// <summary>
/// Which provider renders and delivers a notification (Track 4 milestone 4.1.2), persisted in
/// <c>notification_channels.kind</c>.
///
/// An int-backed enum rather than the provider's name in a string column: a channel row is read by
/// the dispatcher on every send, and a typo in a free-text provider name would be a silent
/// non-delivery. New providers append; values are never reused.
/// </summary>
public enum NotificationChannelKind
{
    /// <summary>SMTP, through the existing FluentEmail path.</summary>
    Email = 1,

    /// <summary>Slack incoming webhook, rendered as Block Kit.</summary>
    Slack = 2,

    /// <summary>Microsoft Teams Workflows webhook, rendered as an Adaptive Card.</summary>
    Teams = 3,

    /// <summary>Generic JSON POST with an HMAC-SHA256 signature header.</summary>
    Webhook = 4
}
