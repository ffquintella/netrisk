using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// Covers the map that replaced the findings grid's per-cell foreign-key lookups.
///
/// The behaviour that matters here is not the dictionary — it is <see cref="RowLabelCache.Missing"/>
/// returning each unresolved id exactly once. That is what bounds the number of requests a page of
/// findings costs, and its absence is what let one page of twenty rows issue ninety blocking
/// <c>/Teams/{id}</c> and <c>/Hosts/{id}</c> calls on the UI thread.
/// </summary>
public class RowLabelCacheTest
{
    [Fact]
    public void MissingReportsEachUnresolvedIdOnce()
    {
        var cache = new RowLabelCache();

        // The shape of a real page: the same fix team on most rows, a couple of others.
        var ids = new int?[] { 1, 26, 1, 1, 26, 2, 26, 1 };

        var missing = cache.Missing(ids);

        Assert.Equal([1, 26, 2], missing);
    }

    [Fact]
    public void MissingIgnoresRowsWithNoId()
    {
        var cache = new RowLabelCache();

        var missing = cache.Missing([null, 7, null, 7]);

        Assert.Equal([7], missing);
    }

    [Fact]
    public void MissingSkipsIdsAlreadyResolved()
    {
        var cache = new RowLabelCache();
        cache.Set(1, "Infra (1)");

        Assert.Equal([26], cache.Missing([1, 26, 1]));

        cache.Set(26, "AppSec (26)");

        // Paging back to a page that has been seen must cost no request at all.
        Assert.Empty(cache.Missing([1, 26, 1, 26]));
    }

    [Fact]
    public void MissingToleratesNoRows()
    {
        var cache = new RowLabelCache();

        Assert.Empty(cache.Missing(null));
        Assert.Empty(cache.Missing([]));
    }

    [Fact]
    public void StillMissingNarrowsASetOfKnownIds()
    {
        // The overload used to work out what a bulk listing did not cover: /Users/Listings returns
        // only enabled accounts, so a finding assigned to a deactivated analyst is left over and
        // still needs naming.
        var cache = new RowLabelCache();
        cache.Set(4, "Ana (4)");
        cache.Set(9, "Bruno (9)");

        Assert.Equal([7], cache.StillMissing([4, 7, 9, 7]));
        Assert.Empty(cache.StillMissing([4, 9]));
        Assert.Empty(cache.StillMissing(null));
    }

    [Fact]
    public void LabelIsNullWhenTheRowHasNoId()
    {
        Assert.Null(new RowLabelCache().Label(null));
    }

    [Fact]
    public void LabelIsTheResolvedTextOnceKnown()
    {
        var cache = new RowLabelCache();
        cache.Set(26, "AppSec (26)");

        Assert.Equal("AppSec (26)", cache.Label(26));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void LabelFallsBackToTheBareIdWhileUnresolved()
    {
        // The prefetch has not landed, or the server refused it. The column still identifies the
        // row rather than going blank — and, unlike the converters this replaced, asking again is
        // free and logs nothing.
        Assert.Equal("26", new RowLabelCache().Label(26));
    }

    [Fact]
    public void SetOverwritesAStaleLabel()
    {
        var cache = new RowLabelCache();
        cache.Set(3, "Old name (3)");
        cache.Set(3, "New name (3)");

        Assert.Equal("New name (3)", cache.Label(3));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task ReadsAndWritesFromDifferentThreadsDoNotCorruptTheMap()
    {
        // A page assignment starts its refresh off the UI thread while the UI thread is still
        // rendering the previous page, so reads and writes genuinely overlap.
        var cache = new RowLabelCache();

        var writer = Task.Run(() =>
        {
            for (var id = 0; id < 2000; id++) cache.Set(id, $"host-{id} ({id})");
        });

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                Assert.NotNull(cache.Label(i % 2000));
                _ = cache.Missing(Enumerable.Range(0, 50).Select(n => (int?)n).ToList());
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(2000, cache.Count);
        Assert.Equal("host-1999 (1999)", cache.Label(1999));
    }
}
