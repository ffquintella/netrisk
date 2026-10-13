using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using GUIClient.UiTests.Infrastructure;
using ReactiveUI.Avalonia;
using Xunit;

// S54 §4: one headless Application per test (Application and Dispatcher rebuilt every time, so no
// view, double or static subscription carries over), and no parallelism — the headless platform
// does not support concurrent sessions.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// The client's own <see cref="GUIClient.App"/>, minus its start-up side effects.
///
/// Deriving from the real class is the point: <c>App.Initialize</c> loads <c>App.axaml</c> — the
/// Semi theme, the DataGrid and TreeDataGrid themes, the Material icon styles, <c>Tokens.axaml</c>,
/// <c>DarkStyles</c>/<c>WindowStyles</c>/<c>ComponentStyles</c> and the view locator — and applies the
/// localized theme strings, so a view under test resolves exactly the styles and resources it does
/// in the shipped client, and a style class nobody defines is as unstyled here as it is there.
///
/// What is skipped is <c>App.OnFrameworkInitializationCompleted</c>: it checks the configured server,
/// opens the first-run window or the shell, attaches the tray icon and, in Debug builds, starts the
/// in-process DevTools MCP listener on a TCP port. None of that belongs in a test, and the listener
/// would be a network side effect (S54 §4: no test talks to the network).
/// </summary>
public sealed class TestApp : GUIClient.App
{
    /// <summary>
    /// Runs once per test: under <c>AvaloniaTestIsolationLevel.PerTest</c> the headless session calls
    /// <see cref="TestAppBuilder.BuildAvaloniaApp"/> once per assembly and builds a new Application —
    /// this one — for every test. So everything a test must not inherit from the previous one is reset
    /// here, before <c>App.Initialize</c> reads the localizer through the service locator.
    /// </summary>
    public override void Initialize()
    {
        TestAppBuilder.PinCulture();
        UiDiagnostics.Reset();
        UiHost.Reset();
        UiServices.Install();

        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Deliberately not calling base: see the class summary.
    }
}

/// <summary>
/// Entry point named by <c>[assembly: AvaloniaTestApplication]</c>. Called once per test assembly;
/// the per-test work is in <see cref="TestApp.Initialize"/>.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>S54 §4: the culture is pinned, so text the tests assert on does not depend on the host.</summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static AppBuilder BuildAvaloniaApp()
    {
        PinCulture();
        Logger.Sink = UiDiagnostics.Sink;

        // Skia, not the headless stub renderer: the dashboard's LiveCharts controls draw through
        // SkiaSharp, and with the stub every frame threw "SkiaSharp is not supported", the render loop
        // produced no scene, and hit-testing — so every simulated click — found nothing. Skia renders
        // off-screen; no display, Xvfb or GPU is involved on any of the three systems.
        return AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseReactiveUI(reactiveUi => reactiveUi.WithExceptionHandler(UiDiagnostics.CommandExceptionObserver));
    }

    internal static void PinCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
}
