using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>Showing views, letting them settle, and the assertions every UI test ends with.</summary>
public static class UiHost
{
    // Generous: a shared CI runner (ui-tests.yml) is several times slower than a workstation.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly List<Window> Shown = new();

    /// <summary>The windows this test has shown through the host, oldest first.</summary>
    public static IReadOnlyList<Window> ShownWindows => Shown;

    /// <summary>Forgets the previous test's windows; called when each test's Application is built.</summary>
    internal static void Reset() => Shown.Clear();

    /// <summary>
    /// Shows <paramref name="window"/> headlessly and runs the dispatcher until its first layout,
    /// render and posted work have run — the point at which styles are applied and every binding
    /// has been evaluated at least once.
    /// </summary>
    public static T Show<T>(T window) where T : Window
    {
        Shown.Add(window);
        window.Show();
        Settle(window);
        return window;
    }

    /// <summary>Hosts a user control in a plain window of the given size and shows it.</summary>
    public static Window Host(Control content, double width = 1280, double height = 900)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        return Show(window);
    }

    /// <summary>Runs queued dispatcher work and one render tick, so pending bindings and layout land.</summary>
    public static void Settle(TopLevel? topLevel = null)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        if (topLevel != null) topLevel.UpdateLayout();
    }

    /// <summary>
    /// Pumps the dispatcher until <paramref name="condition"/> holds. For work a view-model starts
    /// asynchronously (a load posted from an event, a debounced reaction). Fails with
    /// <paramref name="what"/> rather than hanging when it never does.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            if (clock.Elapsed > limit)
                Assert.Fail($"Timed out after {limit.TotalSeconds:0}s waiting for {what}.\n{UiDiagnostics.Describe()}");

            Settle();
            await Task.Delay(15);
        }

        Settle();
    }

    /// <summary>
    /// The assertion S54 §4 requires of every UI test: no standing binding error, no Avalonia error
    /// (a render-loop exception, say) and no exception escaping a ReactiveCommand while the view was
    /// up (see <see cref="UiDiagnostics"/>).
    ///
    /// <paramref name="tolerated"/> names product defects this suite has found and reported but that
    /// T319 does not fix (it changes no production code). Each one must still occur: once someone fixes
    /// it, this fails until the entry is removed from <see cref="KnownDefects"/>, so a tolerance can
    /// never outlive its defect.
    /// </summary>
    public static void AssertClean(params KnownDefect[] tolerated)
    {
        Settle();

        var standing = UiDiagnostics.StandingBindingErrors();

        var unexpected = standing.Where(e => !tolerated.Any(d => d.Matches(e))).ToList();
        var fixedDefects = tolerated.Where(d => !standing.Any(d.Matches)).ToList();

        var report = string.Join(Environment.NewLine, unexpected.Select(e => "[Binding] " + e));
        foreach (var message in UiDiagnostics.FrameworkErrorMessages)
            report += Environment.NewLine + "[Avalonia] " + message;
        foreach (var exception in UiDiagnostics.CommandExceptions)
            report += Environment.NewLine + "[ReactiveCommand] " + exception;

        Assert.True(report.Trim().Length == 0, "The view produced errors while it was shown:\n" + report);
        Assert.True(fixedDefects.Count == 0,
            "These known defects no longer occur — remove them from KnownDefects and from this test: " +
            string.Join("; ", fixedDefects.Select(d => d.Name)));
    }

    /// <summary>The first descendant of <paramref name="root"/> of type <typeparamref name="T"/> matching the predicate.</summary>
    public static T Find<T>(this Visual root, Func<T, bool>? predicate = null) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => predicate?.Invoke(c) ?? true)
        ?? throw new InvalidOperationException(
            $"No {typeof(T).Name} found under {root.GetType().Name}" +
            (predicate == null ? "." : " matching the predicate."));
}
