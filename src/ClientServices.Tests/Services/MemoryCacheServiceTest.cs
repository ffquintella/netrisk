using System;
using System.Collections.Generic;
using ClientServices.Interfaces;
using ClientServices.Services;
using DAL.Entities;
using JetBrains.Annotations;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers the client-side cache: storage keyed by type + name, removal, presence checks, and
/// expiry.
///
/// Expiry is asserted with an already-elapsed lifetime rather than by waiting on the clock — a
/// negative <see cref="TimeSpan"/> puts the entry's expiry in the past the instant it is stored, so
/// the assertions are deterministic and cost no wall time. Eviction is lazy and synchronous, so
/// the first read after expiry is the one that has to answer "absent"; there is no sweep to wait
/// for.
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
        // null! is the point of the test: the guard against caching a null is what is under
        // test, and the parameter is non-nullable precisely so a caller has to mean it.
        _cache.Set<Risk>("risk-1", null!);

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

    // ---------------------------------------------------------------- expiry

    /// <summary>An expiry already in the past when the entry is stored — nothing here waits.</summary>
    private static readonly TimeSpan AlreadyElapsed = TimeSpan.FromMinutes(-1);

    [Fact]
    public void TestGetDoesNotServeAnExpiredEntry()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, AlreadyElapsed);

        Assert.Null(_cache.Get<Risk>("risk-1"));
    }

    [Fact]
    public void TestHasCacheIsFalseForAnExpiredEntry()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, AlreadyElapsed);

        Assert.False(_cache.HasCache<Risk>("risk-1"));
    }

    [Fact]
    public void TestHasCacheWithWildcardIsFalseWhenEveryEntryOfTheTypeHasExpired()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, AlreadyElapsed);
        _cache.Set("risk-2", new Risk { Id = 2 }, AlreadyElapsed);

        Assert.False(_cache.HasCache<Risk>("*"));
    }

    [Fact]
    public void TestHasCacheWithWildcardIsTrueWhileOneEntryOfTheTypeIsStillLive()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, AlreadyElapsed);
        _cache.Set("risk-2", new Risk { Id = 2 }, TimeSpan.FromMinutes(5));

        Assert.True(_cache.HasCache<Risk>("*"));
        Assert.Null(_cache.Get<Risk>("risk-1"));
        Assert.Equal(2, _cache.Get<Risk>("risk-2")!.Id);
    }

    [Fact]
    public void TestAnEntryInsideItsLifetimeSurvives()
    {
        _cache.Set("risk-1", new Risk { Id = 1, Subject = "live" }, TimeSpan.FromMinutes(5));

        Assert.True(_cache.HasCache<Risk>("risk-1"));
        Assert.True(_cache.HasCache<Risk>("*"));
        Assert.Equal("live", _cache.Get<Risk>("risk-1")!.Subject);
    }

    [Fact]
    public void TestAZeroLifetimeExpiresImmediately()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, TimeSpan.Zero);

        Assert.False(_cache.HasCache<Risk>("risk-1"));
        Assert.Null(_cache.Get<Risk>("risk-1"));
    }

    [Fact]
    public void TestExpiryIsPerEntryNotPerType()
    {
        _cache.Set("stale", new Risk { Id = 1 }, AlreadyElapsed);
        _cache.Set("fresh", new Risk { Id = 2 }, TimeSpan.FromMinutes(5));

        Assert.Null(_cache.Get<Risk>("stale"));
        Assert.Equal(2, _cache.Get<Risk>("fresh")!.Id);
    }

    [Fact]
    public void TestSetRevivesAnExpiredKey()
    {
        _cache.Set("risk-1", new Risk { Id = 1, Subject = "stale" }, AlreadyElapsed);
        Assert.Null(_cache.Get<Risk>("risk-1"));

        _cache.Set("risk-1", new Risk { Id = 1, Subject = "fresh" }, TimeSpan.FromMinutes(5));

        Assert.Equal("fresh", _cache.Get<Risk>("risk-1")!.Subject);
    }

    [Fact]
    public void TestExpiryOfOneTypeLeavesAnotherTypeAlone()
    {
        _cache.Set("shared", new Risk { Id = 1 }, AlreadyElapsed);
        _cache.Set("shared", new Host { Id = 9 }, TimeSpan.FromMinutes(5));

        Assert.False(_cache.HasCache<Risk>("shared"));
        Assert.Equal(9, _cache.Get<Host>("shared")!.Id);
    }

    /// <summary>
    /// A read of an expired entry drops it, and the default lifetime still applies to whatever the
    /// same type caches afterwards.
    /// </summary>
    [Fact]
    public void TestTheDefaultLifetimeIsNotExpired()
    {
        _cache.Set("risk-1", new Risk { Id = 1 }, AlreadyElapsed);
        Assert.Null(_cache.Get<Risk>("risk-1"));

        _cache.Set("risk-2", new Risk { Id = 2 });

        Assert.True(_cache.HasCache<Risk>("risk-2"));
        Assert.Equal(2, _cache.Get<Risk>("risk-2")!.Id);
    }
}
