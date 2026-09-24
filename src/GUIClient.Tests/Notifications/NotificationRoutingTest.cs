using System.Collections.Generic;
using GUIClient.Notifications;
using Xunit;

namespace GUIClient.Tests.Notifications;

/// <summary>
/// The regression these cover: every toast used to go to the single stack owned by the main
/// window, so a save performed in the Administration window reported itself behind that window.
/// </summary>
public class NotificationRoutingTest
{
    private static NotificationTargetState Shell(bool active) => new(active, IsVisible: true, IsShell: true);

    private static NotificationTargetState Child(bool active, bool visible = true) =>
        new(active, visible, IsShell: false);

    [Fact]
    public void ActiveChildWindowReceivesTheNotification()
    {
        var targets = new List<NotificationTargetState> { Shell(active: false), Child(active: true) };

        Assert.Equal(1, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void ActiveShellReceivesTheNotificationWhileAChildIsOpen()
    {
        var targets = new List<NotificationTargetState> { Shell(active: true), Child(active: false) };

        Assert.Equal(0, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void FrontmostOfSeveralActiveWindowsWins()
    {
        // A dialog over the Administration window: both can report as active on some platforms,
        // and the one registered last is the one in front.
        var targets = new List<NotificationTargetState> { Shell(active: true), Child(active: true), Child(active: true) };

        Assert.Equal(2, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void NothingFocusedFallsBackToTheShell()
    {
        var targets = new List<NotificationTargetState> { Shell(active: false), Child(active: false) };

        Assert.Equal(0, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void AnInvisibleWindowIsNotATarget()
    {
        var targets = new List<NotificationTargetState> { Child(active: true, visible: false), Child(active: false) };

        Assert.Equal(1, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void NoVisibleHostDropsTheNotification()
    {
        var targets = new List<NotificationTargetState> { Child(active: true, visible: false) };

        Assert.Equal(NotificationRouting.NoTarget, NotificationRouting.SelectTarget(targets));
    }

    [Fact]
    public void NoHostAtAllDropsTheNotification()
    {
        Assert.Equal(NotificationRouting.NoTarget, NotificationRouting.SelectTarget(new List<NotificationTargetState>()));
    }
}
