using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace GUIClient.Agents;

/// <summary>Limits on <c>register_agent</c> (T346). Defaults are the shipped values; tests shrink them.</summary>
public sealed class AgentRegistryOptions
{
    /// <summary>Registrations waiting for the user at once. Beyond it a request is refused and only counted.</summary>
    public int MaxPending { get; init; } = 3;

    /// <summary>New registration attempts accepted per <see cref="RateWindow"/>.</summary>
    public int MaxAttemptsPerWindow { get; init; } = 10;

    public TimeSpan RateWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a registration waits for the user before it expires.</summary>
    public TimeSpan PendingTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an approved registration waits for its agent to collect the key before it is discarded.</summary>
    public TimeSpan CollectTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a denied or expired registration still answers its agent's poll before it is forgotten.</summary>
    public TimeSpan DecidedRetention { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxRegistrationsPerUser { get; init; } = 20;
}

public enum PendingRegistrationStatus { Pending, Approved, Denied, Expired, Collected }

/// <summary>A registration the user has not decided yet, or whose key the agent has not collected yet.</summary>
public sealed class PendingRegistration
{
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PendingRegistration(string pendingId, int userId, string name, string description, AgentClientInfo client,
        DateTimeOffset createdAt)
    {
        PendingId = pendingId;
        UserId = userId;
        Name = name;
        Description = description;
        Client = client;
        CreatedAt = createdAt;
        Fingerprint = AgentKeyIssuer.Fingerprint(pendingId);
    }

    /// <summary>Random, returned only to the caller that registered: it is what collects the key.</summary>
    public string PendingId { get; }

    public int UserId { get; }
    public string Name { get; }
    public string Description { get; }
    public AgentClientInfo Client { get; }
    public string Fingerprint { get; }
    public DateTimeOffset CreatedAt { get; }
    public PendingRegistrationStatus Status { get; private set; } = PendingRegistrationStatus.Pending;
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Completes when the registration stops waiting for the user (decided, expired or cancelled).</summary>
    public Task Settled => _settled.Task;

    internal void Settle(PendingRegistrationStatus status, DateTimeOffset at)
    {
        Status = status;
        DecidedAt = at;
        _settled.TrySetResult();
    }
}

public enum RegistrationOutcomeKind { Created, Invalid, RateLimited, AlreadyPending, NoUser }

/// <summary>What <see cref="AgentRegistry.Begin"/> did with a request.</summary>
public sealed record RegistrationOutcome(
    RegistrationOutcomeKind Kind,
    PendingRegistration? Pending = null,
    string? Error = null,
    TimeSpan? RetryAfter = null);

public enum RegistrationResultKind { Pending, Approved, Denied, Expired, NotFound }

/// <summary>Where a pending registration stands for the agent polling it. Carries the key only on approval.</summary>
public sealed class RegistrationResult
{
    internal RegistrationResult(RegistrationResultKind kind, PendingRegistration? pending = null,
        AgentRegistration? registration = null, IssuedAgentKey? key = null)
    {
        Kind = kind;
        Pending = pending;
        Registration = registration;
        Key = key;
    }

    public RegistrationResultKind Kind { get; }
    public PendingRegistration? Pending { get; }
    public AgentRegistration? Registration { get; }
    public IssuedAgentKey? Key { get; }

    public override string ToString() => $"RegistrationResult {{ Kind = {Kind}, Agent = {Registration?.Id} }}";
}

/// <summary>A snapshot for the approval dialog: the request and how many others are queued behind it.</summary>
public sealed record RegistrationPrompt(
    string PendingId,
    string Name,
    string Description,
    AgentClientInfo Client,
    string Fingerprint,
    int WaitingCount,
    int RefusedCount);

/// <summary>
/// Approved agents of the signed-in user and the registrations waiting for the user (T346, T347). Thread-safe: the
/// MCP server calls it from connection threads, the GUI from the UI thread. Registrations are bound to the user id;
/// one never authenticates while another user is signed in.
/// </summary>
public sealed class AgentRegistry
{
    private readonly object _gate = new();
    private readonly IAgentRegistrationStore _store;
    private readonly TimeProvider _time;
    private readonly IAgentAuditSink _audit;
    private readonly AgentRegistryOptions _options;
    private readonly List<AgentRegistration> _registrations;
    private readonly Dictionary<string, PendingRegistration> _pending = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _attempts = new();
    private int? _userId;
    private int _refusedWhilePending;

    public AgentRegistry(IAgentRegistrationStore store, TimeProvider time, IAgentAuditSink audit,
        AgentRegistryOptions? options = null)
    {
        _store = store;
        _time = time;
        _audit = audit;
        _options = options ?? new AgentRegistryOptions();
        _registrations = store.Load().ToList();
    }

