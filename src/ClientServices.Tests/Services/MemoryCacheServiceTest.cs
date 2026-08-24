using System;
using System.Collections.Generic;
using ClientServices.Interfaces;
using ClientServices.Services;
using DAL.Entities;
using JetBrains.Annotations;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers the deterministic behaviour of the client-side cache: storage keyed by type + name,
/// removal, and presence checks.
///
/// Expiry is deliberately not asserted. <c>Get</c> never compares the stored expiry against the
/// clock itself — eviction happens only in <c>CleanCacheAsync</c>, which is <c>async void</c> over a
/// <c>Task.Run</c> and therefore races the caller. Any expiry assertion would be flaky, so the
/// behaviour is reported as a defect rather than pinned down here.
/// </summary>
[TestSubject(typeof(MemoryCacheService))]
public class MemoryCacheServiceTest
{
    private readonly IMemoryCacheService _cache = new MemoryCacheService();

    [Fact]
    public void TestSetThenGetRoundTrips()
    {
        _cache.Set("risk-1", new Risk { Id = 1, Subject = "S1" });

        var cached = _cache.Get<Risk>("risk-1");

        Assert.NotNull(cached);
        Assert.Equal(1, cached.Id);
        Assert.Equal("S1", cached.Subject);
    }

    [Fact]
    public void TestGetReturnsDefaultForAnUnknownKey()
    {
        _cache.Set("risk-1", new Risk { Id = 1 });

        Assert.Null(_cache.Get<Risk>("risk-2"));
    }

    [Fact]
    public void TestGetReturnsDefaultForATypeNeverCached()
    {
        Assert.Null(_cache.Get<Risk>("anything"));
    }

    [Fact]
    public void TestEntriesAreKeyedByTypeAsWellAsName()
    {
        _cache.Set("shared", new Risk { Id = 1 });
        _cache.Set("shared", new Host { Id = 9 });

        Assert.Equal(1, _cache.Get<Risk>("shared")!.Id);
        Assert.Equal(9, _cache.Get<Host>("shared")!.Id);
    }

    [Fact]
    public void TestSetOverwritesTheSameKey()
    {
        _cache.Set("risk-1", new Risk { Id = 1, Subject = "first" });
        _cache.Set("risk-1", new Risk { Id = 1, Subject = "second" });

        Assert.Equal("second", _cache.Get<Risk>("risk-1")!.Subject);
    }

    [Fact]
    public void TestSetIgnoresANullValue()
    {
        _cache.Set<Risk>("risk-1", null);

        Assert.False(_cache.HasCache<Risk>("risk-1"));
    }

    [Fact]
    public void TestSetAcceptsAnExplicitLifetime()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, TimeSpan.FromHours(2));

        Assert.True(_cache.HasCache<Risk>("risk-1"));
    }

    [Fact]
    public void TestHasCacheIsFalseBeforeAnythingIsStored()
    {
        Assert.False(_cache.HasCache<Risk>("risk-1"));
    }

    [Fact]
    public void TestHasCacheWithWildcardReportsWhetherTheTypeHasAnyEntry()
    {
        Assert.False(_cache.HasCache<Risk>("*"));

        _cache.Set("risk-1", new Risk { Id = 1 });

        Assert.True(_cache.HasCache<Risk>("*"));
    }

    [Fact]
    public void TestRemoveDropsOnlyTheNamedKey()
    {
        _cache.Set("risk-1", new Risk { Id = 1 });
        _cache.Set("risk-2", new Risk { Id = 2 });

        _cache.Remove<Risk>("risk-1");

        Assert.False(_cache.HasCache<Risk>("risk-1"));
        Assert.True(_cache.HasCache<Risk>("risk-2"));
    }

    [Fact]
    public void TestRemoveWithWildcardClearsEveryEntryOfThatType()
    {
        _cache.Set("risk-1", new Risk { Id = 1 });
        _cache.Set("risk-2", new Risk { Id = 2 });
        _cache.Set("host-1", new Host { Id = 1 });

        _cache.Remove<Risk>("*");

        Assert.Null(_cache.Get<Risk>("risk-1"));
        Assert.Null(_cache.Get<Risk>("risk-2"));
        Assert.True(_cache.HasCache<Host>("host-1"));
    }

    [Fact]
    public void TestRemoveIsANoOpForATypeNeverCached()
    {
        _cache.Remove<Risk>("risk-1");

        Assert.False(_cache.HasCache<Risk>("risk-1"));
    }

    [Fact]
    public void TestListsAreCachedAsTheirOwnType()
    {
        _cache.Set("all", new List<Risk> { new() { Id = 1 }, new() { Id = 2 } });

        var cached = _cache.Get<List<Risk>>("all");

        Assert.NotNull(cached);
        Assert.Equal(2, cached.Count);
        Assert.False(_cache.HasCache<Risk>("all"));
    }
}
