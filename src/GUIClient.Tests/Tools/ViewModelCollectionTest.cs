using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The five view-models that used <c>Parallel.ForEach</c> all materialize UI collections through
/// this policy. The large inputs make a lost item, duplicate or unstable output order visible
/// without depending on a probabilistic race in the test itself.
/// </summary>
public class ViewModelCollectionTest
{
    [Fact]
    public void MaterializeKeepsEveryItemOnceAndInSourceOrder()
    {
        var source = Enumerable.Range(0, 10_000).ToArray();

        var result = ViewModelCollection.Materialize(source, value => $"item-{value}");

        Assert.Equal(source.Length, result.Count);
        Assert.Equal("item-0", result[0]);
        Assert.Equal("item-9999", result[^1]);
        Assert.Equal(source.Length, result.Distinct().Count());
        Assert.Equal(source.Select(value => $"item-{value}"), result);
    }

    [Fact]
    public async Task MaterializeAsyncKeepsEveryItemInSourceOrderAndHonorsTheConcurrencyLimit()
    {
        var source = Enumerable.Range(0, 200).ToArray();
        var running = 0;
        var observedMaximum = 0;

        var result = await ViewModelCollection.MaterializeAsync(source, async value =>
        {
            var current = Interlocked.Increment(ref running);
            InterlockedExtensions.Max(ref observedMaximum, current);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds((source.Length - value) % 7 + 1));
                return value * 3;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }, maxDegreeOfParallelism: 3);

        Assert.Equal(source.Select(value => value * 3), result);
        Assert.InRange(observedMaximum, 2, 3);
    }

    [Fact]
    public void HaveSameMembersIgnoresOrderButDetectsMissingAndDuplicateItems()
    {
        Assert.True(ViewModelCollection.HaveSameMembers(new[] { 3, 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(ViewModelCollection.HaveSameMembers(new[] { 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(ViewModelCollection.HaveSameMembers(new[] { 1, 1, 2 }, new[] { 1, 2, 2 }));
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            var observed = Volatile.Read(ref target);
            while (observed < candidate)
            {
                var original = Interlocked.CompareExchange(ref target, candidate, observed);
                if (original == observed) return;
                observed = original;
            }
        }
    }
}
