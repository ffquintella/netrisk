using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T348/T349 (S55 "Access leases"): 10-minute blocks capped at six, the user may only shorten or downgrade, and a
/// lease ends on expiry, sign-out, lock, exit, revoke or by the user; pause refuses calls while the clock runs; a read
/// lease refuses edit tools; a key without a lease reaches only request_access and get_status.
/// </summary>
[TestSubject(typeof(AgentLeaseManager))]
public class AgentLeaseTest
{
    [Fact]
    public void TestBlocksAreTenMinutesAndCappedAtSix()
    {
        var rig = new AgentRig(leases: new AgentLeaseOptions { MaxPending = 10 });
        var (agent, _) = rig.RegisterApproved();

        Assert.Equal(AccessRequestOutcomeKind.Invalid, rig.Leases.Request(agent, 0, AgentAccessMode.Read, "r").Kind);
        Assert.Equal(AccessRequestOutcomeKind.Invalid, rig.Leases.Request(agent, 7, AgentAccessMode.Read, "r").Kind);

        var lease = rig.Grant(agent, 6, AgentAccessMode.Read);

        Assert.Equal(60, lease.Minutes);
        Assert.Equal(rig.Time.GetUtcNow().AddMinutes(60), lease.ExpiresAt);
    }

    [Fact]
    public void TestTheConfigurableCapNeverExceedsSix()
    {
        Assert.Equal(6, new AgentLeaseOptions { MaxBlocks = 10 }.MaxBlocks);
        Assert.Equal(1, new AgentLeaseOptions { MaxBlocks = 0 }.MaxBlocks);

        var rig = new AgentRig(leases: new AgentLeaseOptions { MaxBlocks = 3 });
        var (agent, _) = rig.RegisterApproved();

        Assert.Equal(AccessRequestOutcomeKind.Invalid, rig.Leases.Request(agent, 4, AgentAccessMode.Read, "r").Kind);
        Assert.Equal(AccessRequestOutcomeKind.Created, rig.Leases.Request(agent, 3, AgentAccessMode.Read, "r").Kind);
    }

    [Fact]
    public void TestLeaseEndsAtExpiry()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        var ended = new List<LeaseEndReason>();
        rig.Leases.LeaseEnded += (_, reason) => ended.Add(reason);
        rig.Grant(agent, 1, AgentAccessMode.Read);