    /// <summary>Raised after any change the Agents window shows. May fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a registration is revoked, so its lease can end at once.</summary>
    public event Action<AgentRegistration>? Revoked;

    /// <summary>Raised when a new registration starts waiting for the user.</summary>
    public event Action<PendingRegistration>? PendingAdded;

    public int? CurrentUserId
    {
        get { lock (_gate) return _userId; }
    }

    /// <summary>Requests refused or collapsed while others were waiting — the counter the dialog shows.</summary>
    public int RefusedWhilePending
    {
        get { lock (_gate) return _refusedWhilePending; }
    }

    /// <summary>Sets the signed-in user. A change cancels every pending registration: they belonged to someone else.</summary>
    public void SetUser(int? userId)
    {
        lock (_gate)
        {
            if (_userId == userId) return;
            _userId = userId;
            CancelPendingLocked("user changed");
        }
        Changed?.Invoke();
    }

    /// <summary>Starts a registration, or says why it was not started.</summary>
    public RegistrationOutcome Begin(string? name, string? description, AgentClientInfo? client)
    {
        if (!AgentText.TryClean(name, AgentText.MaxNameLength, required: true, out var cleanName))
            return new(RegistrationOutcomeKind.Invalid,
                Error: $"name is required and must be at most {AgentText.MaxNameLength} characters");
        if (!AgentText.TryClean(description, AgentText.MaxDescriptionLength, required: false, out var cleanDescription))
            return new(RegistrationOutcomeKind.Invalid,
                Error: $"description must be at most {AgentText.MaxDescriptionLength} characters");

        var cleanClient = new AgentClientInfo(
            AgentText.CleanOrEmpty(client?.Name, AgentText.MaxClientNameLength),
            AgentText.CleanOrEmpty(client?.Version, AgentText.MaxClientVersionLength));

        PendingRegistration created;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            SweepLocked(now);

            if (_userId is not { } userId) return new(RegistrationOutcomeKind.NoUser);

            if (_registrations.Count(r => r.UserId == userId) >= _options.MaxRegistrationsPerUser)
                return Refused(RegistrationOutcomeKind.Invalid, cleanName,
                    "this account already has the maximum number of agents; revoke one in NetRisk first");

            while (_attempts.Count > 0 && now - _attempts.Peek() >= _options.RateWindow) _attempts.Dequeue();
            if (_attempts.Count >= _options.MaxAttemptsPerWindow)
            {
                _refusedWhilePending++;
                var retry = _attempts.Peek() + _options.RateWindow - now;
                Record(AgentAuditKind.RegistrationRefused, null, cleanName, "rate limited");
                return new(RegistrationOutcomeKind.RateLimited, RetryAfter: retry);
            }
            _attempts.Enqueue(now);

            var waiting = _pending.Values.Where(p => p.Status == PendingRegistrationStatus.Pending).ToList();

            // Collapsing: a second request for an agent already on screen is counted, not shown again. Its caller does
            // not get the first request's pending id — that id is what collects the key.
            if (waiting.Any(p => string.Equals(p.Name, cleanName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(p.Client.Name, cleanClient.Name, StringComparison.OrdinalIgnoreCase)))
                return Refused(RegistrationOutcomeKind.AlreadyPending, cleanName, "already waiting for the user");

            if (waiting.Count >= _options.MaxPending)
            {
                _refusedWhilePending++;
                Record(AgentAuditKind.RegistrationRefused, null, cleanName, "too many registrations waiting");
                return new(RegistrationOutcomeKind.RateLimited, RetryAfter: _options.PendingTimeout);
            }

            created = new PendingRegistration(RandomGenerator.RandomToken(24), userId, cleanName, cleanDescription,
                cleanClient, now);
            _pending[created.PendingId] = created;
            Record(AgentAuditKind.RegistrationRequested, null, cleanName,
                $"client '{cleanClient.Name}' fingerprint {created.Fingerprint}");
        }

        PendingAdded?.Invoke(created);
        Changed?.Invoke();
        return new(RegistrationOutcomeKind.Created, created);
    }

    /// <summary>The dialog's snapshot of a registration still waiting for the user, or null.</summary>
    public RegistrationPrompt? GetPrompt(string pendingId)
    {
        lock (_gate)
        {
            SweepLocked(_time.GetUtcNow());
            if (!_pending.TryGetValue(pendingId, out var p) || p.Status != PendingRegistrationStatus.Pending) return null;
            var waiting = _pending.Values.Count(o => o.Status == PendingRegistrationStatus.Pending) - 1;
            return new RegistrationPrompt(p.PendingId, p.Name, p.Description, p.Client, p.Fingerprint, waiting,
                _refusedWhilePending);
        }
    }

