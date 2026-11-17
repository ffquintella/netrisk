using System;
using System.Collections.Generic;
using System.Linq;

namespace GUIClient.Agents;

/// <summary>
/// One leased tool call as the activity panel lists it (T355): when, which agent, which tool, which item, read or edit,
/// and the outcome status. Never a field value, never item text, never the key.
/// </summary>
public sealed record AgentActivityEntry(DateTimeOffset At, string AgentId, string AgentName, string Tool, bool IsEdit,
    AgentItemRef? Item, string Result);

/// <summary>The live list behind the activity panel: the most recent calls, bounded, newest last. Thread-safe.</summary>
public sealed class AgentActivityLog
{
    public const int Capacity = 200;

    private readonly object _gate = new();
    private readonly LinkedList<AgentActivityEntry> _entries = new();

    /// <summary>Raised after an entry is added. May fire on any thread.</summary>
    public event Action<AgentActivityEntry>? Added;

    public void Record(AgentActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > Capacity) _entries.RemoveFirst();
        }
        Added?.Invoke(entry);
    }

    public IReadOnlyList<AgentActivityEntry> Entries()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}

/// <summary>What an agent changed on one item (T356): which fields, when, by which agent. Field names only.</summary>
public sealed record AgentItemChange(AgentItemRef Item, string AgentName, DateTimeOffset At, IReadOnlyList<string> Fields);

/// <summary>An item an agent is reading or editing now, for the presence marker (T356).</summary>
public sealed record AgentItemPresence(AgentItemRef Item, string AgentName, bool Editing, DateTimeOffset LastSeen);

/// <summary>
/// Item presence and change notices (T356): which items an agent touched recently, and what it changed. Views mark an
/// item an agent is on, refresh one it changed, and an edit form opened before the agent's write refuses to save over it
/// (<see cref="ChangedSince"/>). Thread-safe; events may fire on any thread, so the GUI marshals them.
/// </summary>
public sealed class AgentPresence
{
    /// <summary>How long an item stays marked after the agent's last call on it.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly Dictionary<AgentItemRef, AgentItemPresence> _present = new();
    private readonly Dictionary<AgentItemRef, AgentItemChange> _changes = new();

    /// <summary>Raised after an item was touched or changed.</summary>
    public event Action? Changed;

    /// <summary>Raised after an agent's write to an item landed.</summary>
    public event Action<AgentItemChange>? ItemChanged;

    public void Touch(AgentItemRef item, string agentName, bool editing, DateTimeOffset at)
    {
        lock (_gate)
        {
            var wasEditing = _present.TryGetValue(item, out var before) && before.Editing && at - before.LastSeen < Window;
            _present[item] = new AgentItemPresence(item, agentName, editing || wasEditing, at);
        }
        Changed?.Invoke();
    }

    public void RecordChange(AgentItemRef item, string agentName, IReadOnlyList<string> fields, DateTimeOffset at)
    {
        var change = new AgentItemChange(item, agentName, at, fields.ToList());
        lock (_gate)
        {
            _changes[item] = change;
            _present[item] = new AgentItemPresence(item, agentName, true, at);
        }
        ItemChanged?.Invoke(change);
        Changed?.Invoke();
    }

    /// <summary>The items touched within <see cref="Window"/> of <paramref name="now"/>, most recent first.</summary>
    public IReadOnlyList<AgentItemPresence> Active(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _present.Values.Where(p => now - p.LastSeen < Window).OrderByDescending(p => p.LastSeen).ToList();
        }
    }

    public AgentItemPresence? PresenceOf(AgentItemRef item, DateTimeOffset now)
    {
        lock (_gate)
            return _present.TryGetValue(item, out var p) && now - p.LastSeen < Window ? p : null;
    }

    /// <summary>
    /// The agent's last change to <paramref name="item"/> if it landed after <paramref name="since"/> — the moment a form
    /// loaded the item. A form that gets one must not save: its copy is older than the agent's write.
    /// </summary>
    public AgentItemChange? ChangedSince(AgentItemRef item, DateTimeOffset since)
    {
        lock (_gate)
            return _changes.TryGetValue(item, out var change) && change.At > since ? change : null;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _present.Clear();
            _changes.Clear();
        }
        Changed?.Invoke();
    }
}

/// <summary>The lease countdown the status bar shows (T355): remaining time and the one-minute expiry warning.</summary>
public static class AgentLeaseClock
{
    public static readonly TimeSpan WarningBefore = TimeSpan.FromMinutes(1);

    /// <summary>True in the last minute of a lease that has not ended.</summary>
    public static bool IsExpiring(AgentLease lease, DateTimeOffset now)
    {
        var remaining = lease.Remaining(now);
        return remaining > TimeSpan.Zero && remaining <= WarningBefore;
    }

    /// <summary>Remaining time as <c>m:ss</c>, never negative.</summary>
    public static string Remaining(AgentLease lease, DateTimeOffset now)
    {
        var remaining = lease.Remaining(now);
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}

/// <summary>
/// T356, review M63 #7: a screen's guard against saving a copy older than an agent's write. Opened when the screen's copy
/// of the item was read from the server (<paramref name="since"/>); <see cref="Conflict"/> is the agent's change that
/// landed after it, if any — then the screen must not save that copy (or must merge the server's values first).
/// </summary>
public sealed class AgentSaveGuard
{
    private readonly AgentPresence? _presence;

    public AgentSaveGuard(AgentPresence? presence, AgentItemRef item, DateTimeOffset since)
    {
        _presence = presence;
        Item = item;
        Since = since;
    }

    public AgentItemRef Item { get; }

    public DateTimeOffset Since { get; private set; }

    public AgentItemChange? Conflict() => _presence?.ChangedSince(Item, Since);

    /// <summary>The screen holds the server's copy again (it saved, or reloaded): only later changes conflict.</summary>
    public void Reset(DateTimeOffset now) => Since = now;
}
