using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// The classic-desktop application lifetime the shipped client always runs under, for the headless session —
/// which has none of its own.
///
/// It matters because of the client's message boxes. Every confirmation (<c>ConfirmationDialog</c>) and every error
/// box is a <c>MessageBoxManager.GetMessageBoxStandard(…).ShowAsync()</c>, and MsBox.Avalonia decides how to show
/// itself from <see cref="Application.ApplicationLifetime"/>: as a window under a classic-desktop lifetime, as a popup
/// under a single-view one, and otherwise it throws "ApplicationLifetime is not supported". Without a lifetime a flow
/// that asks "delete X?" fails on the harness rather than on anything the client does.
///
/// <para><b>How it is installed.</b> Avalonia refuses to change <see cref="Application.ApplicationLifetime"/> once
/// the Application has started initializing — <c>TestApp.Initialize</c> is already too late — and Avalonia 12 does
/// not let user code implement the lifetime interfaces. What it does is hand every Application it builds the
/// lifetime stored on the <see cref="AppBuilder"/>, which only <c>SetupWithLifetime</c> sets, and that also runs the
/// setup there and then, outside the headless session. So <see cref="TestAppBuilder"/> stores one on the builder
/// through <see cref="AttachTo"/> (reflection, resolved once, failing loudly by name if Avalonia renames the field),
/// and every test's Application starts with it.</para>
///
/// <para><b>What it does.</b> It is Avalonia's own <see cref="ClassicDesktopStyleApplicationLifetime"/>, never started
/// and shared by the run (Avalonia allows one live instance), with <see cref="ShutdownMode.OnExplicitShutdown"/> so a
/// closing window never shuts anything down. Its <c>MainWindow</c> stays null — what the client code that reads it
/// (<c>NotificationService</c>) saw before it existed — and <c>IMainWindowProvider</c> remains a double. The windows a
/// test opens are recorded here, per test (<see cref="Windows"/>), so a message box — which MsBox shows with no
/// owner — can be found.</para>
/// </summary>
public static class UiDesktop
{
    private static readonly List<Window> Opened = [];
    private static bool _tracking;

    private static readonly Lazy<FieldInfo> BuilderLifetimeField = new(() =>
        typeof(AppBuilder).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "AppBuilder._lifetime not found: the UI test harness gives the headless Application a classic-desktop " +
            "lifetime through it (see UiDesktop)."));

    /// <summary>Stores the lifetime on <paramref name="builder"/>, so every Application it sets up starts with it.</summary>
    internal static AppBuilder AttachTo(AppBuilder builder)
    {
        BuilderLifetimeField.Value.SetValue(builder,
            new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown });

        TrackWindows();
        return builder;
    }

    /// <summary>Forgets the previous test's windows; called when each test's Application is built.</summary>
    internal static void Reset() => Opened.Clear();

    /// <summary>The windows this test has opened and not closed, oldest first.</summary>
    public static IReadOnlyList<Window> Windows => Opened;

    /// <summary>
    /// Routed-event class handlers are process-wide, so they are added once and record into the running test's list.
    /// </summary>
    private static void TrackWindows()
    {
        if (_tracking) return;
        _tracking = true;

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (!Opened.Contains(window)) Opened.Add(window);
        }, handledEventsToo: true);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Opened.Remove(window), handledEventsToo: true);
    }
}

/// <summary>
/// The client's MsBox message boxes — confirmations and error boxes — as a test meets them: wait for one, read what
/// it says, answer it with a simulated click.
/// </summary>
public static class UiMessageBoxes
{
    /// <summary>The message boxes open now, oldest first.</summary>
    public static IReadOnlyList<Window> Open() =>
        UiDesktop.Windows.Where(IsMessageBox).ToList();

    /// <summary>Pumps the dispatcher until a message box is open, and returns it laid out.</summary>
    public static async Task<Window> WaitForAsync(string what)
    {
        Window? box = null;
        await UiHost.WaitUntilAsync(() => (box = Open().LastOrDefault()) != null, what);
        UiHost.Settle(box);
        return box!;
    }

    /// <summary>Everything the box says — its title and message — as one string, for <c>Assert.Contains</c>.</summary>
    public static string Text(Window box) =>
        string.Join("\n", new[] { box.Title }
            .Concat(box.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .Concat(box.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text))
            .Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>Clicks the box's button captioned <paramref name="caption"/> ("Yes", "No", "Ok") and waits for it to close.</summary>
    public static async Task AnswerAsync(Window box, string caption)
    {
        // MsBox lays out every button it has and hides the ones the box does not use.
        var buttons = box.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        var button = buttons.FirstOrDefault(b => string.Equals(Caption(b), caption, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException(
                         $"The message box has no \"{caption}\" button; it has: " +
                         string.Join(", ", buttons.Select(Caption)));

        UiInput.Click(button);
        await UiHost.WaitUntilAsync(() => !Open().Contains(box), $"the message box to close after \"{caption}\"");
    }

    private static string? Caption(Button button) => button.Content switch
    {
        string text => text,
        Control content => content.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Prepend((content as TextBlock)?.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
        _ => button.Content?.ToString()
    };

    private static bool IsMessageBox(Window window) =>
        window.GetType().Namespace?.StartsWith("MsBox.Avalonia", StringComparison.Ordinal) == true;
}
