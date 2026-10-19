using System;
using System.Collections.Generic;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The client cache of the last visited nodes (S40 §10.9; T254): a snapshot of a day never changes, so going back along the
/// breadcrumb to one of the last <see cref="DefaultCapacity"/> (node, date) pairs needs no request. Least-recently used
/// entries leave first; "Atualizar" clears it. Pure; not thread-safe — the view-models touch it on the UI thread only.
/// </summary>
public sealed class RecentCache<TKey, TValue> where TKey : notnull
{
    public const int DefaultCapacity = 20;

    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _index = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public RecentCache(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "The cache keeps at least one entry.");
        _capacity = capacity;
    }

    public int Count => _index.Count;

    /// <summary>The entry, which becomes the most recently used one.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Stores or replaces the entry as the most recent one, dropping the least recent beyond the capacity.</summary>
    public void Put(TKey key, TValue value)
    {
        if (_index.TryGetValue(key, out var existing)) _order.Remove(existing);
        var node = _order.AddFirst((key, value));
        _index[key] = node;
        while (_index.Count > _capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _index.Remove(last.Value.Key);
        }
    }

    public void Clear()
    {
        _index.Clear();
        _order.Clear();
    }
}
