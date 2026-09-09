using System.Collections.Generic;
using System.Linq;

namespace GUIClient.Tools;

/// <summary>
/// An id → display-label map for a list view's foreign-key columns.
///
/// A grid column that shows the *name* behind an id has two ways to get it: resolve each cell as it
/// renders, or resolve the whole page once and let the cells read the answer. The findings grid did
/// the first, through <c>TeamIdToTeamNameConverter</c> and <c>HostIdToNameConverter</c>. A value
/// converter cannot be asynchronous, so "resolve" there meant a blocking REST call on the UI thread,
/// once per cell and again on every re-render — a page of twenty findings issued about ninety
/// serialised requests, froze the window for several seconds, and, when the server was unreachable,
/// logged one error per cell. Neither converter caches a failure, so nothing ever damped it down.
///
/// This is the other way. The view model fills the map for the ids on the page in a couple of
/// requests, off the UI thread, and the columns become dictionary lookups.
///
/// Every method takes the lock: a page assignment can start a refresh while the UI thread is still
/// rendering the previous one, and a <see cref="Dictionary{TKey,TValue}"/> read concurrent with a
/// write is not safe.
/// </summary>
public sealed class RowLabelCache
{
    private readonly Dictionary<int, string> _labels = new();
    private readonly object _gate = new();

    /// <summary>
    /// The distinct ids in <paramref name="ids"/> that have no label yet.
    ///
    /// Nulls are dropped — a finding with no fix team is not an unresolved fix team — and ids
    /// already known are skipped, so paging back to a page that has been seen costs no request.
    /// </summary>
    public IReadOnlyList<int> Missing(IEnumerable<int?>? ids)
    {
        if (ids == null) return [];

        lock (_gate)
        {
            return ids.Where(id => id != null)
                .Select(id => id!.Value)
                .Distinct()
                .Where(id => !_labels.ContainsKey(id))
                .ToList();
        }
    }

    /// <summary>
    /// The ids in <paramref name="ids"/> that still have no label.
    ///
    /// For narrowing a set that has already been through <see cref="Missing"/> — a bulk listing does
    /// not always cover every id it was asked about, and what it left over is what needs resolving
    /// one at a time. A separate name rather than an overload: <c>int[]</c> is ambiguous between the
    /// two signatures, and this reads better at the call site anyway.
    /// </summary>
    public IReadOnlyList<int> StillMissing(IEnumerable<int>? ids)
    {
        if (ids == null) return [];

        lock (_gate)
        {
            return ids.Distinct().Where(id => !_labels.ContainsKey(id)).ToList();
        }
    }

    /// <summary>Records the label to show for an id.</summary>
    public void Set(int id, string label)
    {
        lock (_gate)
        {
            _labels[id] = label;
        }
    }

    /// <summary>
    /// The cell text for an id: null when there is no id at all, the resolved label when it is
    /// known, and the bare id otherwise.
    ///
    /// The bare id rather than an empty string is deliberate. An unresolved id means the prefetch
    /// has not landed yet or the server refused it, and both of those are states in which the
    /// operator is better served by a row that still identifies its host than by a blank column.
    /// </summary>
    public string? Label(int? id)
    {
        if (id == null) return null;

        lock (_gate)
        {
            return _labels.TryGetValue(id.Value, out var label) ? label : id.Value.ToString();
        }
    }

    /// <summary>How many ids have been resolved.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _labels.Count;
            }
        }
    }
}
