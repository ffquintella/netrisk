using System;
using System.Collections.Generic;
using System.Threading;
using DAL.Context;

namespace ServerServices.Security;

/// <summary>
/// The process-wide cache of the entity tree that hierarchical scope (T292, S40 §11.3) expands claims
/// through. One instance per host, shared by every <see cref="Services.DalService"/> the host builds.
/// </summary>
public interface IEntityHierarchy
{
    /// <summary>
    /// The current snapshot, loaded through <paramref name="load"/> when there is none, when it is older
    /// than the time to live, or after <see cref="Invalidate"/>.
    /// </summary>
    EntityTree GetTree(Func<IReadOnlyCollection<EntityTreeRow>> load);

    /// <summary>
    /// Drops the snapshot. Called after every save that creates, deletes or re-parents an entity
    /// (<c>AuditableContext.EntityTreeChanged</c>), so a change of parent moves access on the next
    /// request, without a new login.
    /// </summary>
    void Invalidate();

    /// <summary>Increments on every invalidation and every load: a cache keyed by expanded scope adds it to its key.</summary>
    long Revision { get; }
}

/// <summary>
/// <see cref="IEntityHierarchy"/> over an in-memory snapshot.
///
/// Invalidation is local to the process. A change made by another process — a second API instance, the
/// console client, hand-written SQL — is picked up when the snapshot's time to live runs out, so that is
/// the bound on how long a moved subtree stays visible to its old unit's users there. A load that races an
/// invalidation is used for the request that made it but not kept, so it cannot pin a stale tree until the
/// time to live.
/// </summary>
public sealed class EntityHierarchy : IEntityHierarchy
{
    /// <summary>
    /// How long a snapshot is trusted without a local invalidation. Short, because it bounds the
    /// cross-process staleness of a security decision; loading the tree is one narrow query over a table
    /// of hundreds to a few thousand rows.
    /// </summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);

    private sealed record Snapshot(EntityTree Tree, DateTime LoadedAt);

    private readonly TimeSpan _timeToLive;
    private readonly Func<DateTime> _clock;
    private readonly object _loadGate = new();

    private Snapshot? _snapshot;
    private long _revision;
    private long _invalidations;

    public EntityHierarchy() : this(DefaultTimeToLive, () => DateTime.UtcNow)
    {
    }

    public EntityHierarchy(TimeSpan timeToLive, Func<DateTime> clock)
    {
        _timeToLive = timeToLive;
        _clock = clock;
    }

    public long Revision => Interlocked.Read(ref _revision);

    public EntityTree GetTree(Func<IReadOnlyCollection<EntityTreeRow>> load)
    {
        var current = Volatile.Read(ref _snapshot);
        if (IsFresh(current)) return current!.Tree;

        lock (_loadGate)
        {
            current = Volatile.Read(ref _snapshot);
            if (IsFresh(current)) return current!.Tree;

            var invalidationsAtStart = Interlocked.Read(ref _invalidations);
            var loadedAt = _clock();
            var tree = EntityTree.Build(load(), Interlocked.Increment(ref _revision));

            // Kept only when nothing invalidated the tree while it loaded: otherwise the rows may predate
            // the change that invalidated it, and keeping them would undo the invalidation.
            if (Interlocked.Read(ref _invalidations) == invalidationsAtStart)
                Volatile.Write(ref _snapshot, new Snapshot(tree, loadedAt));

            return tree;
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _invalidations);
        Interlocked.Increment(ref _revision);
        Volatile.Write(ref _snapshot, null);
    }

    private bool IsFresh(Snapshot? snapshot) =>
        snapshot is not null && _clock() - snapshot.LoadedAt < _timeToLive;
}
