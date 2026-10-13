using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.VisualTree;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// Collects the failures a view can produce without throwing, so a test can fail on them
/// (S54 §4, "Erros de binding reprovam o teste").
///
/// Avalonia does not throw on a broken binding — it logs a warning in the <c>Binding</c> area and
/// renders the fallback — and ReactiveUI routes an exception escaping a command to a handler instead
/// of the caller. Both are invisible to <c>LintUi</c> and to the Avalonia-free <c>GUIClient.Tests</c>,
/// which is exactly why this layer exists. Error-level entries from any other area (a render-loop
/// exception, say) are collected too and always fail. Installed by <see cref="TestAppBuilder"/>,
/// cleared for every test by <see cref="TestApp"/>; <see cref="UiHost.AssertClean"/> is the assertion.
///
/// <para><b>What counts as a binding error.</b> Every <c>Binding</c>-area warning is collected, and
/// all of them fail the test except two kinds that describe a moment rather than a defect, and only
/// once the view has settled (S54 amendment 2026-10-13):</para>
/// <list type="number">
///   <item><b>A data context that has since been replaced.</b> A child view declares its own
///   <c>x:DataType</c> and takes its data context from a parent property
///   (<c>DataContext="{Binding RiskTailPanel}"</c>). Until that binding lands, the child's bindings see
///   the parent's view-model and log "Unable to cast object of type 'Parent' to type 'Child'". The
///   error is ignored only if the element no longer holds a 'Parent' when the view has settled; if it
///   still does, the child is bound to the wrong view-model and the test fails.</item>
///   <item><b>A null link in the middle of a path</b> ("Value is null"): <c>SelectedHost.Name</c>
///   before anything is selected. That is data the empty-server doubles do not provide, not a broken
///   binding — a wrong path or property name fails differently ("Could not find a matching property
///   accessor…"), and so does a failed conversion ("Could not convert…"); both fail the test.</item>
/// </list>
/// </summary>
public static class UiDiagnostics
{
    private static readonly ConcurrentQueue<BindingError> BindingErrors = new();
    private static readonly ConcurrentQueue<string> FrameworkErrors = new();
    private static readonly ConcurrentQueue<Exception> CommandErrors = new();

    private static readonly Regex InvalidCast = new(
        @"Unable to cast object of type '(?<from>[^']+)' to type '(?<to>[^']+)'", RegexOptions.Compiled);

    private const string NullLink = "Value is null.";

    /// <summary>The Avalonia log sink: keeps <c>Binding</c>-area warnings and errors.</summary>
    public static ILogSink Sink { get; } = new BindingErrorSink();

    /// <summary>Handed to <c>UseReactiveUI(…WithExceptionHandler…)</c> in place of the client's dialog.</summary>
    public static IObserver<Exception> CommandExceptionObserver { get; } =
        Observer.Create<Exception>(ex => CommandErrors.Enqueue(ex));

    public static void Reset()
    {
        BindingErrors.Clear();
        FrameworkErrors.Clear();
        CommandErrors.Clear();
    }

    /// <summary>The binding errors that still describe the view as it is now (see the class summary).</summary>
    public static IReadOnlyList<string> StandingBindingErrors() =>
        BindingErrors.Where(e => e.IsStanding()).Select(e => e.Text).Distinct().ToList();

    public static IReadOnlyList<Exception> CommandExceptions => CommandErrors.ToArray();

    /// <summary>
    /// Error-level entries Avalonia logged outside the Binding area — above all "Exception in render
    /// loop", which is how a control that cannot draw (or a renderer that cannot run) shows up. Always
    /// a failure: the first thing the render loop stops doing is the hit-testing every click needs.
    /// </summary>
    public static IReadOnlyList<string> FrameworkErrorMessages => FrameworkErrors.Distinct().ToList();

    /// <summary>A readable report of everything that should fail a test, for an assertion message.</summary>
    public static string Describe()
    {
        var report = new StringBuilder();

        foreach (var message in StandingBindingErrors())
            report.AppendLine("[Binding] " + message);

        foreach (var message in FrameworkErrorMessages)
            report.AppendLine("[Avalonia] " + message);

        foreach (var exception in CommandErrors)
            report.AppendLine("[ReactiveCommand] " + exception);

        return report.ToString();
    }

    private sealed record BindingError(object? Source, string Message, string Text)
    {
        public bool IsStanding()
        {
            if (Message.EndsWith(NullLink, StringComparison.Ordinal)) return false;

            var cast = InvalidCast.Match(Message);
            if (!cast.Success) return true;

            // Standing only while the element still sees the view-model it could not cast.
            var context = DataContextOf(Source);
            return context != null && context.GetType().FullName == cast.Groups["from"].Value;
        }

        /// <summary>
        /// The data context the failed binding read. A DataGrid column is not a control and has no
        /// data context of its own; its bindings resolve against the grid's.
        /// </summary>
        private static object? DataContextOf(object? source) => source switch
        {
            StyledElement element => element.DataContext,
            DataGridColumn column => UiHost.ShownWindows
                .SelectMany(w => w.GetVisualDescendants().OfType<DataGrid>())
                .FirstOrDefault(grid => grid.Columns.Contains(column))?.DataContext,
            _ => null
        };
    }

    private sealed class BindingErrorSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) =>
            area == LogArea.Binding ? level >= LogEventLevel.Warning : level >= LogEventLevel.Error;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            if (!IsEnabled(level, area)) return;

            var message = Render(messageTemplate, propertyValues);
            var text = $"{level} on {source?.GetType().Name ?? "?"}: {message}";

            if (area == LogArea.Binding)
                BindingErrors.Enqueue(new BindingError(source, message, text));
            else
                FrameworkErrors.Enqueue($"{area} — {text}");
        }

        /// <summary>Fills Serilog-style "{Name}" holes positionally, as Avalonia's own sinks do.</summary>
        private static string Render(string template, object?[] values)
        {
            var index = 0;
            return Regex.Replace(template, @"\{[^}]+\}",
                match => index < values.Length ? values[index++]?.ToString() ?? "(null)" : match.Value);
        }
    }
}
