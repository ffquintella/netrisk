using System;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.v3;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// The only way a UI test is declared in this project (S54 §3.2, lock 2).
///
/// Behaves exactly like <see cref="AvaloniaFactAttribute"/> — it names the same public discoverer,
/// <see cref="AvaloniaFactDiscoverer"/>, so the test body runs on the headless Avalonia dispatcher —
/// and additionally skips itself unless the run was opted into with <c>NETRISK_UI_TESTS=1</c>.
/// Anyone who starts the <c>GUIClient.UiTests</c> executable by accident sees every UI test skipped
/// and the reason, instead of a UI run.
///
/// Why not <c>: AvaloniaFactAttribute</c>, as S54 §3.2 first sketched: that class is sealed in
/// Avalonia.Headless.XUnit 12.x. What makes a fact "Avalonia" is the discoverer its attribute names,
/// so naming the same one is the equivalent (recorded as an amendment in S54).
///
/// <see cref="Guards.UiTestAttributeGuardTest"/> fails on any test method declared with something
/// else (a bare <c>[Fact]</c> or <c>[AvaloniaFact]</c> would slip past this lock).
/// </summary>
[XunitTestCaseDiscoverer(typeof(AvaloniaFactDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!UiTestGate.IsEnabled)
            Skip = UiTestGate.SkipReason;
    }
}

/// <summary>The <see cref="AvaloniaTheoryAttribute"/> counterpart of <see cref="UiFactAttribute"/>.</summary>
[XunitTestCaseDiscoverer(typeof(AvaloniaTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiTheoryAttribute : TheoryAttribute
{
    public UiTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!UiTestGate.IsEnabled)
            Skip = UiTestGate.SkipReason;
    }
}

/// <summary>The opt-in switch both attributes read (S54 §3.2).</summary>
public static class UiTestGate
{
    public const string VariableName = "NETRISK_UI_TESTS";

    public const string SkipReason =
        "UI tests run only on demand or at release — set NETRISK_UI_TESTS=1 (S54 §3).";

    public static bool IsEnabled => Environment.GetEnvironmentVariable(VariableName) == "1";
}
