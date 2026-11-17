using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace GUIClient.Agents;

/// <summary>What a lease lets the agent do. Edit tools are simply absent under <see cref="Read"/>.</summary>
public enum AgentAccessMode { Read, ReadWrite }

/// <summary>Why a lease ended (T348). Every one of these is an audit event.</summary>
public enum LeaseEndReason { Expired, Logout, Lock, Exit, Revoked, EndedByUser, Replaced, ServerStopped, UserChanged }

/// <summary>The answer to "may this agent run a tool now?".</summary>
public enum LeaseCheck { Allowed, NoLease, Paused, ReadOnly }

/// <summary>Limits on <c>request_access</c>. Defaults are the shipped values.</summary>
public sealed class AgentLeaseOptions
{
    public const int BlockMinutes = 10;
    public const int AbsoluteMaxBlocks = 6;

    private readonly int _maxBlocks = AbsoluteMaxBlocks;

    /// <summary>The configurable cap on blocks per request: 1 to 6, never more (S55).</summary>
    public int MaxBlocks
    {
        get => _maxBlocks;
        init => _maxBlocks = Math.Clamp(value, 1, AbsoluteMaxBlocks);
    }

    /// <summary>Access requests waiting for the user at once, across all agents.</summary>
    public int MaxPending { get; init; } = 3;

