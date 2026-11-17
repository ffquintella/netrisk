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
/// T355 and T356 (S55 "Co-work experience"), the parts that decide something: the activity list records every leased
/// call with no data, the lease clock warns in the last minute, "Grant another 10 min" is a new request that only the
/// user's approval turns into time, presence marks items and an edit form opened before an agent's write must not save.
/// </summary>
[TestSubject(typeof(AgentPresence))]
public class AgentCoworkExperienceTest
{
    // ---- T355 activity -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestEveryLeasedCallIsListedWithToolItemKindAndResultButNoData()
    {
        var rig = AgentReadRig.OverAllFixtures();
        await rig.CallAsync("get_finding", new JsonObject { ["finding_id"] = 1 });
        await rig.CallAsync("update_risk", new JsonObject { ["risk_id"] = 1, ["version"] = "rev-0000000000000000", ["notes"] = "x" });
        rig.Rig.Leases.Pause(rig.Agent.Id);
        await rig.CallAsync("search_risks");

        var entries = rig.Rig.Activity.Entries();
        Assert.Equal(new[] { "get_finding", "update_risk", "search_risks" }, entries.Select(e => e.Tool));
        Assert.Equal(new[] { "ok", AgentToolErrors.Stale, "paused" }, entries.Select(e => e.Result));
        Assert.Equal(new[] { false, true, false }, entries.Select(e => e.IsEdit));
        Assert.Equal(new AgentItemRef(AgentItemKinds.Finding, 1), entries[0].Item);
        Assert.Null(entries[2].Item);
        Assert.All(entries, e => Assert.Equal(rig.Agent.Id, e.AgentId));
    }

    [Fact]
    public void TestTheActivityListIsBounded()
    {
        var log = new AgentActivityLog();
        for (var i = 0; i < AgentActivityLog.Capacity + 25; i++)
            log.Record(new AgentActivityEntry(DateTimeOffset.UnixEpoch.AddSeconds(i), "a", "A", "t" + i, false, null, "ok"));

        Assert.Equal(AgentActivityLog.Capacity, log.Entries().Count);
        Assert.Equal("t25", log.Entries()[0].Tool);
    }

    [Fact]
    public void TestTheLeaseClockWarnsOnlyInTheLastMinute()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        var lease = rig.Grant(agent, 1, AgentAccessMode.Read);
        var start = rig.Time.GetUtcNow();

