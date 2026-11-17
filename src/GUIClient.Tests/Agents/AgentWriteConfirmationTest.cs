using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T357 (S55 confirm-each-write): under a lease that asks for it, every edit waits for the user's approval in a dialog
/// showing the tool, the item and each field's old and new value — one dialog at a time. Deny, a time-out, the lease
/// ending or being paused while the dialog is open, or no approval queue at all write nothing and answer cleanly.
/// </summary>
[TestSubject(typeof(AgentApprovalCoordinator))]
public class AgentWriteConfirmationTest
{
    private static (AgentRig Rig, StubEditTool Tool, ScriptedPrompter Prompter, AgentRegistration Agent, string Key)
        Confirming(Func<WritePrompt, CancellationToken, Task<bool>> onWrite, TimeSpan? wait = null)
    {
        var prompter = new ScriptedPrompter { OnWrite = onWrite };
        var tool = new StubEditTool();
        var rig = new AgentRig(prompter: prompter, tools: new IAgentTool[] { tool }, writeConfirmationWait: wait);
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite, confirmEachWrite: true);
        rig.Approvals.Start();
        return (rig, tool, prompter, agent, key);
    }

    private static async Task<JsonObject> EditAsync(AgentRig rig, string key)
    {
        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(4, "edit_item", $"{{\"agent_key\":\"{key}\"}}")));
        return JsonNode.Parse(response.Body)!["result"]!["structuredContent"]!.AsObject();
    }

    [Fact]
    public async Task TestAnApprovedWriteRunsAndTheDialogShowedTheExactChange()
    {
        var (rig, tool, prompter, agent, key) = Confirming((_, _) => Task.FromResult(true));

        var result = await EditAsync(rig, key);

        Assert.Equal("ok", (string?)result["status"]);
        Assert.Equal(1, tool.Applied);
        var prompt = Assert.Single(prompter.WritePrompts);
        Assert.Equal("edit_item", prompt.ToolName);
        Assert.Equal(agent.Name, prompt.AgentName);
        Assert.Equal(agent.Fingerprint, prompt.AgentFingerprint);
        Assert.Equal(new AgentItemRef(AgentItemKinds.Risk, 1), prompt.Item);
        Assert.Equal(new AgentFieldChange("notes", "old text", "new text"), Assert.Single(prompt.Changes));
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.WriteConfirmed);
    }

    [Fact]
    public async Task TestADeniedWriteIsACleanRefusalAndWritesNothing()
    {
        var (rig, tool, _, _, key) = Confirming((_, _) => Task.FromResult(false));

        var result = await EditAsync(rig, key);

        Assert.Equal(AgentToolErrors.WriteDenied, (string?)result["status"]);
        Assert.Equal(2, result.Count); // status and message, nothing else
        Assert.Equal(0, tool.Applied);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.WriteDeclined);
        Assert.Null(rig.Presence.ChangedSince(new AgentItemRef(AgentItemKinds.Risk, 1), DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task TestAWriteNotConfirmedInTimeWritesNothing()
    {
        var (rig, tool, _, _, key) = Confirming(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, wait: TimeSpan.FromMilliseconds(100));

        var result = await EditAsync(rig, key);

        Assert.Equal(AgentToolErrors.NotConfirmed, (string?)result["status"]);
        Assert.Equal(0, tool.Applied);
    }

    [Theory]
    [InlineData("ended", "access_expired")]
    [InlineData("paused", "paused")]
    [InlineData("revoked", "access_expired")]
    public async Task TestAccessEndingWhileTheDialogIsOpenClosesItAndWritesNothing(string change, string status)
    {
        var dialogClosed = new TaskCompletionSource();
        AgentRig? rigRef = null;
        AgentRegistration? agentRef = null;
        var (rig, tool, _, agent, key) = Confirming(async (_, token) =>
        {
            token.Register(() => dialogClosed.TrySetResult());
            switch (change)
            {
                case "ended": rigRef!.Leases.End(agentRef!.Id, LeaseEndReason.EndedByUser); break;
                case "paused": rigRef!.Leases.Pause(agentRef!.Id); break;
                default: rigRef!.Leases.End(agentRef!.Id, LeaseEndReason.Revoked); break;
            }
            await Task.Delay(Timeout.Infinite, token);
            return true; // never reached: the dialog was closed under the user
        });
        rigRef = rig;
        agentRef = agent;

        var result = await EditAsync(rig, key);

        Assert.Equal(status, (string?)result["status"]);
        Assert.Equal(0, tool.Applied);
        await dialogClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TestWritesFromSeveralAgentsAreConfirmedOneDialogAtATime()
    {
        var prompter = new ScriptedPrompter
        {
            OnWrite = async (_, _) =>
            {
                await Task.Delay(30);
                return true;
            }
        };
        var tool = new StubEditTool();
        var rig = new AgentRig(prompter: prompter, tools: new IAgentTool[] { tool });
        var keys = Enumerable.Range(0, 4).Select(i =>
        {
            var (agent, key) = rig.RegisterApproved("Agent " + i);
            rig.Grant(agent, 1, AgentAccessMode.ReadWrite, confirmEachWrite: true);
            return key;
        }).ToList();
        rig.Approvals.Start();

        var results = await Task.WhenAll(keys.Select(key => EditAsync(rig, key)));

        Assert.All(results, r => Assert.Equal("ok", (string?)r["status"]));
        Assert.Equal(4, tool.Applied);
        Assert.Equal(1, prompter.MaxOnScreen);
        Assert.Equal(4, prompter.WritePrompts.Count);
    }

    /// <summary>Review M63 #6: one change per agent waits for the user; a parallel one is refused before it reads anything.</summary>
    [Fact]
    public async Task TestASecondChangeWhileOneWaitsForTheUserIsBusyAndWritesNothing()
    {
        var shown = new TaskCompletionSource();
        var release = new TaskCompletionSource<bool>();
        var (rig, tool, prompter, _, key) = Confirming((_, _) =>
        {
            shown.TrySetResult();
            return release.Task;
        });

        var first = EditAsync(rig, key);
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await EditAsync(rig, key);
        release.SetResult(true);
        var firstResult = await first;
        prompter.OnWrite = (_, _) => Task.FromResult(true);
        var third = await EditAsync(rig, key);

        Assert.Equal("busy", (string?)second["status"]);
        Assert.Equal("ok", (string?)firstResult["status"]);
        Assert.Equal("ok", (string?)third["status"]);          // the slot is free again once the first was answered
        Assert.Equal(2, tool.Prepared);                         // the busy one read nothing
        Assert.Equal(2, tool.Applied);
    }

    /// <summary>
    /// Review M63 #8: the user signs out, or another user signs in, while the dialog is open, and then the dialog is
    /// approved. The gate checked again before the write is what stops it.
    /// </summary>
    [Theory]
    [InlineData("signed-out")]
    [InlineData("other-user")]
    [InlineData("permission-lost")]
    public async Task TestAnApprovalAfterTheSessionChangedWritesNothing(string change)
    {
        AgentRig? rigRef = null;
        var (rig, tool, _, _, key) = Confirming((_, _) =>
        {
            switch (change)
            {
                case "signed-out": rigRef!.User = null; break;
                case "other-user":
                    var other = AgentRig.DefaultUser(StubTool.StubPermission);
                    other.UserId = AgentRig.UserId + 1;
                    rigRef!.User = other;
                    break;
                default: rigRef!.User = AgentRig.DefaultUser("something_else"); break;
            }
            return Task.FromResult(true);
        });
        rigRef = rig;

        var result = await EditAsync(rig, key);

        Assert.Equal(change == "permission-lost" ? "forbidden" : "unavailable", (string?)result["status"]);
        Assert.Equal(0, tool.Applied);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.ToolRefused && e.Detail.EndsWith("before the write"));
    }

    /// <summary>
    /// Review M63 #5: a write that landed just before access ended is reported as landed — field names and version only —
    /// and listed as such, never as "nothing happened".
    /// </summary>
    [Theory]
    [InlineData("ended")]
    [InlineData("paused")]
    [InlineData("signed-out")]
    public async Task TestAccessEndingAfterTheWriteIsReportedAsALandedWrite(string change)
    {
        var tool = new StubEditTool();
        var rig = new AgentRig(tools: new IAgentTool[] { tool });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite);
        tool.OnApply = () =>
        {
            switch (change)
            {
                case "ended": rig.Leases.End(agent.Id, LeaseEndReason.EndedByUser); break;
                case "paused": rig.Leases.Pause(agent.Id); break;
                default: rig.User = null; break;
            }
        };

        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(4, "edit_item", $"{{\"agent_key\":\"{key}\"}}")));
        var result = JsonNode.Parse(response.Body)!["result"]!.AsObject();
        var structured = result["structuredContent"]!.AsObject();

        Assert.Equal(1, tool.Applied);
        Assert.False((bool)result["isError"]!);
        Assert.Equal(McpDispatcher.WrittenAccessEnded, (string?)structured["status"]);
        Assert.Equal(new[] { "notes" }, structured["changed_fields"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("rev-0000000000000002", (string?)structured["version"]);
        Assert.Equal(new[] { "status", "message", "changed_fields", "version" }, structured.Select(p => p.Key));
        Assert.Equal(McpDispatcher.WrittenAccessEnded, rig.Activity.Entries().Single().Result);
        Assert.NotNull(rig.Presence.ChangedSince(new AgentItemRef(AgentItemKinds.Risk, 1), DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task TestWithoutAnApprovalQueueNothingIsWrittenAndNothingIsClaimedDeclined()
    {
        var (rig, tool, prompter, _, key) = Confirming((_, _) => Task.FromResult(true));
        await rig.Approvals.StopAsync();

        var result = await EditAsync(rig, key);

        Assert.Equal(AgentToolErrors.NotConfirmed, (string?)result["status"]);
        Assert.Equal(0, tool.Applied);
        Assert.Empty(prompter.WritePrompts);
    }

    [Fact]
    public async Task TestALeaseWithoutConfirmationWritesWithoutADialog()
    {
        var prompter = new ScriptedPrompter { OnWrite = (_, _) => Task.FromResult(false) };
        var tool = new StubEditTool();
        var rig = new AgentRig(prompter: prompter, tools: new IAgentTool[] { tool });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite, confirmEachWrite: false);

        var result = await EditAsync(rig, key);

        Assert.Equal("ok", (string?)result["status"]);
        Assert.Equal(1, tool.Applied);
        Assert.Empty(prompter.WritePrompts);
    }

    [Fact]
    public void TestConfirmEachWriteIsTheDefaultOfAReadWriteGrantAndMeaninglessUnderARead()
    {
        var rig = new AgentRig();
        var (writer, _) = rig.RegisterApproved("Writer");
        var (reader, _) = rig.RegisterApproved("Reader");

        var request = rig.Leases.Request(writer, 1, AgentAccessMode.ReadWrite, "edit").Request!;
        rig.Leases.Decide(request.RequestId, AccessDecision.Grant(1, AgentAccessMode.ReadWrite));
        var readRequest = rig.Leases.Request(reader, 1, AgentAccessMode.Read, "read").Request!;
        rig.Leases.Decide(readRequest.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));

        Assert.True(AgentAccessChoices.DefaultConfirmEachWrite);
        Assert.True(rig.Leases.GetLease(writer.Id)!.ConfirmEachWrite);
        Assert.False(rig.Leases.GetLease(reader.Id)!.ConfirmEachWrite);
    }

    [Fact]
    public async Task TestTheLeaseTellsTheAgentWhetherEachWriteIsConfirmedAndWhichEditToolsItHas()
    {
        var (rig, _, _, _, key) = Confirming((_, _) => Task.FromResult(true));

        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(2, McpDispatcher.GetStatusTool,
            $"{{\"agent_key\":\"{key}\"}}")));
        var lease = JsonNode.Parse(response.Body)!["result"]!["structuredContent"]!["lease"]!;

        Assert.True((bool)lease["confirm_each_write"]!);
        Assert.Equal(new[] { "edit_item" }, lease["edit_tools"]!.AsArray().Select(n => (string)n!));
    }
}