    public TimeSpan PendingTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan DecidedRetention { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>A granted, in-memory lease. Nothing about it is persisted.</summary>
public sealed class AgentLease
{
    internal AgentLease(string leaseId, string agentId, string agentName, AgentAccessMode mode, int blocks,
        DateTimeOffset grantedAt)
    {
        LeaseId = leaseId;
        AgentId = agentId;
        AgentName = agentName;
        Mode = mode;
        Blocks = blocks;
        GrantedAt = grantedAt;
        ExpiresAt = grantedAt.AddMinutes(blocks * AgentLeaseOptions.BlockMinutes);
    }

    public string LeaseId { get; }
    public string AgentId { get; }
    public string AgentName { get; }
    public AgentAccessMode Mode { get; }
    public int Blocks { get; }
    public int Minutes => Blocks * AgentLeaseOptions.BlockMinutes;
    public DateTimeOffset GrantedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Paused from the GUI: calls are refused but the clock keeps running.</summary>
    public bool IsPaused { get; internal set; }

    public TimeSpan Remaining(DateTimeOffset now) => ExpiresAt > now ? ExpiresAt - now : TimeSpan.Zero;
}

public enum AccessRequestStatus { Pending, Granted, Denied, Expired }

/// <summary>An access request waiting for the user.</summary>
public sealed class PendingAccessRequest
{
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PendingAccessRequest(string requestId, AgentRegistration agent, int blocks, AgentAccessMode mode,
        string reason, DateTimeOffset createdAt)
    {
        RequestId = requestId;
        AgentId = agent.Id;
        AgentName = agent.Name;
        AgentFingerprint = agent.Fingerprint;
        RequestedBlocks = blocks;
        RequestedMode = mode;
        Reason = reason;
        CreatedAt = createdAt;
    }

    public string RequestId { get; }
    public string AgentId { get; }
    public string AgentName { get; }
    public string AgentFingerprint { get; }
    public int RequestedBlocks { get; }
    public AgentAccessMode RequestedMode { get; }
    public string Reason { get; }
    public DateTimeOffset CreatedAt { get; }
    public AccessRequestStatus Status { get; private set; } = AccessRequestStatus.Pending;
    public DateTimeOffset? DecidedAt { get; private set; }
    public AgentLease? Lease { get; private set; }

    public Task Settled => _settled.Task;

    internal void Settle(AccessRequestStatus status, DateTimeOffset at, AgentLease? lease = null)
    {
        Status = status;
        DecidedAt = at;
        Lease = lease;
        _settled.TrySetResult();
    }
}

/// <summary>The user's answer in the access dialog: fewer blocks and a lower mode are allowed, more of either is not.</summary>
public sealed record AccessDecision(bool Approved, int Blocks, AgentAccessMode Mode)
{
    public static readonly AccessDecision Deny = new(false, 0, AgentAccessMode.Read);

    public static AccessDecision Grant(int blocks, AgentAccessMode mode) => new(true, blocks, mode);
}

public enum AccessRequestOutcomeKind { Created, Collapsed, Invalid, Busy }

public sealed record AccessRequestOutcome(
    AccessRequestOutcomeKind Kind,
    PendingAccessRequest? Request = null,
    string? Error = null);

/// <summary>
/// What the access dialog offers (S55 "Approval fatigue"): never more than was asked for, one block and read-only
/// preselected, so the quickest click grants the least.
/// </summary>
public static class AgentAccessChoices
{
    public const int DefaultBlocks = 1;
    public const AgentAccessMode DefaultMode = AgentAccessMode.Read;

    public static IReadOnlyList<int> Blocks(int requestedBlocks) =>
        Enumerable.Range(1, Math.Clamp(requestedBlocks, 1, AgentLeaseOptions.AbsoluteMaxBlocks)).ToList();

    public static IReadOnlyList<AgentAccessMode> Modes(AgentAccessMode requested) =>
        requested == AgentAccessMode.ReadWrite
            ? new[] { AgentAccessMode.Read, AgentAccessMode.ReadWrite }
            : new[] { AgentAccessMode.Read };
}

/// <summary>A snapshot for the access dialog.</summary>
public sealed record AccessPrompt(
    string RequestId,
    string AgentName,
    string AgentFingerprint,
    string Reason,
    int RequestedBlocks,
    AgentAccessMode RequestedMode,
    int WaitingCount);

/// <summary>
/// Access leases (T348): requested in blocks of 10 minutes, granted, shortened or downgraded by the user, held only
/// in memory and ended on expiry, sign-out, lock, exit, revoke or by the user. One active lease per agent; a new
/// request while one is active changes nothing until it is approved, and then replaces it. Thread-safe.
/// </summary>
public sealed class AgentLeaseManager
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly IAgentAuditSink _audit;
    private readonly AgentLeaseOptions _options;
    private readonly Dictionary<string, AgentLease> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAccessRequest> _requests = new(StringComparer.Ordinal);

    public AgentLeaseManager(TimeProvider time, IAgentAuditSink audit, AgentLeaseOptions? options = null)
    {
        _time = time;
        _audit = audit;
        _options = options ?? new AgentLeaseOptions();
    }

    public int MaxBlocks => _options.MaxBlocks;

    /// <summary>Raised after a lease ends, with the reason. May fire on any thread.</summary>
    public event Action<AgentLease, LeaseEndReason>? LeaseEnded;

    /// <summary>Raised after a new access request starts waiting for the user.</summary>
    public event Action<PendingAccessRequest>? RequestAdded;

    /// <summary>Raised after any change the GUI shows.</summary>
    public event Action? Changed;

    /// <summary>Starts an access request for an authenticated agent, or says why not.</summary>
    public AccessRequestOutcome Request(AgentRegistration agent, int blocks, AgentAccessMode mode, string? reason)
    {
        if (blocks < 1 || blocks > _options.MaxBlocks)
            return new(AccessRequestOutcomeKind.Invalid,
                Error: $"blocks must be between 1 and {_options.MaxBlocks} ({AgentLeaseOptions.BlockMinutes} minutes each)");
        if (!AgentText.TryClean(reason, AgentText.MaxReasonLength, required: true, out var cleanReason))
            return new(AccessRequestOutcomeKind.Invalid,
                Error: $"reason is required and must be at most {AgentText.MaxReasonLength} characters");

        PendingAccessRequest created;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            RaiseLater(SweepLocked(now));

            var existing = _requests.Values.FirstOrDefault(r =>
                r.AgentId == agent.Id && r.Status == AccessRequestStatus.Pending);
            if (existing is not null) return new(AccessRequestOutcomeKind.Collapsed, existing);

            if (_requests.Values.Count(r => r.Status == AccessRequestStatus.Pending) >= _options.MaxPending)
            {
                Record(AgentAuditKind.AccessRefused, agent.Id, agent.Name, "too many requests waiting");
                return new(AccessRequestOutcomeKind.Busy, Error: "too many access requests are waiting for the user");
            }

            created = new PendingAccessRequest(RandomGenerator.RandomToken(24), agent, blocks, mode, cleanReason, now);
            _requests[created.RequestId] = created;
            Record(AgentAuditKind.AccessRequested, agent.Id, agent.Name,
                $"{blocks * AgentLeaseOptions.BlockMinutes} min, {mode}");
        }

        RequestAdded?.Invoke(created);
        Changed?.Invoke();
        return new(AccessRequestOutcomeKind.Created, created);
    }

    public AccessPrompt? GetPrompt(string requestId)
    {
        lock (_gate)
        {
            if (!_requests.TryGetValue(requestId, out var r) || r.Status != AccessRequestStatus.Pending) return null;
            var waiting = _requests.Values.Count(o => o.Status == AccessRequestStatus.Pending) - 1;
            return new AccessPrompt(r.RequestId, r.AgentName, r.AgentFingerprint, r.Reason, r.RequestedBlocks,
                r.RequestedMode, waiting);
        }
    }