        Assert.False(AgentLeaseClock.IsExpiring(lease, start));
        Assert.Equal("10:00", AgentLeaseClock.Remaining(lease, start));
        Assert.False(AgentLeaseClock.IsExpiring(lease, start.AddSeconds(538)));
        Assert.True(AgentLeaseClock.IsExpiring(lease, start.AddSeconds(540)));
        Assert.Equal("0:01", AgentLeaseClock.Remaining(lease, start.AddSeconds(599)));
        Assert.False(AgentLeaseClock.IsExpiring(lease, start.AddMinutes(10)));
        Assert.Equal("0:00", AgentLeaseClock.Remaining(lease, start.AddMinutes(11)));
    }

    // ---- T355 "Grant another 10 min": a new request, through the approval dialog, never a silent renewal ----------

    private static (AgentCoworkRuntime Runtime, ScriptedPrompter Prompter, AgentRegistration Agent, ManualTimeProvider Time)
        RuntimeWithLease(AgentAccessMode mode)
    {
        var prompter = new ScriptedPrompter();
        var time = new ManualTimeProvider();
        var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), prompter, new RecordingAuditSink(), time);
        runtime.Registry.SetUser(AgentRig.UserId);
        var outcome = runtime.Registry.Begin("Claude Code", "Helps", new AgentClientInfo("claude-code", "2"));
        runtime.Registry.Decide(outcome.Pending!.PendingId, true);
        var agent = runtime.Registry.WaitAsync(outcome.Pending.PendingId, TimeSpan.Zero, CancellationToken.None)
            .GetAwaiter().GetResult().Registration!;
        var request = runtime.Leases.Request(agent, 1, mode, "work").Request!;
        runtime.Leases.Decide(request.RequestId, AccessDecision.Grant(1, mode, confirmEachWrite: true));
        runtime.Approvals.Start();
        return (runtime, prompter, agent, time);
    }

    [Fact]
    public async Task TestMoreTimeIsANewUserInitiatedRequestShownInTheAccessDialog()
    {
        var (runtime, prompter, agent, time) = RuntimeWithLease(AgentAccessMode.ReadWrite);
        var shown = new TaskCompletionSource<AccessPrompt>();
        var decide = new TaskCompletionSource<AccessDecision>();
        prompter.OnAccess = prompt =>
        {
            shown.TrySetResult(prompt);
            return decide.Task;
        };
        time.Advance(TimeSpan.FromSeconds(550));
        var old = runtime.Leases.GetLease(agent.Id)!;

        Assert.True(runtime.RequestMoreTime(agent.Id));
        var prompt = await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(prompt.UserInitiated);
        Assert.Equal(1, prompt.RequestedBlocks);
        Assert.Equal(AgentAccessMode.ReadWrite, prompt.RequestedMode);
        // Nothing is extended while the dialog waits: the old lease runs out on its own clock.
        Assert.Same(old, runtime.Leases.GetLease(agent.Id));
        Assert.Equal(old.ExpiresAt, runtime.Leases.GetLease(agent.Id)!.ExpiresAt);

        decide.SetResult(AccessDecision.Grant(1, AgentAccessMode.ReadWrite));
        await WaitUntil(() => !ReferenceEquals(runtime.Leases.GetLease(agent.Id), old));
        var renewed = runtime.Leases.GetLease(agent.Id)!;
        Assert.Equal(time.GetUtcNow().AddMinutes(10), renewed.ExpiresAt);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task TestMoreTimeTheUserDeniesLeavesTheLeaseToExpire()
    {
        var (runtime, prompter, agent, time) = RuntimeWithLease(AgentAccessMode.Read);
        var answered = new TaskCompletionSource();
        prompter.OnAccess = _ =>
        {
            answered.TrySetResult();
            return Task.FromResult(AccessDecision.Deny);
        };
        var old = runtime.Leases.GetLease(agent.Id)!;

        Assert.True(runtime.RequestMoreTime(agent.Id));
        await answered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntil(() => runtime.Leases.GetPendingFor(agent.Id) is null);

        Assert.Same(old, runtime.Leases.GetLease(agent.Id));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(runtime.Leases.GetLease(agent.Id));
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task TestMoreTimeNeedsALeaseAndARunningApprovalQueue()
    {
        var (runtime, _, agent, _) = RuntimeWithLease(AgentAccessMode.Read);
        runtime.Leases.End(agent.Id, LeaseEndReason.EndedByUser);
        Assert.False(runtime.RequestMoreTime(agent.Id));
        Assert.Equal(AccessRequestOutcomeKind.Invalid, runtime.Leases.RequestExtension(agent).Kind);

        await runtime.Approvals.StopAsync();
        Assert.False(runtime.RequestMoreTime(agent.Id));
        await runtime.DisposeAsync();
    }

    // ---- T356 presence, refresh, conflict ------------------------------------------------------------------------

    [Fact]
    public void TestPresenceMarksAnItemForAWindowAndRemembersEditing()
    {
        var presence = new AgentPresence();
        var item = new AgentItemRef(AgentItemKinds.Risk, 4);
        var now = DateTimeOffset.UnixEpoch;

        presence.Touch(item, "Claude", editing: true, now);
        presence.Touch(item, "Claude", editing: false, now.AddSeconds(10));

        Assert.True(presence.PresenceOf(item, now.AddSeconds(20))!.Editing);
        Assert.Single(presence.Active(now.AddSeconds(20)));
        Assert.Null(presence.PresenceOf(item, now.AddSeconds(10) + AgentPresence.Window));
        Assert.Empty(presence.Active(now.AddMinutes(5)));
    }

    [Fact]
    public void TestAFormOpenedBeforeAnAgentsWriteMustNotSaveAndOneOpenedAfterMay()
    {
        var presence = new AgentPresence();
        var item = new AgentItemRef(AgentItemKinds.Incident, 9);
        var changed = (AgentItemChange?)null;
        presence.ItemChanged += c => changed = c;
        var openedBefore = DateTimeOffset.UnixEpoch;

        presence.RecordChange(item, "Claude", new[] { "cause", "notes" }, openedBefore.AddSeconds(30));

        Assert.Equal(new[] { "cause", "notes" }, presence.ChangedSince(item, openedBefore)!.Fields);
        Assert.Null(presence.ChangedSince(item, openedBefore.AddSeconds(31)));
        Assert.Null(presence.ChangedSince(new AgentItemRef(AgentItemKinds.Incident, 10), openedBefore));
        Assert.Equal(item, changed!.Item);
    }

    [Fact]
    public async Task TestTheUsersSaveMakesTheAgentsNextWriteStaleSoNeitherOverwritesTheOther()
    {
        var rig = AgentReadRig.OverAllFixtures();
        var arguments = rig.Edits.ArgumentsFor("update_incident");
        rig.Edits.Incident.Cause = "The user's own cause"; // the user saved from the GUI after the agent read
        rig.Edits.Incident.LastUpdate = rig.Edits.Incident.LastUpdate.AddMinutes(2);

        var answer = await rig.CallAsync("update_incident", arguments);

        Assert.Equal(AgentToolErrors.Stale, answer.Status);
        Assert.Null(rig.Rig.Presence.ChangedSince(new AgentItemRef(AgentItemKinds.Incident, 1), DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task TestTheRuntimeForgetsActivityAndPresenceWhenItStops()
    {
        var (runtime, _, _, _) = RuntimeWithLease(AgentAccessMode.Read);
        runtime.Activity.Record(new AgentActivityEntry(DateTimeOffset.UnixEpoch, "a", "A", "get_risk", false, null, "ok"));
        runtime.Presence.RecordChange(new AgentItemRef(AgentItemKinds.Risk, 1), "A", new[] { "notes" }, DateTimeOffset.UnixEpoch);

        await runtime.StopAsync(LeaseEndReason.Logout);

        Assert.Empty(runtime.Activity.Entries());
        Assert.Null(runtime.Presence.ChangedSince(new AgentItemRef(AgentItemKinds.Risk, 1), DateTimeOffset.MinValue));
    }

    // ---- T354 listing: an MCP client that passes its key as an argument still learns its edit tools --------------

    [Fact]
    public async Task TestASessionThatAuthenticatedOnceListsItsEditToolsAndAReadLeaseNever()
    {
        var rig = AgentReadRig.OverAllFixtures();
        var init = await rig.Rig.SendAsync(AgentRig.Post(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}"));
        var session = init.Header("Mcp-Session-Id")!;

        async Task<string[]> List()
        {
            var r = await rig.Rig.SendAsync(AgentRig.Post("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", sessionId: session));
            return JsonNode.Parse(r.Body)!["result"]!["tools"]!.AsArray().Select(t => (string)t!["name"]!).ToArray();
        }

        Assert.DoesNotContain("update_risk", await List());
        await rig.Rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(3, McpDispatcher.GetStatusTool, $"{{\"agent_key\":\"{rig.Key}\"}}"),
            sessionId: session));
        Assert.Contains("update_risk", await List());

        rig.Rig.Leases.End(rig.Agent.Id, LeaseEndReason.EndedByUser);
        rig.Rig.Grant(rig.Agent, 1, AgentAccessMode.Read);
        Assert.DoesNotContain("update_risk", await List());
        Assert.Contains("get_risk", await List());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    // ---- review M63 #4: nothing the GUI does in response carries the agent header ------------------------------------

    [Fact]
    public async Task TestHandlersOfPresenceAndActivityEventsNeverRunInTheAgentsContext()
    {
        var rig = AgentReadRig.OverAllFixtures();
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var posted = new System.Collections.Generic.List<Task>();
        void Observe(string what)
        {
            seen.Enqueue(what + ":" + (ClientServices.Http.AgentCallContext.Current ?? "none"));
            // Work a handler posts elsewhere (as the GUI posts to its UI thread) inherits the raising context.
            lock (posted) posted.Add(Task.Run(() =>
                seen.Enqueue(what + "-posted:" + (ClientServices.Http.AgentCallContext.Current ?? "none"))));
        }
        rig.Rig.Presence.Changed += () => Observe("presence");
        rig.Rig.Presence.ItemChanged += _ => Observe("item-changed");
        rig.Rig.Activity.Added += _ => Observe("activity");

        await rig.CallAsync("get_risk", new JsonObject { ["risk_id"] = 1 });
        await rig.CallAsync("update_risk");
        Task[] pending;
        lock (posted) pending = posted.ToArray();
        await Task.WhenAll(pending);

        Assert.Contains(seen, s => s.StartsWith("item-changed:", StringComparison.Ordinal));
        Assert.All(seen, s => Assert.EndsWith(":none", s));
        Assert.Null(ClientServices.Http.AgentCallContext.Current);
    }

    [Fact]
    public void TestEveryIssuedRegistrationIdIsOneTheHeaderCarries()
    {
        for (var i = 0; i < 20; i++)
        {
            var (agent, _) = new AgentRig().RegisterApproved("Agent " + i); // a fresh registry each: registration is rate-limited
            Assert.True(Model.Agents.AgentContextHeader.IsValid(agent.Id), agent.Id);
            Assert.Equal(Model.Agents.AgentContextHeader.Length, agent.Id.Length);
        }
    }
}
