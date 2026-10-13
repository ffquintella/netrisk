using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// M60 / S54 (docs/testing/desktop-ui-tests.md) — the headless Avalonia UI tests of the desktop client.
///
/// <b>Execution rule (S54 §3).</b> The UI tests are too expensive for the development loop. They run
/// only when the user asks for them, or as the release gate before <c>git tag vX.Y.Z</c> — never from
/// <c>./build.sh</c>, <c>Compile</c>, a <c>Package*</c>/<c>Bump*</c> target, or push/PR CI. So:
/// <list type="bullet">
/// <item><see cref="CompileUiTests"/> only <em>builds</em> the project, and <c>Compile</c> depends on it
/// (E3: a renamed view-model or a removed view breaks the build, which is cheap);</item>
/// <item><see cref="TestUi"/> is the single entry point that <em>runs</em> them. Nothing depends on it,
/// and it must stay that way: adding it to another target's <c>DependsOn</c>/<c>Triggers</c> breaks E1.</item>
/// </list>
/// </summary>
partial class Build
{
    AbsolutePath UiTestsProject => SourceDirectory / "GUIClient.UiTests" / "GUIClient.UiTests.csproj";

    /// <summary>Where <see cref="TestUi"/> writes its reports; ui-tests.yml uploads it on failure.</summary>
    AbsolutePath UiTestsResultsDirectory => OutputDirectory / "ui-tests";

    Target CompileUiTests => _ => _
        .Description("Compile the headless desktop UI tests without running them (S54 §3, E3)")
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(UiTestsProject)
                .SetConfiguration(Configuration)
                .SetVerbosity(DotNetVerbosity.minimal)
                .DisableProcessOutputLogging());
        });

    Target TestUi => _ => _
        .Description("Run the headless desktop UI tests — on explicit request or as the release gate only (S54 §3)")
        .DependsOn(CompileUiTests)
        .Executes(() =>
        {
            var assembly = SourceDirectory / "GUIClient.UiTests" / "bin" / Configuration.ToString() / "net10.0" /
                           "GUIClient.UiTests.dll";
            Assert.FileExists(assembly);

            UiTestsResultsDirectory.CreateOrCleanDirectory();

            // The opt-in the [UiFact]/[UiTheory] attributes read (S54 §3.2, lock 2). It is set for this
            // child process only, never for the build, so nothing else Nuke runs inherits it.
            var environment = EnvironmentInfo.Variables.ToDictionary(v => v.Key, v => v.Value);
            environment["NETRISK_UI_TESTS"] = "1";

            // `dotnet exec` rather than the apphost, which cannot find a runtime installed outside the
            // default location (Homebrew, a CI tool cache) without DOTNET_ROOT. -failSkips: with the
            // opt-in set nothing may skip, so a gate that stopped opening fails instead of passing empty.
            DotNet(
                $"exec \"{assembly}\" -failSkips -noColor " +
                $"-xml \"{UiTestsResultsDirectory / "GUIClient.UiTests.xml"}\" " +
                $"-html \"{UiTestsResultsDirectory / "GUIClient.UiTests.html"}\"",
                workingDirectory: RootDirectory,
                environmentVariables: environment);
        });
}