    public PendingAccessRequest? FindRequest(string requestId)
    {
        lock (_gate) return _requests.GetValueOrDefault(requestId);
    }

    /// <summary>
    /// The user's decision. Refused (false) when the request is no longer waiting, or when the grant asks for more
    /// blocks than requested or a higher mode than requested — the dialog can only shorten and downgrade.
    /// </summary>
    public bool Decide(string requestId, AccessDecision decision)
    {
        List<(AgentLease, LeaseEndReason)> ended;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ended = SweepLocked(now);
            _requests.TryGetValue(requestId, out var r);
            var invalidGrant = decision.Approved
                               && (decision.Blocks < 1 || decision.Blocks > (r?.RequestedBlocks ?? 0)
                                   || (decision.Mode == AgentAccessMode.ReadWrite
                                       && r?.RequestedMode == AgentAccessMode.Read));
            if (r is null || r.Status != AccessRequestStatus.Pending || invalidGrant)
            {
                RaiseLater(ended);
                return false;
            }

            if (!decision.Approved)
            {
                r.Settle(AccessRequestStatus.Denied, now);
                Record(AgentAuditKind.AccessDenied, r.AgentId, r.AgentName, "denied by the user");
            }
            else
            {

                if (_leases.Remove(r.AgentId, out var previous))
                {
                    ended.Add((previous, LeaseEndReason.Replaced));
                    Record(AgentAuditKind.LeaseEnded, previous.AgentId, previous.AgentName, nameof(LeaseEndReason.Replaced));
                }

                var lease = new AgentLease(RandomGenerator.RandomToken(16), r.AgentId, r.AgentName, decision.Mode,
                    decision.Blocks, now);
                _leases[r.AgentId] = lease;
                r.Settle(AccessRequestStatus.Granted, now, lease);
                Record(AgentAuditKind.AccessGranted, r.AgentId, r.AgentName,
                    $"{lease.Minutes} min, {lease.Mode}, until {lease.ExpiresAt:O}");
            }
        }

        RaiseEnded(ended);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Waits up to <paramref name="wait"/> for the user's decision on the agent's own request.</summary>
    public async Task<PendingAccessRequest?> WaitAsync(string? requestId, string agentId, TimeSpan wait,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(requestId) || requestId.Length > AgentText.MaxIdLength) return null;

        PendingAccessRequest? request;
        lock (_gate)
        {
            RaiseLater(SweepLocked(_time.GetUtcNow()));
            request = _requests.GetValueOrDefault(requestId);
        }
        // Another agent's request id is reported as unknown, not as someone else's state.
        if (request is null || request.AgentId != agentId) return null;

        if (request.Status == AccessRequestStatus.Pending && wait > TimeSpan.Zero)
        {
            try
            {
                await request.Settled.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Still waiting; the caller reports "pending".
            }
        }