        rig.Time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        rig.Leases.Sweep();
        Assert.Equal(LeaseCheck.Allowed, rig.Leases.Authorize(agent.Id, requiresEdit: false));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Leases.Sweep();

        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(agent.Id, requiresEdit: false));
        Assert.Equal(new[] { LeaseEndReason.Expired }, ended);
    }

    [Fact]
    public void TestTheUserMayShortenButNeverLengthen()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        var request = rig.Leases.Request(agent, 3, AgentAccessMode.Read, "r").Request!;

        Assert.False(rig.Leases.Decide(request.RequestId, AccessDecision.Grant(4, AgentAccessMode.Read)));
        Assert.False(rig.Leases.Decide(request.RequestId, AccessDecision.Grant(0, AgentAccessMode.Read)));
        Assert.Equal(AccessRequestStatus.Pending, request.Status);

        Assert.True(rig.Leases.Decide(request.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read)));
        Assert.Equal(10, rig.Leases.GetLease(agent.Id)!.Minutes);
    }

    [Fact]
    public void TestTheUserMayDowngradeToReadOnlyButNeverUpgrade()
    {
        var rig = new AgentRig();
        var (reader, _) = rig.RegisterApproved("Reader");
        var readRequest = rig.Leases.Request(reader, 1, AgentAccessMode.Read, "r").Request!;
        Assert.False(rig.Leases.Decide(readRequest.RequestId, AccessDecision.Grant(1, AgentAccessMode.ReadWrite)));

        var (editor, _) = rig.RegisterApproved("Editor");
        var lease = rig.Grant(editor, 2, AgentAccessMode.ReadWrite, grantMode: AgentAccessMode.Read);

        Assert.Equal(AgentAccessMode.Read, lease.Mode);
    }

    [Fact]
    public void TestAReadLeaseRefusesEditTools()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);

        Assert.Equal(LeaseCheck.Allowed, rig.Leases.Authorize(agent.Id, requiresEdit: false));
        Assert.Equal(LeaseCheck.ReadOnly, rig.Leases.Authorize(agent.Id, requiresEdit: true));

        var (editor, _) = rig.RegisterApproved("Editor");
        rig.Grant(editor, 1, AgentAccessMode.ReadWrite);
        Assert.Equal(LeaseCheck.Allowed, rig.Leases.Authorize(editor.Id, requiresEdit: true));
    }

    [Theory]
    [InlineData(LeaseEndReason.Logout)]
    [InlineData(LeaseEndReason.Lock)]
    [InlineData(LeaseEndReason.Exit)]
    [InlineData(LeaseEndReason.ServerStopped)]
    public void TestEveryLeaseAndRequestEndsOnSessionEvents(LeaseEndReason reason)
    {
        var rig = new AgentRig();
        var (first, _) = rig.RegisterApproved("First");
        var (second, _) = rig.RegisterApproved("Second");
        rig.Grant(first, 1, AgentAccessMode.ReadWrite);
        var waiting = rig.Leases.Request(second, 1, AgentAccessMode.Read, "r").Request!;
        var ended = new List<LeaseEndReason>();
        rig.Leases.LeaseEnded += (_, r) => ended.Add(r);

        rig.Leases.EndAll(reason);

        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(first.Id, requiresEdit: false));
        Assert.Equal(new[] { reason }, ended);
        Assert.Equal(AccessRequestStatus.Expired, waiting.Status);
        Assert.False(rig.Leases.Decide(waiting.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read)));
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.LeaseEnded && e.Detail == reason.ToString());
    }

    [Fact]
    public async Task TestSignOutThroughTheRuntimeEndsLeasesAndStopsAuthentication()
    {
        var audit = new RecordingAuditSink();
        var time = new ManualTimeProvider();
        await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), new ScriptedPrompter(),
            audit, time);
        runtime.Registry.SetUser(AgentRig.UserId);
        var pending = runtime.Registry.Begin("Agent", null, null).Pending!;
        runtime.Registry.Decide(pending.PendingId, approve: true);
        var issued = await runtime.Registry.WaitAsync(pending.PendingId, TimeSpan.Zero, CancellationToken.None);
        var request = runtime.Leases.Request(issued.Registration!, 1, AgentAccessMode.Read, "r").Request!;
        runtime.Leases.Decide(request.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));

        await runtime.SignOutAsync(LeaseEndReason.Logout);

        Assert.Equal(LeaseCheck.NoLease, runtime.Leases.Authorize(issued.Registration!.Id, requiresEdit: false));
        Assert.Null(runtime.Registry.Authenticate(issued.Key!.Reveal()));
    }

    [Fact]
    public void TestPauseRefusesCallsWhileTheClockKeepsRunning()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        var lease = rig.Grant(agent, 1, AgentAccessMode.Read);

        Assert.True(rig.Leases.Pause(agent.Id));
        Assert.Equal(LeaseCheck.Paused, rig.Leases.Authorize(agent.Id, requiresEdit: false));

        rig.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(rig.Leases.Resume(agent.Id));
        Assert.Equal(LeaseCheck.Allowed, rig.Leases.Authorize(agent.Id, requiresEdit: false));
        Assert.Equal(lease.ExpiresAt, rig.Leases.GetLease(agent.Id)!.ExpiresAt);

        rig.Leases.Pause(agent.Id);
        rig.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(agent.Id, requiresEdit: false));
    }

    [Fact]
    public async Task TestRevokingTheAgentEndsItsLeaseAndRequestsAtOnce()
    {
        var time = new ManualTimeProvider();
        await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), new ScriptedPrompter(),
            new RecordingAuditSink(), time);
        runtime.Registry.SetUser(AgentRig.UserId);
        var pending = runtime.Registry.Begin("Agent", null, null).Pending!;
        runtime.Registry.Decide(pending.PendingId, approve: true);
        var agent = (await runtime.Registry.WaitAsync(pending.PendingId, TimeSpan.Zero, CancellationToken.None))
            .Registration!;
        var request = runtime.Leases.Request(agent, 1, AgentAccessMode.Read, "r").Request!;
        runtime.Leases.Decide(request.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));
        var next = runtime.Leases.Request(agent, 2, AgentAccessMode.Read, "more").Request!;
        var ended = new List<LeaseEndReason>();
        runtime.Leases.LeaseEnded += (_, reason) => ended.Add(reason);

        runtime.Registry.Revoke(agent.Id);

        Assert.Equal(LeaseCheck.NoLease, runtime.Leases.Authorize(agent.Id, requiresEdit: false));
        Assert.Equal(new[] { LeaseEndReason.Revoked }, ended);
        Assert.Equal(AccessRequestStatus.Expired, next.Status);
    }

    /// <summary>
    /// Review finding 5: if the user grants a waiting request at the instant the agent is revoked, no lease may
    /// survive on the revoked agent — its requests are cancelled before its lease ends.
    /// </summary>
    [Fact]
    public async Task TestAGrantDuringRevocationLeavesNoLease()
    {
        await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), new ScriptedPrompter(),
            new RecordingAuditSink(), new ManualTimeProvider());
        runtime.Registry.SetUser(AgentRig.UserId);
        var (agent, _) = await RegisterAsync(runtime);
        var first = runtime.Leases.Request(agent, 1, AgentAccessMode.Read, "r").Request!;
        runtime.Leases.Decide(first.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));
        var next = runtime.Leases.Request(agent, 2, AgentAccessMode.Read, "more").Request!;
        runtime.Leases.LeaseEnded += (_, _) =>
            runtime.Leases.Decide(next.RequestId, AccessDecision.Grant(2, AgentAccessMode.Read));

        runtime.Registry.Revoke(agent.Id);

        Assert.Empty(runtime.Leases.ActiveLeases());
    }

    /// <summary>
    /// Review finding 3: a request_access that reaches the server while it stops must not leave a lease behind —
    /// the listener and the approval queue stop before leases and requests are cleared.
    /// </summary>
    [Fact]
    public async Task TestNoGrantCanLandWhileTheServerStops()
    {
        var prompter = new ScriptedPrompter
        {
            OnAccess = _ => Task.FromResult(AccessDecision.Grant(1, AgentAccessMode.Read))
        };
        var directory = Path.Combine(Path.GetTempPath(), "nr-agent-" + Guid.NewGuid().ToString("N"));
        var discovery = Path.Combine(directory, AgentDiscoveryFile.FileName);
        try
        {
            var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), prompter,
                new RecordingAuditSink(), TimeProvider.System);
            await runtime.StartAsync(AgentRig.UserId, discovery);
            var info = AgentDiscoveryFile.TryRead(discovery, out _)!;
            var (agent, key) = await RegisterAsync(runtime);
            var first = runtime.Leases.Request(agent, 1, AgentAccessMode.Read, "r").Request!;
            runtime.Leases.Decide(first.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));
            var injected = 0;
            runtime.Leases.LeaseEnded += (_, _) =>
            {
                if (Interlocked.Exchange(ref injected, 1) == 1) return;
                SendBlocking(info, AgentRig.ToolCall(1, "request_access",
                    $"{{\"agent_key\":\"{key}\",\"reason\":\"arrives while stopping\"}}"));
            };

            await runtime.StopAsync(LeaseEndReason.Logout);
            await Task.Delay(200);

            Assert.Equal(1, injected);
            Assert.Empty(runtime.Leases.ActiveLeases());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Review finding 3: starting the server clears every lease, waiting request and pending registration left from
    /// an earlier session — even for the same user, whom the client sets before it starts the server.
    /// </summary>
    [Fact]
    public async Task TestStartingTheServerClearsWhatAnEarlierSessionLeft()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nr-agent-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(),
                new ScriptedPrompter(), new RecordingAuditSink(), new ManualTimeProvider());
            runtime.Registry.SetUser(AgentRig.UserId);
            var (agent, _) = await RegisterAsync(runtime);
            var (other, _) = await RegisterAsync(runtime, "Other");
            var granted = runtime.Leases.Request(agent, 1, AgentAccessMode.Read, "r").Request!;
            runtime.Leases.Decide(granted.RequestId, AccessDecision.Grant(1, AgentAccessMode.Read));
            var waiting = runtime.Leases.Request(other, 1, AgentAccessMode.Read, "r").Request!;
            var registration = runtime.Registry.Begin("Late", null, null).Pending!;

            await runtime.StartAsync(AgentRig.UserId, Path.Combine(directory, AgentDiscoveryFile.FileName));

            Assert.Empty(runtime.Leases.ActiveLeases());
            Assert.Equal(AccessRequestStatus.Expired, waiting.Status);
            Assert.Equal(PendingRegistrationStatus.Expired, registration.Status);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TestTheUserCanEndALeaseAtAnyTime()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        rig.Grant(agent, 6, AgentAccessMode.ReadWrite);

        Assert.True(rig.Leases.End(agent.Id, LeaseEndReason.EndedByUser));

        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(agent.Id, requiresEdit: false));
        Assert.False(rig.Leases.End(agent.Id, LeaseEndReason.EndedByUser));
    }

    [Fact]
    public void TestASecondRequestChangesNothingUntilApprovedAndThenReplaces()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();
        var first = rig.Grant(agent, 1, AgentAccessMode.Read);

        var second = rig.Leases.Request(agent, 2, AgentAccessMode.ReadWrite, "need to edit").Request!;
        // Less than the 5-minute pending timeout, so the second request is still waiting for the user.
        rig.Time.Advance(TimeSpan.FromMinutes(3));

        Assert.Same(first, rig.Leases.GetLease(agent.Id));
        Assert.Equal(LeaseCheck.ReadOnly, rig.Leases.Authorize(agent.Id, requiresEdit: true));

        Assert.True(rig.Leases.Decide(second.RequestId, AccessDecision.Grant(2, AgentAccessMode.ReadWrite)));
        var replaced = rig.Leases.GetLease(agent.Id)!;
        Assert.NotSame(first, replaced);
        Assert.Equal(rig.Time.GetUtcNow().AddMinutes(20), replaced.ExpiresAt);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.LeaseEnded
                                               && e.Detail == nameof(LeaseEndReason.Replaced));
    }

    [Fact]
    public void TestARepeatedRequestCollapsesOntoTheOneWaiting()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();

        var first = rig.Leases.Request(agent, 1, AgentAccessMode.Read, "r");
        var again = rig.Leases.Request(agent, 6, AgentAccessMode.ReadWrite, "more");

        Assert.Equal(AccessRequestOutcomeKind.Created, first.Kind);
        Assert.Equal(AccessRequestOutcomeKind.Collapsed, again.Kind);
        Assert.Same(first.Request, again.Request);
    }

    [Fact]
    public void TestRequestsBeyondTheQueueAreRefused()
    {
        var rig = new AgentRig(leases: new AgentLeaseOptions { MaxPending = 1 });
        var (first, _) = rig.RegisterApproved("First");
        var (second, _) = rig.RegisterApproved("Second");

        rig.Leases.Request(first, 1, AgentAccessMode.Read, "r");

        Assert.Equal(AccessRequestOutcomeKind.Busy, rig.Leases.Request(second, 1, AgentAccessMode.Read, "r").Kind);
    }

    [Fact]
    public void TestDeniedAndUndecidedRequestsGrantNothing()
    {
        var rig = new AgentRig();
        var (denied, _) = rig.RegisterApproved("Denied");
        var (ignored, _) = rig.RegisterApproved("Ignored");
        var deniedRequest = rig.Leases.Request(denied, 1, AgentAccessMode.Read, "r").Request!;
        var ignoredRequest = rig.Leases.Request(ignored, 1, AgentAccessMode.Read, "r").Request!;

        rig.Leases.Decide(deniedRequest.RequestId, AccessDecision.Deny);
        rig.Time.Advance(new AgentLeaseOptions().PendingTimeout);
        rig.Leases.Sweep();

        Assert.Equal(AccessRequestStatus.Denied, deniedRequest.Status);
        Assert.Equal(AccessRequestStatus.Expired, ignoredRequest.Status);
        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(denied.Id, requiresEdit: false));
        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(ignored.Id, requiresEdit: false));
    }

    [Fact]
    public void TestRequestReasonIsRequiredAndBounded()
    {
        var rig = new AgentRig();
        var (agent, _) = rig.RegisterApproved();

        Assert.Equal(AccessRequestOutcomeKind.Invalid, rig.Leases.Request(agent, 1, AgentAccessMode.Read, null).Kind);
        Assert.Equal(AccessRequestOutcomeKind.Invalid, rig.Leases.Request(agent, 1, AgentAccessMode.Read,
            new string('r', AgentText.MaxReasonLength + 1)).Kind);
    }

    [Fact]
    public void TestTheAccessDialogNeverOffersMoreThanWasRequested()
    {
        Assert.Equal(new[] { 1, 2, 3 }, AgentAccessChoices.Blocks(3));
        Assert.Equal(Enumerable.Range(1, 6), AgentAccessChoices.Blocks(60));
        Assert.Equal(new[] { AgentAccessMode.Read }, AgentAccessChoices.Modes(AgentAccessMode.Read));
        Assert.Equal(new[] { AgentAccessMode.Read, AgentAccessMode.ReadWrite },
            AgentAccessChoices.Modes(AgentAccessMode.ReadWrite));
        Assert.Equal(1, AgentAccessChoices.DefaultBlocks);
        Assert.Equal(AgentAccessMode.Read, AgentAccessChoices.DefaultMode);
    }

    [Fact]
    public async Task TestAKeyWithoutALeaseReachesOnlyRequestAccessAndGetStatus()
    {
        var rig = new AgentRig();
        var (agent, key) = rig.RegisterApproved();

        var list = await rig.SendAsync(AgentRig.Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
        var tools = JsonNode.Parse(list.Body)!["result"]!["tools"]!.AsArray()
            .Select(t => (string)t!["name"]!).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "get_status", "register_agent", "request_access" }, tools);

        var status = await CallAsync(rig, "get_status", $"{{\"agent_key\":\"{key}\"}}");
        Assert.Equal("ok", (string?)status["status"]);
        Assert.Null(status["lease"]);

        var unknown = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(3, "list_risks", "{}")));
        Assert.Equal(-32602, (int)JsonNode.Parse(unknown.Body)!["error"]!["code"]!);

        Assert.Equal(LeaseCheck.NoLease, rig.Leases.Authorize(agent.Id, requiresEdit: false));
    }

    // Review finding 4: one gate in the dispatcher decides whether a business tool runs. These go through the dispatcher
    // with stub tools, so deleting the gate (or the Authorize call in it) fails them.

    [Fact]
    public async Task TestTheLeaseGateRunsNoBusinessToolWithoutAKeyAndALease()
    {
        var read = new StubTool("read_item", requiresEdit: false);
        var rig = new AgentRig(tools: new IAgentTool[] { read });
        var (_, key) = rig.RegisterApproved();

        var noKey = await CallAsync(rig, "read_item", "{\"id\":1}");
        var forged = await CallAsync(rig, "read_item", "{\"id\":1,\"agent_key\":\"nrak_forged\"}");
        var noLease = await CallAsync(rig, "read_item", $"{{\"id\":1,\"agent_key\":\"{key}\"}}");

        Assert.Equal("unauthorized", (string?)noKey["status"]);
        Assert.Equal("unauthorized", (string?)forged["status"]);
        Assert.Equal("access_expired", (string?)noLease["status"]);
        Assert.Empty(read.Invocations);
    }

    [Fact]
    public async Task TestTheLeaseGateRunsReadToolsAndRefusesEditToolsUnderAReadLease()
    {
        var read = new StubTool("read_item", requiresEdit: false);
        var edit = new StubTool("edit_item", requiresEdit: true);
        var rig = new AgentRig(tools: new IAgentTool[] { read, edit });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);

        var readResult = await CallAsync(rig, "read_item", $"{{\"id\":1,\"agent_key\":\"{key}\"}}");
        var editResult = await CallAsync(rig, "edit_item", $"{{\"id\":1,\"agent_key\":\"{key}\"}}");

        Assert.Equal("ok", (string?)readResult["status"]);
        Assert.Single(read.Invocations);
        Assert.Equal("read_only", (string?)editResult["status"]);
        Assert.Empty(edit.Invocations);
    }

    [Fact]
    public async Task TestTheLeaseGateRunsEditToolsUnderAnEditLease()
    {
        var read = new StubTool("read_item", requiresEdit: false);
        var edit = new StubTool("edit_item", requiresEdit: true);
        var rig = new AgentRig(tools: new IAgentTool[] { read, edit });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite);

        Assert.Equal("ok", (string?)(await CallAsync(rig, "read_item", $"{{\"agent_key\":\"{key}\"}}"))["status"]);
        Assert.Equal("ok", (string?)(await CallAsync(rig, "edit_item", $"{{\"agent_key\":\"{key}\"}}"))["status"]);
        Assert.Single(read.Invocations);
        Assert.Single(edit.Invocations);
    }

    [Fact]
    public async Task TestTheLeaseGateRunsNothingWhilePausedOrAfterExpiry()
    {
        var read = new StubTool("read_item", requiresEdit: false);
        var rig = new AgentRig(tools: new IAgentTool[] { read });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.ReadWrite);
        var arguments = $"{{\"agent_key\":\"{key}\"}}";

        rig.Leases.Pause(agent.Id);
        var paused = await CallAsync(rig, "read_item", arguments);
        rig.Leases.Resume(agent.Id);
        rig.Time.Advance(TimeSpan.FromMinutes(AgentLeaseOptions.BlockMinutes));
        var expired = await CallAsync(rig, "read_item", arguments);

        Assert.Equal("paused", (string?)paused["status"]);
        Assert.Equal("access_expired", (string?)expired["status"]);
        Assert.Empty(read.Invocations);
    }

    [Fact]
    public async Task TestABusinessToolNeverReceivesTheAgentKey()
    {
        var read = new StubTool("read_item", requiresEdit: false);
        var rig = new AgentRig(tools: new IAgentTool[] { read });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);

        await CallAsync(rig, "read_item", $"{{\"id\":42,\"agent_key\":\"{key}\"}}");

        var (call, arguments) = Assert.Single(read.Invocations);
        Assert.False(arguments.ContainsKey("agent_key"));
        Assert.Equal(42, (int)arguments["id"]!);
        Assert.Equal(agent.Id, call.Agent.Id);
        Assert.Equal(AgentAccessMode.Read, call.Lease.Mode);
    }

    [Fact]
    public void TestABusinessToolCannotTakeABootstrapName()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentRig(tools: new IAgentTool[] { new StubTool(McpDispatcher.GetStatusTool, requiresEdit: false) }));
    }

    [Fact]
    public async Task TestRequestAccessOverMcpReturnsTheLeaseTheUserGranted()
    {
        var prompter = new ScriptedPrompter
        {
            OnAccess = prompt => Task.FromResult(AccessDecision.Grant(2, AgentAccessMode.Read))
        };
        var rig = new AgentRig(prompter: prompter, decisionWait: TimeSpan.FromSeconds(10));
        rig.Approvals.Start();
        var (agent, key) = rig.RegisterApproved();

        var granted = await CallAsync(rig, "request_access",
            "{\"blocks\":3,\"mode\":\"read_write\",\"reason\":\"Update the incident timeline\"}",
            authorization: "Bearer " + key);

        Assert.Equal("granted", (string?)granted["status"]);
        Assert.Equal("read", (string?)granted["mode"]);
        Assert.Equal(20, (int)granted["minutes"]!);
        Assert.Equal(LeaseCheck.ReadOnly, rig.Leases.Authorize(agent.Id, requiresEdit: true));
        await rig.Approvals.StopAsync();
    }

    [Fact]
    public async Task TestAPendingRequestIsPolledWithItsRequestId()
    {
        var rig = new AgentRig();
        var (agent, key) = rig.RegisterApproved();

        var pending = await CallAsync(rig, "request_access", $"{{\"agent_key\":\"{key}\",\"reason\":\"Read risks\"}}");
        Assert.Equal("pending", (string?)pending["status"]);
        var requestId = (string)pending["request_id"]!;

        rig.Leases.Decide(requestId, AccessDecision.Grant(1, AgentAccessMode.Read));
        var granted = await CallAsync(rig, "request_access", $"{{\"agent_key\":\"{key}\",\"request_id\":\"{requestId}\"}}");

        Assert.Equal("granted", (string?)granted["status"]);
        Assert.Equal(LeaseCheck.Allowed, rig.Leases.Authorize(agent.Id, requiresEdit: false));
    }

    [Fact]
    public async Task TestRequestAccessRefusesUnknownMissingAndConflictingKeys()
    {
        var rig = new AgentRig();
        var (_, key) = rig.RegisterApproved();
        var (_, otherKey) = rig.RegisterApproved("Other");

        var missing = await CallAsync(rig, "request_access", "{\"reason\":\"r\"}");
        var unknown = await CallAsync(rig, "request_access", "{\"agent_key\":\"nrak_forged\",\"reason\":\"r\"}");
        var conflicting = await CallAsync(rig, "request_access", $"{{\"agent_key\":\"{otherKey}\",\"reason\":\"r\"}}",
            authorization: "Bearer " + key);
        var badBlocks = await CallAsync(rig, "request_access", $"{{\"agent_key\":\"{key}\",\"blocks\":\"six\",\"reason\":\"r\"}}");

        Assert.Equal("unauthorized", (string?)missing["status"]);
        Assert.Equal("unauthorized", (string?)unknown["status"]);
        Assert.Equal("unauthorized", (string?)conflicting["status"]);
        Assert.Equal("invalid", (string?)badBlocks["status"]);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.AuthenticationFailed);
    }

    private static async Task<(AgentRegistration Registration, string Key)> RegisterAsync(AgentCoworkRuntime runtime,
        string name = "Agent")
    {
        var pending = runtime.Registry.Begin(name, null, null).Pending!;
        runtime.Registry.Decide(pending.PendingId, approve: true);
        var issued = await runtime.Registry.WaitAsync(pending.PendingId, TimeSpan.Zero, CancellationToken.None);
        return (issued.Registration!, issued.Key!.Reveal());
    }

    /// <summary>A plain blocking HTTP POST to a running listener; null when nothing is listening any more.</summary>
    private static string? SendBlocking(AgentDiscoveryInfo info, string body)
    {
        var port = new Uri(info.Url).Port;
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            stream.ReadTimeout = 15000;
            stream.Write(Encoding.UTF8.GetBytes(AgentRig.Post(body, host: "127.0.0.1:" + port, token: info.LaunchToken)));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return null;
        }
    }

    private static async Task<JsonObject> CallAsync(AgentRig rig, string tool, string arguments,
        string? authorization = null)
    {
        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(9, tool, arguments),
            authorization: authorization));
        Assert.Equal(200, response.Status);
        return JsonNode.Parse(response.Body)!["result"]!["structuredContent"]!.AsObject();
    }
}
