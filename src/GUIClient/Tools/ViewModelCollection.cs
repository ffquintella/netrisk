using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GUIClient.Tools;

/// <summary>
/// Materializes data for view-model collections without allowing worker threads to mutate a shared
/// <see cref="List{T}"/> or <c>ObservableCollection</c>. Async projections may still overlap their
/// I/O; <see cref="Task.WhenAll(IEnumerable{Task})"/> returns their results in source order.
/// </summary>
public static class ViewModelCollection
{
    public static List<TResult> Materialize<TSource, TResult>(IEnumerable<TSource> source,
        Func<TSource, TResult> projector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projector);

        var result = new List<TResult>();
        foreach (var item in source)
            result.Add(projector(item));

        return result;
    }

    public static async Task<List<TResult>> MaterializeAsync<TSource, TResult>(IEnumerable<TSource> source,
        Func<TSource, Task<TResult>> projector, int maxDegreeOfParallelism = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        var items = source.ToList();
        var result = new List<TResult>(items.Count);

        for (var index = 0; index < items.Count; index += maxDegreeOfParallelism)
        {
            var batch = items.Skip(index).Take(maxDegreeOfParallelism).Select(projector);
            result.AddRange(await Task.WhenAll(batch));
        }

        return result;
    }

    public static bool HaveSameMembers<T>(IEnumerable<T> first, IEnumerable<T> second)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var counts = new Dictionary<T, int>();
        foreach (var item in first)
            counts[item] = counts.GetValueOrDefault(item) + 1;

        foreach (var item in second)
        {
            if (!counts.TryGetValue(item, out var count)) return false;
            if (count == 1) counts.Remove(item);
            else counts[item] = count - 1;
        }

        return counts.Count == 0;
    }
}
