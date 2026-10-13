namespace GUIClient.UiTests.Infrastructure;

/// <summary>A product defect this suite reproduces, reported but not fixed by the change that found it.</summary>
/// <param name="Name">Short name, used in failure messages.</param>
/// <param name="Fragment">A substring of the standing binding error the defect produces.</param>
/// <param name="Reason">What is wrong, where, and why it is tolerated rather than fixed.</param>
public sealed record KnownDefect(string Name, string Fragment, string Reason)
{
    public bool Matches(string bindingError) => bindingError.Contains(Fragment, System.StringComparison.Ordinal);
}

/// <summary>
/// Defects found by the UI suite and tolerated, by name, in the tests that reproduce them
/// (<see cref="UiHost.AssertClean"/>), while they wait for their fix. The list is meant to stay short: an entry
/// whose defect stops occurring fails the test that tolerates it, so it has to be removed with the fix.
///
/// Empty since 2026-10-13. The three defects the first run of T319 found were fixed by T324 (risk status colour
/// with no selection), T325 (status icons on a nullable icon target) and T326 (findings toolbar permission gate);
/// their regression tests are <c>GUIClient.UiTests/Converters/StatusToColourConverterTest</c>,
/// <c>GUIClient.Tests/Tools/StatusIconMapTest</c> and <c>GUIClient.Tests/Tools/PermissionGateTest</c>.
/// </summary>
public static class KnownDefects
{
}
