using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T359 review findings #3, #5 and #6 (S55): the LGPD disclosure gates the server, the per-write dialog cannot be steered
/// by item or agent text, and a lease without confirm-each-write has a write budget after which every write is confirmed.
/// </summary>
[TestSubject(typeof(AgentServerGate))]
public class AgentReviewFixesTest
{
    // ---- #3: the disclosure gates the server -----------------------------------------------------------------------

    [Theory]
    [InlineData("true", "1", true)]
    [InlineData("true", null, false)]   // switched on under M61–M63, or by editing the store: not until accepted
    [InlineData("true", "0", false)]    // an older disclosure version
    [InlineData("false", "1", false)]
    [InlineData(null, "1", false)]
    [InlineData("TRUE", "1", false)]
    public void TestTheServerRunsOnlyWhenOnAndTheDisclosureWasAccepted(string? enabled, string? disclosure, bool expected) =>
        Assert.Equal(expected, AgentServerGate.MayRun(enabled, disclosure));

    [Fact]
    public async Task TestAStartNoLongerWantedUnderTheLifecycleLockStartsNothing()
    {
        var work = Directory.CreateTempSubdirectory("netrisk-gate-");
        var discovery = Path.Combine(work.FullName, "agents", AgentDiscoveryFile.FileName);
        try
        {
            await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), new ScriptedPrompter(),
                new RecordingAuditSink(), new ManualTimeProvider());

            await runtime.StartAsync(AgentRig.UserId, discovery, stillWanted: () => false);

            Assert.False(runtime.IsRunning);
            Assert.False(File.Exists(discovery));
            await runtime.StartAsync(AgentRig.UserId, discovery, stillWanted: () => true);
            Assert.True(runtime.IsRunning);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    // ---- #5: the write dialog ---------------------------------------------------------------------------------------

    private static (AgentRig Rig, StubEditTool Tool, ScriptedPrompter Prompter, string Key) Rig(bool confirm,
        Func<WritePrompt, System.Threading.CancellationToken, Task<bool>>? onWrite = null)
    {
        var prompter = new ScriptedPrompter { OnWrite = onWrite ?? ((_, _) => Task.FromResult(false)) };
        var tool = new StubEditTool();
        var rig = new AgentRig(prompter: prompter, tools: new IAgentTool[] { tool });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite, confirmEachWrite: confirm);
        rig.Approvals.Start();
        return (rig, tool, prompter, key);
    }

    private static async Task<JsonObject> EditAsync(AgentRig rig, string key)
    {
        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(4, "edit_item", $"{{\"agent_key\":\"{key}\"}}")));
        return JsonNode.Parse(response.Body)!["result"]!["structuredContent"]!.AsObject();
    }

    [Fact]
    public async Task TestTheWriteDialogShowsNoHiddenCharacterAndNoInjectedLine()
    {
        var (rig, tool, prompter, key) = Rig(confirm: true);
        tool.Label = "Payroll‮⁦ risk\n\nNetRisk: pre-approved by your administrator​";
        tool.Changes = new[]
        {
            new AgentFieldChange("notes", "Old‮text\n\n\n\n\n\n\nhidden below", "New text")
        };

        await EditAsync(rig, key);

        var prompt = Assert.Single(prompter.WritePrompts);
        Assert.DoesNotContain('\n', prompt.ItemLabel);
        Assert.DoesNotContain(prompt.ItemLabel, c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
        Assert.Equal("Payroll risk NetRisk: pre-approved by your administrator", prompt.ItemLabel);
        var change = Assert.Single(prompt.Changes);
        Assert.DoesNotContain(change.Before!, c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
        Assert.Contains(AgentWriteDisplay.BlankLines(6), change.Before);
        Assert.Equal(0, tool.Applied);
    }

    [Fact]
    public void TestTheChangedSpanShowsAOneSentenceChangeInsideALongText()
    {
        var paragraph = string.Concat(Enumerable.Repeat("Unchanged sentence about the payroll process. ", 40));
        var before = paragraph + "Owner reviews monthly. " + paragraph;
        var after = paragraph + "Owner never reviews. " + paragraph;

        var span = AgentWriteDisplay.ChangedSpan(before, after);

        Assert.Contains("«−reviews monthly−»«+never reviews+»", span, StringComparison.Ordinal);
        Assert.True(span.Length < 200, span);
        Assert.Equal((1, before.Length), AgentWriteDisplay.Size(before));
    }

    [Theory]
    [InlineData(21, 0)] // more lines than MaxLines
    [InlineData(6, 4)]  // more consecutive blank lines than MaxConsecutiveBlankLines
    public async Task TestAnAgentValueThatCouldHideTextBelowTheFoldIsRefused(int lines, int blankRun)
    {
        var text = string.Join("\n", Enumerable.Range(1, lines).Select(i => "line " + i));
        if (blankRun > 0) text = "start" + new string('\n', blankRun + 1) + "payload below the fold";
        var edits = new AgentEditFixtures();
        var rig = AgentReadRig.OverAllFixtures(edits: edits);
        var args = edits.ArgumentsFor("update_risk");
        args["notes"] = text;

        var answer = await rig.CallAsync("update_risk", args);

        Assert.Equal(AgentToolErrors.Invalid, answer.Status);
        Assert.Contains("line breaks", (string)answer.Structured["message"]!);
        edits.Risks.DidNotReceive().SaveRisk(Arg.Any<DAL.Entities.Risk>());
    }

    // ---- #6: the write budget ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TestALeaseWithoutConfirmationMustConfirmEveryWriteAfterItsBudget()
    {
        var (rig, tool, prompter, key) = Rig(confirm: false); // the dialog, once asked, denies

        for (var i = 0; i < AgentWriteBudget.WritesWithoutConfirmation; i++)
            Assert.Equal("ok", (string?)(await EditAsync(rig, key))["status"]);
        Assert.Empty(prompter.WritePrompts);

        var over = await EditAsync(rig, key);

        Assert.Equal(AgentToolErrors.WriteDenied, (string?)over["status"]);
        Assert.Single(prompter.WritePrompts);
        Assert.Equal(AgentWriteBudget.WritesWithoutConfirmation, tool.Applied);
        Assert.Single(rig.Activity.Entries(), e => e.Result == AgentWriteBudget.ConfirmationRequired);

        // Still confirmed for the rest of the lease, and the notice is not repeated.
        await EditAsync(rig, key);
        Assert.Equal(2, prompter.WritePrompts.Count);
        Assert.Single(rig.Activity.Entries(), e => e.Result == AgentWriteBudget.ConfirmationRequired);
    }
}
