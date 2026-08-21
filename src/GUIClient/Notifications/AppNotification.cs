using System;

namespace GUIClient.Notifications;

/// <summary>One transient notification: a short message the user does not have to dismiss.</summary>
public sealed class AppNotification
{
    public AppNotification(string message, NotificationSeverity severity, TimeSpan lifetime)
    {
        Message = message;
        Severity = severity;
        Lifetime = lifetime;
    }

    public string Message { get; }

    public NotificationSeverity Severity { get; }

    /// <summary>How long the toast stays on screen before it removes itself.</summary>
    public TimeSpan Lifetime { get; }

    public bool IsSuccess => Severity == NotificationSeverity.Success;
    public bool IsInfo => Severity == NotificationSeverity.Info;
    public bool IsWarning => Severity == NotificationSeverity.Warning;
    public bool IsError => Severity == NotificationSeverity.Error;
}
