using System;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Threading;
using GUIClient.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;

namespace GUIClient.Tools;

/// <summary>
/// The app-wide last resort for exceptions that escape a ReactiveCommand.
///
/// ReactiveUI routes any exception thrown inside a command to its default exception handler,
/// and the built-in default *rethrows* it on the dispatcher. Since nothing above the dispatcher
/// catches it, a single bad command took the whole process down with a <c>SIGABRT</c> — a
/// missing dialog view used to kill the app outright rather than report itself. Nothing in a
/// desktop app is worth losing the user's unsaved work over, so this handler logs and reports
/// instead.
///
/// This is deliberately a floor, not a substitute for handling errors where they happen: a
/// command with a known failure mode should still surface it in context.
/// </summary>
public static class ReactiveErrorHandler
{
    /// <summary>
    /// The observer to hand to <c>IReactiveUIBuilder.WithExceptionHandler</c> during
    /// <c>UseReactiveUI</c>. ReactiveUI 24 made its handler read-only after initialization, so it
    /// has to be supplied at builder time — which is also the earliest possible point, before any
    /// view model (and therefore any ReactiveCommand) exists.
    /// </summary>
    public static IObserver<Exception> CreateObserver() => Observer.Create<Exception>(Report);

    private static void Report(Exception exception)
    {
        // A handler that throws would be no better than the default it replaces, so every step
        // from here down is guarded.
        try
        {
            Log.Error(exception, "Unhandled error in a ReactiveCommand pipeline");
        }
        catch
        {
            Console.WriteLine("Unhandled error in a ReactiveCommand pipeline: {0}", exception);
        }

        try
        {
            Dispatcher.UIThread.Post(() => _ = ShowAsync(exception));
        }
        catch
        {
            // No dispatcher to report on (shutting down, or the failure happened before the UI
            // came up). The log line above is all we can offer.
        }
    }

    private static async System.Threading.Tasks.Task ShowAsync(Exception exception)
    {
        try
        {
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Title(),
                    ContentMessage = Unwrap(exception).Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
        }
        catch (Exception reportingFailure)
        {
            // Never let the report itself escape — it would land back in this same handler.
            try
            {
                Log.Error(reportingFailure, "Could not display the error dialog for {Original}", exception.Message);
            }
            catch
            {
                // Nothing left to try.
            }
        }
    }

    /// <summary>
    /// ReactiveUI wraps the real fault in an <c>UnhandledErrorException</c> whose own message is
    /// boilerplate about observable pipelines. Report the cause the user can act on instead.
    /// </summary>
    private static Exception Unwrap(Exception exception) =>
        exception is UnhandledErrorException { InnerException: { } cause } ? cause : exception;

    private static string Title()
    {
        try
        {
            return ViewModelBase.Localizer["Error"];
        }
        catch
        {
            // Localization is resolved from the DI container and may not be up yet.
            return "Error";
        }
    }
}
