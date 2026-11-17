using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace GUIClient.Agents;

public sealed class AgentCoworkOptions
{
    public McpServerOptions Server { get; init; } = new();
    public AgentRegistryOptions Registry { get; init; } = new();
    public AgentLeaseOptions Leases { get; init; } = new();
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Business tools (M62+), each reachable only through the dispatcher's lease gate.</summary>
    public IReadOnlyList<IAgentTool> Tools { get; init; } = Array.Empty<IAgentTool>();

    /// <summary>
    /// The signed-in user as the client holds it, read on every call: what <c>tools/list</c> is filtered by and what a
    /// tool's permission is checked against. Absent means nobody, so no business tool is listed or runs.
    /// </summary>
    public Func<Model.Authentication.AuthenticatedUserInfo?>? CurrentUser { get; init; }
}

/// <summary>
/// The agent co-work feature minus its windows: registry, leases, approval queue and the loopback server, with the
/// lifecycle S55 asks for — started only for a signed-in user who switched it on, stopped on sign-out, every lease
/// ended on lock or exit. The GUI adds settings, dialogs and logging around it.
/// </summary>
public sealed class AgentCoworkRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IAgentAuditSink _audit;
    private readonly TimeProvider _time;
    private readonly AgentCoworkOptions _options;
    private McpLoopbackServer? _server;
    private ITimer? _sweep;
    private string? _discoveryPath;
    private int? _runningUserId;

    public AgentCoworkRuntime(IAgentRegistrationStore store, IAgentApprovalPrompter prompter, IAgentAuditSink audit,
        TimeProvider time, AgentCoworkOptions? options = null)
    {
        _audit = audit;
        _time = time;
        _options = options ?? new AgentCoworkOptions();
        Registry = new AgentRegistry(store, time, audit, _options.Registry);
        Leases = new AgentLeaseManager(time, audit, _options.Leases);
        Approvals = new AgentApprovalCoordinator(Registry, Leases, prompter, audit, time);
        Dispatcher = new McpDispatcher(Registry, Leases, Approvals, time, _options.Server, _options.Tools,
            _options.CurrentUser, audit, Activity, Presence);

        // Revocation is immediate: the key stops authenticating in Revoke, and the lease and any request end here.
        // Requests first: a grant landing between the two would otherwise leave a lease on a revoked agent.
        Registry.Revoked += registration =>
        {
            Leases.CancelRequestsFor(registration.Id, "agent revoked");
            Leases.End(registration.Id, LeaseEndReason.Revoked);
        };
    }

    public AgentRegistry Registry { get; }
    public AgentLeaseManager Leases { get; }

    /// <summary>The live call list behind the activity panel (T355). Emptied when the server stops.</summary>
    public AgentActivityLog Activity { get; } = new();

    /// <summary>Item presence and change notices for the views (T356). Emptied when the server stops.</summary>
    public AgentPresence Presence { get; } = new();
    public AgentApprovalCoordinator Approvals { get; }
    public McpDispatcher Dispatcher { get; }

    public bool IsRunning => _server is not null;
    public int? Port => _server?.Port;

    /// <summary>Raised after the server starts or stops.</summary>
    public event Action? StateChanged;

    /// <summary>Starts (or keeps) the server for <paramref name="userId"/> and writes the discovery file.</summary>
    public async Task StartAsync(int userId, string discoveryPath, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_server is not null)
            {
                if (_runningUserId == userId) return;
                // Another user signed in while the server ran: nothing granted to the previous one survives.
                Leases.EndAll(LeaseEndReason.UserChanged);
                Registry.CancelPending(nameof(LeaseEndReason.UserChanged));
                Activity.Clear();
                Presence.Clear();
                Registry.SetUser(userId);
                _runningUserId = userId;
                return;
            }

            // A fresh start begins empty, whoever signs in: the client sets the user before starting, so "same user"
            // says nothing about whether a lease or request is left from an earlier session (review finding 3).
            Leases.EndAll(LeaseEndReason.ServerStopped);
            Registry.CancelPending("server started");
            Registry.SetUser(userId);

            var launchToken = RandomGenerator.RandomToken(32);
            Approvals.Start();
            var server = new McpLoopbackServer(
                port => new McpEndpoint(new LoopbackRequestGuard(port, launchToken), Dispatcher, _audit, _time),
                _audit, _time);
            try
            {
                server.Start();
                AgentDiscoveryFile.Write(discoveryPath, new AgentDiscoveryInfo(
                    $"http://127.0.0.1:{server.Port}{McpEndpoint.Path}", launchToken, Environment.ProcessId,
                    _time.GetUtcNow()));
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                await Approvals.StopAsync().ConfigureAwait(false);
                throw;
            }

            _server = server;
            _runningUserId = userId;
            _discoveryPath = discoveryPath;
            _sweep = _time.CreateTimer(_ => Sweep(), null, _options.SweepInterval, _options.SweepInterval);
            _audit.Record(new AgentAuditEvent(AgentAuditKind.ServerStarted, _time.GetUtcNow(), null, null,
                "listening on 127.0.0.1:" + server.Port));
        }
        finally
        {
            _lifecycle.Release();
        }
        StateChanged?.Invoke();
    }

    /// <summary>Stops the server: every lease ends with <paramref name="reason"/>, every waiting request is dropped.</summary>
    public async Task StopAsync(LeaseEndReason reason)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        McpLoopbackServer? server;
        try
        {
            server = _server;
            _server = null;
            _runningUserId = null;
            _sweep?.Dispose();
            _sweep = null;

            // Close every way in first — the listener (no new request) and the approval queue (no new decision) —
            // and only then clear: cleared first, a request in flight could still be granted (review finding 3).
            if (server is not null)
            {
                await Approvals.StopAsync().ConfigureAwait(false);
                await server.DisposeAsync().ConfigureAwait(false);
            }
            Leases.EndAll(reason);
            Registry.CancelPending(reason.ToString());
            Activity.Clear();
            Presence.Clear();

            if (_discoveryPath is not null) AgentDiscoveryFile.DeleteIfOwned(_discoveryPath, Environment.ProcessId);
            _discoveryPath = null;
            if (server is not null)
                _audit.Record(new AgentAuditEvent(AgentAuditKind.ServerStopped, _time.GetUtcNow(), null, null,
                    reason.ToString()));
        }
        finally
        {
            _lifecycle.Release();
        }
        if (server is not null) StateChanged?.Invoke();
    }

    /// <summary>Sign-out: the server closes and registrations stop authenticating until the next sign-in.</summary>
    public async Task SignOutAsync(LeaseEndReason reason)
    {
        await StopAsync(reason).ConfigureAwait(false);
        Registry.SetUser(null);
    }

    /// <summary>Session locked: every lease and waiting request ends; the server keeps listening.</summary>
    public void EndAllLeases(LeaseEndReason reason)
    {
        Leases.EndAll(reason);
    }

    /// <summary>
    /// Sign-out or exit right before the process ends: synchronous and without waiting on sockets. Leases end, the
    /// discovery file goes; the process takes the listener with it.
    /// </summary>
    public void StopNow(LeaseEndReason reason)
    {
        // Same order as StopAsync: no new request, no new decision, then clear.
        _server?.Abort();
        Approvals.Cancel();
        Leases.EndAll(reason);
        Registry.CancelPending(reason.ToString());
        if (_discoveryPath is not null) AgentDiscoveryFile.DeleteIfOwned(_discoveryPath, Environment.ProcessId);
        _discoveryPath = null;
    }

    /// <summary>
    /// T355 "Grant another 10 min": a new access request for the agent, started by the user, shown in the same approval
    /// dialog as any other. Nothing is extended until the user grants it there. False when it could not be asked.
    /// </summary>
    public bool RequestMoreTime(string agentId)
    {
        if (!Approvals.IsRunning || Registry.Get(agentId) is not { } agent) return false;
        var outcome = Leases.RequestExtension(agent);
        if (outcome.Kind == AccessRequestOutcomeKind.Created) Approvals.EnqueueAccess(outcome.Request!.RequestId);
        return outcome.Kind is AccessRequestOutcomeKind.Created or AccessRequestOutcomeKind.Collapsed;
    }

    public void Sweep()
    {
        Registry.Sweep();
        Leases.Sweep();
    }

    public async ValueTask DisposeAsync() => await StopAsync(LeaseEndReason.ServerStopped).ConfigureAwait(false);
}
