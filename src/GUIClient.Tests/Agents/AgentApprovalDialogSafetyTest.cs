using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// Review finding 8: the approval dialogs come forward on their own while the user may be typing elsewhere, so a stray
/// Space or Enter must not approve. Each dialog focuses Deny when it opens, and its approving command stays disabled
/// until the dialog has been on screen for <c>AgentApprovalTiming.ArmDelay</c>. Scanned as text because this project
/// does not load Avalonia views (see the csproj).
/// </summary>
public class AgentApprovalDialogSafetyTest
{
    [Theory]
    [InlineData("AgentRegistrationDialog", "AgentRegistrationDialogViewModel", "BtApproveClicked")]
    [InlineData("AgentAccessDialog", "AgentAccessDialogViewModel", "BtGrantClicked")]
    [InlineData("AgentWriteDialog", "AgentWriteDialogViewModel", "BtApproveClicked")] // T357
    public void TestDenyHasTheFocusAndApprovingWaitsUntilTheDialogIsArmed(string view, string viewModel, string command)
    {
        var markup = Read("Views", "Agents", view + ".axaml");
        var codeBehind = Read("Views", "Agents", view + ".axaml.cs");
        var model = Read("ViewModels", "Agents", viewModel + ".cs");

        Assert.Contains("Name=\"BtDeny\"", markup);
        Assert.DoesNotContain("IsDefault=\"True\"", markup);
        Assert.Matches(new Regex(@"FindControl<Button>\(""BtDeny""\)\?\.Focus\("), codeBehind);
        Assert.Contains("StartArming(AgentApprovalTiming.ArmDelay)", codeBehind);
        Assert.Matches(new Regex(command + @"\s*=\s*ReactiveCommand\.Create\([^;]*WhenAnyValue\(x => x\.IsDecisionArmed\)"),
            model);
    }

    private static string Read(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project))
                return File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, Path.Combine(parts)));
        }
        throw new InvalidOperationException("Could not find GUIClient/GUIClient.csproj from " + AppContext.BaseDirectory);
    }
}
