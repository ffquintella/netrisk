using System;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>T254 (S40 §10.9): the last 20 visited (node, date) pairs come back without a request, least recently used out first.</summary>
[TestSubject(typeof(RecentCache<,>))]
public class RecentCacheTest
{
    [Fact]
    public void TestTheCacheKeepsTwentyByDefault()
    {
        var cache = new RecentCache<(string Node, DateOnly Day), int>();
        var day = new DateOnly(2026, 10, 5);

        for (var i = 0; i < 25; i++) cache.Put(("e:" + i, day), i);

        Assert.Equal(RecentCache<(string, DateOnly), int>.DefaultCapacity, cache.Count);
        Assert.False(cache.TryGet(("e:4", day), out _));
        Assert.True(cache.TryGet(("e:5", day), out var kept));
        Assert.Equal(5, kept);
        Assert.False(cache.TryGet(("e:5", day.AddDays(-1)), out _));
    }

    [Fact]
    public void TestReadingAnEntryKeepsItAndReplacingItDoesNotGrow()
    {
        var cache = new RecentCache<string, string>(2);
        cache.Put("org", "a");
        cache.Put("e:7", "b");

        Assert.True(cache.TryGet("org", out _));
        cache.Put("e:9", "c");

        Assert.True(cache.TryGet("org", out var org));
        Assert.Equal("a", org);
        Assert.False(cache.TryGet("e:7", out _));

        cache.Put("org", "z");
        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("org", out var replaced));
        Assert.Equal("z", replaced);
    }

    [Fact]
    public void TestClearEmptiesItAndACapacityBelowOneIsRefused()
    {
        var cache = new RecentCache<string, int>();
        cache.Put("org", 1);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("org", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecentCache<string, int>(0));
    }
}