    public PendingRegistration? FindPending(string pendingId)
    {
        lock (_gate) return _pending.GetValueOrDefault(pendingId);
    }

    /// <summary>The user's decision. False when the registration is no longer waiting.</summary>
    public bool Decide(string pendingId, bool approve)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            SweepLocked(now);
            if (!_pending.TryGetValue(pendingId, out var p) || p.Status != PendingRegistrationStatus.Pending) return false;

            p.Settle(approve ? PendingRegistrationStatus.Approved : PendingRegistrationStatus.Denied, now);
            Record(approve ? AgentAuditKind.RegistrationApproved : AgentAuditKind.RegistrationDenied, null, p.Name,
                $"fingerprint {p.Fingerprint}");
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Waits up to <paramref name="wait"/> for the user, then reports. On approval the key is generated here, its hash
    /// persisted, and the key returned — once: the pending entry is gone afterwards and a second call is NotFound.
    /// </summary>
    public async Task<RegistrationResult> WaitAsync(string? pendingId, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pendingId) || pendingId.Length > AgentText.MaxIdLength)
            return new RegistrationResult(RegistrationResultKind.NotFound);

        PendingRegistration? pending;
        lock (_gate)
        {
            SweepLocked(_time.GetUtcNow());
            pending = _pending.GetValueOrDefault(pendingId);
        }
        if (pending is null) return new RegistrationResult(RegistrationResultKind.NotFound);

        if (pending.Status == PendingRegistrationStatus.Pending && wait > TimeSpan.Zero)
        {
            try
            {
                await pending.Settled.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Still waiting for the user; reported below.
            }
        }

        // A caller that went away (the endpoint cancels on disconnect) must not collect the key: it would be stored
        // and written to a dead connection, and the approved registration could never be collected again.
        cancellationToken.ThrowIfCancellationRequested();

        RegistrationResult result;
        var changed = false;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            SweepLocked(now);
            if (!_pending.TryGetValue(pendingId, out var p)) return new RegistrationResult(RegistrationResultKind.NotFound);