        lock (_gate)
        {
            RaiseLater(SweepLocked(_time.GetUtcNow()));
            return request;
        }
    }

    /// <summary>The agent's current lease, or null. An expired lease ends here, lazily, as well as on the timer.</summary>
    public AgentLease? GetLease(string agentId)
    {
        lock (_gate)
        {
            RaiseLater(SweepLocked(_time.GetUtcNow()));
            return _leases.GetValueOrDefault(agentId);
        }
    }

    public PendingAccessRequest? GetPendingFor(string agentId)
    {
        lock (_gate)
        {
            return _requests.Values.FirstOrDefault(r => r.AgentId == agentId && r.Status == AccessRequestStatus.Pending);
        }
    }

    /// <summary>
    /// Whether the agent may run a tool now. Every business tool (M62+) asks this before calling the API; a read
    /// lease answers <see cref="LeaseCheck.ReadOnly"/> to an edit tool.
    /// </summary>
    public LeaseCheck Authorize(string agentId, bool requiresEdit)
    {
        var lease = GetLease(agentId);
        if (lease is null) return LeaseCheck.NoLease;
        if (lease.IsPaused) return LeaseCheck.Paused;
        if (requiresEdit && lease.Mode != AgentAccessMode.ReadWrite) return LeaseCheck.ReadOnly;
        return LeaseCheck.Allowed;
    }

    public IReadOnlyList<AgentLease> ActiveLeases()
    {
        lock (_gate)
        {
            RaiseLater(SweepLocked(_time.GetUtcNow()));
            return _leases.Values.ToList();
        }
    }

    /// <summary>Pauses the agent's lease: calls are refused, the clock keeps running.</summary>
    public bool Pause(string agentId) => SetPaused(agentId, true);

    public bool Resume(string agentId) => SetPaused(agentId, false);

    /// <summary>Ends one agent's lease now.</summary>
    public bool End(string agentId, LeaseEndReason reason)
    {
        AgentLease? lease;
        lock (_gate)
        {
            if (!_leases.Remove(agentId, out lease)) return false;
            Record(AgentAuditKind.LeaseEnded, lease.AgentId, lease.AgentName, reason.ToString());
        }
        RaiseEnded(new List<(AgentLease, LeaseEndReason)> { (lease, reason) });
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends every lease and drops every waiting request: sign-out, lock, exit, server stopped.</summary>
    public void EndAll(LeaseEndReason reason)
    {
        var ended = new List<(AgentLease, LeaseEndReason)>();
        lock (_gate)
        {
            foreach (var lease in _leases.Values)
            {
                ended.Add((lease, reason));
                Record(AgentAuditKind.LeaseEnded, lease.AgentId, lease.AgentName, reason.ToString());
            }
            _leases.Clear();
            CancelRequestsLocked(_ => true, reason.ToString());
        }
        RaiseEnded(ended);
        Changed?.Invoke();
    }

    /// <summary>Drops the agent's waiting requests (revoked agent).</summary>
    public void CancelRequestsFor(string agentId, string reason)
    {
        lock (_gate) CancelRequestsLocked(r => r.AgentId == agentId, reason);
        Changed?.Invoke();
    }

    /// <summary>Ends expired leases and expires stale requests. Called on every operation and by the host's timer.</summary>
    public void Sweep()
    {
        List<(AgentLease, LeaseEndReason)> ended;
        lock (_gate) ended = SweepLocked(_time.GetUtcNow());
        RaiseEnded(ended);
        if (ended.Count > 0) Changed?.Invoke();
    }

    private bool SetPaused(string agentId, bool paused)
    {
        lock (_gate)
        {
            RaiseLater(SweepLocked(_time.GetUtcNow()));
            if (!_leases.TryGetValue(agentId, out var lease) || lease.IsPaused == paused) return false;
            lease.IsPaused = paused;
            Record(paused ? AgentAuditKind.LeasePaused : AgentAuditKind.LeaseResumed, lease.AgentId, lease.AgentName,
                paused ? "paused by the user" : "resumed by the user");
        }
        Changed?.Invoke();
        return true;
    }

    private void CancelRequestsLocked(Func<PendingAccessRequest, bool> predicate, string reason)
    {
        var now = _time.GetUtcNow();
        foreach (var r in _requests.Values.Where(predicate).ToList())
        {
            if (r.Status == AccessRequestStatus.Pending)
            {
                r.Settle(AccessRequestStatus.Expired, now);
                Record(AgentAuditKind.AccessRequestExpired, r.AgentId, r.AgentName, reason);
            }
            _requests.Remove(r.RequestId);
        }
    }

    private List<(AgentLease, LeaseEndReason)> SweepLocked(DateTimeOffset now)
    {
        var ended = new List<(AgentLease, LeaseEndReason)>();
        foreach (var lease in _leases.Values.Where(l => now >= l.ExpiresAt).ToList())
        {
            _leases.Remove(lease.AgentId);
            ended.Add((lease, LeaseEndReason.Expired));
            Record(AgentAuditKind.LeaseEnded, lease.AgentId, lease.AgentName, nameof(LeaseEndReason.Expired));
        }

        foreach (var r in _requests.Values.ToList())
        {
            if (r.Status == AccessRequestStatus.Pending && now - r.CreatedAt >= _options.PendingTimeout)
            {
                r.Settle(AccessRequestStatus.Expired, now);
                Record(AgentAuditKind.AccessRequestExpired, r.AgentId, r.AgentName, "not decided in time");
            }
            else if (r.Status != AccessRequestStatus.Pending && now - r.DecidedAt!.Value >= _options.DecidedRetention)
            {
                _requests.Remove(r.RequestId);
            }
        }

        return ended;
    }

    private void RaiseLater(List<(AgentLease, LeaseEndReason)> ended)
    {
        if (ended.Count == 0) return;
        // Never raise while holding the lock: a handler that calls back in would deadlock.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            RaiseEnded(ended);
            Changed?.Invoke();
        });
    }

    private void RaiseEnded(List<(AgentLease, LeaseEndReason)> ended)
    {
        foreach (var (lease, reason) in ended) LeaseEnded?.Invoke(lease, reason);
    }

    private void Record(AgentAuditKind kind, string? agentId, string? agentName, string detail) =>
        _audit.Record(new AgentAuditEvent(kind, _time.GetUtcNow(), agentId, agentName, detail));
}
