using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

    // T356: per item, every field an agent changed that the user has not acknowledged or edited since — highlighted on
    // screen until then. A union over the agent's writes; no time limit.
    private readonly Dictionary<AgentItemRef, AgentItemChange> _unacknowledged = new();

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

    /// <param name="movesVersion">
    /// False for an append that leaves the item's record as it was (T356 review): presence, the notice and the highlight
    /// show it, but <see cref="ChangedSince"/> does not, so a screen's copy of the item is not refused over it.
    /// </param>
    public void RecordChange(AgentItemRef item, string agentName, IReadOnlyList<string> fields, DateTimeOffset at,
        bool movesVersion = true)
    {
        var change = new AgentItemChange(item, agentName, at, fields.ToList());
        lock (_gate)
        {
            if (movesVersion) _changes[item] = change;
            _present[item] = new AgentItemPresence(item, agentName, true, at);
            var pending = _unacknowledged.TryGetValue(item, out var before)
                ? before.Fields.Concat(fields).Distinct(StringComparer.Ordinal).ToList()
                : fields.Distinct(StringComparer.Ordinal).ToList();
            if (pending.Count > 0) _unacknowledged[item] = new AgentItemChange(item, agentName, at, pending);
        }
        ItemChanged?.Invoke(change);
        Changed?.Invoke();
    }

    /// <summary>
    /// T356: the fields agents changed on <paramref name="item"/> that the user has not acknowledged or edited yet (the
    /// latest agent and time), or null. What a screen highlights.
    /// </summary>
    public AgentItemChange? Unacknowledged(AgentItemRef item)
    {
        lock (_gate) return _unacknowledged.TryGetValue(item, out var change) ? change : null;
    }

    /// <summary>T356: the user marked the agent's changes to <paramref name="item"/> as seen; nothing stays highlighted.</summary>
    public void Acknowledge(AgentItemRef item)
    {
        bool removed;
        lock (_gate) removed = _unacknowledged.Remove(item);
        if (removed) Changed?.Invoke();
    }

    /// <summary>
    /// T356: the user edited <paramref name="fields"/> of <paramref name="item"/> (every field when null — the user saved
    /// the whole item): their highlight ends.
    /// </summary>
    public void UserEdited(AgentItemRef item, IEnumerable<string>? fields = null)
    {
        bool changed;
        lock (_gate)
        {
            if (!_unacknowledged.TryGetValue(item, out var pending)) return;
            var left = fields is null
                ? new List<string>()
                : pending.Fields.Except(fields, StringComparer.Ordinal).ToList();
            changed = left.Count != pending.Fields.Count;
            if (left.Count == 0) _unacknowledged.Remove(item);
            else if (changed) _unacknowledged[item] = pending with { Fields = left };
        }
        if (changed) Changed?.Invoke();
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
            _unacknowledged.Clear();
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

    /// <summary>
    /// T356 review: runs the screen's own save of the item. Only a save that reports success makes the screen's copy
    /// current and ends the agent's highlights on the item (the user edited it); one that throws or reports failure leaves
    /// both as they were, and the exception reaches the screen.
    /// </summary>
    public async Task<bool> SaveAsync(Func<Task<bool>> save, Func<DateTimeOffset> now) =>
        Saved(await save().ConfigureAwait(false), now);

    /// <summary>The same for a synchronous save (<see cref="SaveAsync"/>).</summary>
    public bool Save(Func<bool> save, Func<DateTimeOffset> now) => Saved(save(), now);

    private bool Saved(bool succeeded, Func<DateTimeOffset> now)
    {
        if (!succeeded) return false;
        Reset(now());
        _presence?.UserEdited(Item);
        return true;
    }
}
