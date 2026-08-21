using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace GUIClient.Notifications;

/// <summary>
/// Default <see cref="INotificationService"/>: keeps a small, self-expiring queue of
/// notifications that the shell's toast host renders. Registered as a singleton so any
/// view-model can report a routine success without knowing anything about the UI.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LongLifetime = TimeSpan.FromSeconds(8);

    /// <summary>Bound by the shell's toast host. Newest first.</summary>
    public ObservableCollection<AppNotification> Notifications { get; } = new();

    public void Success(string message) => Post(message, NotificationSeverity.Success, DefaultLifetime);

    public void Info(string message) => Post(message, NotificationSeverity.Info, DefaultLifetime);

    public void Warning(string message) => Post(message, NotificationSeverity.Warning, LongLifetime);

    public void Error(string message) => Post(message, NotificationSeverity.Error, LongLifetime);

    private void Post(string message, NotificationSeverity severity, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var notification = new AppNotification(message, severity, lifetime);

        // Callers are frequently on a background thread coming back from a REST call.
        Dispatcher.UIThread.Post(() =>
        {
            Notifications.Insert(0, notification);

            // Keep the stack shallow: older toasts are noise once a few pile up.
            while (Notifications.Count > 4)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }

            DispatcherTimer.RunOnce(() => Notifications.Remove(notification), lifetime);
        });
    }
}
