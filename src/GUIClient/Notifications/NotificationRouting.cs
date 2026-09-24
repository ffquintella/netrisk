using System.Collections.Generic;

namespace GUIClient.Notifications;

/// <summary>
/// One registered toast host, described without any Avalonia type so the routing decision can be
/// tested headlessly.
/// </summary>
/// <param name="IsActive">The host's window currently has focus.</param>
/// <param name="IsVisible">The host's window is on screen.</param>
/// <param name="IsShell">The host belongs to the main window.</param>
public readonly record struct NotificationTargetState(bool IsActive, bool IsVisible, bool IsShell);

/// <summary>
/// Decides which window's toast stack receives a notification.
///
/// A toast is feedback about what the user just did, so it belongs on the window the user is
/// looking at. Before this existed there was a single stack owned by the main window, and a save
/// performed in the Administration window (or in any dialog) reported itself behind that window,
/// where nobody saw it.
/// </summary>
public static class NotificationRouting
{
    /// <summary>Returned when nothing can render the notification; the caller drops it.</summary>
    public const int NoTarget = -1;

    /// <summary>
    /// Returns the index of the receiving host in <paramref name="targets"/>, which is in
    /// registration order (oldest window first).
    /// </summary>
    public static int SelectTarget(IReadOnlyList<NotificationTargetState> targets)
    {
        // Newest first: a dialog opened over the Administration window is the one in front.
        for (var i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i].IsActive && targets[i].IsVisible) return i;
        }

        // Nothing is focused (the app is in the background, or the work finished after the window
        // that started it closed). The shell is the one window the user is sure to come back to.
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].IsShell && targets[i].IsVisible) return i;
        }

        for (var i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i].IsVisible) return i;
        }

        return NoTarget;
    }
}
