using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace GUIClient.Notifications;

/// <summary>
/// Default <see cref="INotificationService"/>: keeps a small, self-expiring queue of
/// notifications per window, rendered by that window's toast host. Registered as a singleton so
/// any view-model can report a routine success without knowing anything about the UI — the
/// notification is routed to the window the user is actually looking at
/// (see <see cref="NotificationRouting"/>).
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LongLifetime = TimeSpan.FromSeconds(8);

    /// <summary>Registration order, oldest window first. Only touched on the UI thread.</summary>
    private readonly List<ToastTarget> _targets = new();

    /// <summary>
    /// Registers <paramref name="window"/>'s toast host and returns the collection it renders.
    /// Calling twice for the same window returns the same collection.
    /// </summary>
    public ObservableCollection<AppNotification> AttachHost(Window window)
    {
        var existing = _targets.FirstOrDefault(t => ReferenceEquals(t.Window, window));
        if (existing is not null) return existing.Notifications;

        var target = new ToastTarget(window);
        _targets.Add(target);
        return target.Notifications;
    }

    /// <summary>Drops a window's host when it closes, so its queue is not a routing candidate.</summary>
    public void DetachHost(Window window) => _targets.RemoveAll(t => ReferenceEquals(t.Window, window));

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
            var shell = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;

            var states = _targets
                .Select(t => new NotificationTargetState(t.Window.IsActive, t.Window.IsVisible,
                    ReferenceEquals(t.Window, shell)))
                .ToList();

            var index = NotificationRouting.SelectTarget(states);
            if (index == NotificationRouting.NoTarget) return;

            var notifications = _targets[index].Notifications;
            notifications.Insert(0, notification);

            // Keep the stack shallow: older toasts are noise once a few pile up.
            while (notifications.Count > 4)
            {
                notifications.RemoveAt(notifications.Count - 1);
            }

            DispatcherTimer.RunOnce(() => notifications.Remove(notification), lifetime);
        });
    }

    private sealed class ToastTarget
    {
        public ToastTarget(Window window) => Window = window;

        public Window Window { get; }

        public ObservableCollection<AppNotification> Notifications { get; } = new();
    }
}