            switch (p.Status)
            {
                case PendingRegistrationStatus.Pending:
                    return new RegistrationResult(RegistrationResultKind.Pending, p);
                case PendingRegistrationStatus.Denied:
                    return new RegistrationResult(RegistrationResultKind.Denied, p);
                case PendingRegistrationStatus.Expired:
                    return new RegistrationResult(RegistrationResultKind.Expired, p);
                case PendingRegistrationStatus.Approved:
                    cancellationToken.ThrowIfCancellationRequested();
                    result = IssueLocked(p, now);
                    changed = true;
                    break;
                default:
                    return new RegistrationResult(RegistrationResultKind.NotFound);
            }
        }
        if (changed) Changed?.Invoke();
        return result;
    }

    /// <summary>The signed-in user's registration whose key is <paramref name="presentedKey"/>, or null.</summary>
    public AgentRegistration? Authenticate(string? presentedKey)
    {
        lock (_gate)
        {
            if (_userId is not { } userId || string.IsNullOrEmpty(presentedKey)) return null;

            AgentRegistration? match = null;
            // Every candidate is compared, match or not, so the time taken says nothing about which one matched.
            foreach (var registration in _registrations)
            {
                var matches = AgentKeyIssuer.Matches(presentedKey, registration.KeyHash);
                if (matches && registration.UserId == userId) match ??= registration;
            }

            if (match is null)
            {
                Record(AgentAuditKind.AuthenticationFailed, null, null, "unknown or revoked agent key");
                return null;
            }

            var now = _time.GetUtcNow();
            var persist = match.LastUsedAt is null || now - match.LastUsedAt.Value > TimeSpan.FromMinutes(5);
            match.LastUsedAt = now;
            if (persist) _store.Save(_registrations);
            return match.Copy();
        }
    }

    /// <summary>The signed-in user's registrations, oldest first.</summary>
    public IReadOnlyList<AgentRegistration> List()
    {
        lock (_gate)
        {
            if (_userId is not { } userId) return Array.Empty<AgentRegistration>();
            return _registrations.Where(r => r.UserId == userId).OrderBy(r => r.CreatedAt).Select(r => r.Copy()).ToList();
        }
    }

    public AgentRegistration? Get(string agentId)
    {
        lock (_gate)
        {
            var found = _registrations.FirstOrDefault(r => r.Id == agentId && r.UserId == _userId);
            return found?.Copy();
        }
    }

    /// <summary>Renames one of the signed-in user's agents. False on an unknown id or an unusable name.</summary>
    public bool Rename(string agentId, string? newName)
    {
        if (!AgentText.TryClean(newName, AgentText.MaxNameLength, required: true, out var cleanName)) return false;
        lock (_gate)
        {
            var found = _registrations.FirstOrDefault(r => r.Id == agentId && r.UserId == _userId);
            if (found is null) return false;
            var previous = found.Name;
            found.Name = cleanName;
            _store.Save(_registrations);
            Record(AgentAuditKind.RegistrationRenamed, found.Id, cleanName, $"was '{previous}'");
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Revokes one of the signed-in user's agents. Immediate: its key stops authenticating on return.</summary>
    public bool Revoke(string agentId)
    {
        AgentRegistration removed;
        lock (_gate)
        {
            var found = _registrations.FirstOrDefault(r => r.Id == agentId && r.UserId == _userId);
            if (found is null) return false;
            _registrations.Remove(found);
            _store.Save(_registrations);
            removed = found.Copy();
            Record(AgentAuditKind.RegistrationRevoked, removed.Id, removed.Name, "revoked by the user");
        }
        Revoked?.Invoke(removed);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Cancels every registration still waiting (server stopped, sign-out).</summary>
    public void CancelPending(string reason)
    {
        lock (_gate) CancelPendingLocked(reason);
        Changed?.Invoke();
    }

    /// <summary>Expires what has waited too long. Called on every operation and by the host's timer.</summary>
    public void Sweep()
    {
        lock (_gate) SweepLocked(_time.GetUtcNow());
    }

    private RegistrationResult IssueLocked(PendingRegistration p, DateTimeOffset now)
    {
        var key = AgentKeyIssuer.NewKey();
        var registration = new AgentRegistration
        {
            Id = RandomGenerator.RandomToken(16),
            UserId = p.UserId,
            Name = p.Name,
            Description = p.Description,
            ClientName = p.Client.Name,
            ClientVersion = p.Client.Version,
            KeyHash = AgentKeyIssuer.Hash(key),
            Fingerprint = p.Fingerprint,
            CreatedAt = now
        };

        _registrations.Add(registration);
        _store.Save(_registrations);
        p.Settle(PendingRegistrationStatus.Collected, now);
        _pending.Remove(p.PendingId);
        Record(AgentAuditKind.KeyIssued, registration.Id, registration.Name, $"fingerprint {registration.Fingerprint}");

        return new RegistrationResult(RegistrationResultKind.Approved, p, registration.Copy(), new IssuedAgentKey(key));
    }

    private RegistrationOutcome Refused(RegistrationOutcomeKind kind, string name, string reason)
    {
        _refusedWhilePending++;
        Record(AgentAuditKind.RegistrationRefused, null, name, reason);
        return new RegistrationOutcome(kind, Error: reason);
    }

    private void CancelPendingLocked(string reason)
    {
        var now = _time.GetUtcNow();
        foreach (var p in _pending.Values.ToList())
        {
            if (p.Status is PendingRegistrationStatus.Pending or PendingRegistrationStatus.Approved)
            {
                p.Settle(PendingRegistrationStatus.Expired, now);
                Record(AgentAuditKind.RegistrationExpired, null, p.Name, reason);
            }
        }
        _pending.Clear();
        _refusedWhilePending = 0;
    }

    private void SweepLocked(DateTimeOffset now)
    {
        foreach (var p in _pending.Values.ToList())
        {
            switch (p.Status)
            {
                case PendingRegistrationStatus.Pending when now - p.CreatedAt >= _options.PendingTimeout:
                    p.Settle(PendingRegistrationStatus.Expired, now);
                    Record(AgentAuditKind.RegistrationExpired, null, p.Name, "not decided in time");
                    break;
                case PendingRegistrationStatus.Approved when now - p.DecidedAt!.Value >= _options.CollectTimeout:
                    p.Settle(PendingRegistrationStatus.Expired, now);
                    _pending.Remove(p.PendingId);
                    Record(AgentAuditKind.RegistrationAbandoned, null, p.Name, "approved but never collected");
                    break;
                case PendingRegistrationStatus.Denied or PendingRegistrationStatus.Expired
                    when now - p.DecidedAt!.Value >= _options.DecidedRetention:
                    _pending.Remove(p.PendingId);
                    break;
            }
        }

        if (!_pending.Values.Any(p => p.Status == PendingRegistrationStatus.Pending)) _refusedWhilePending = 0;
    }

    private void Record(AgentAuditKind kind, string? agentId, string? agentName, string detail) =>
        _audit.Record(new AgentAuditEvent(kind, _time.GetUtcNow(), agentId, agentName, detail));
}
