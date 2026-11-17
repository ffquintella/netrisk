using System;
using System.Collections.Generic;
using ClientServices.Interfaces;
using ClientServices.Services;
using DAL.Entities;
using JetBrains.Annotations;
using NSubstitute;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// M62 security review: the client's read caches are process-wide, so a session change must empty them — every
/// sign-in and every expired session — or the next user of the process is answered with the previous user's data.
/// </summary>
[TestSubject(typeof(SessionCacheReset))]
public class SessionCacheResetTest
{
    private readonly IMemoryCacheService _cache = new MemoryCacheService();
    private readonly IHostsService _hosts = Substitute.For<IHostsService>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();

    [Fact]
    public void TestASignInClearsEveryCache()
    {
        new SessionCacheReset(_cache, _hosts).Attach(_authentication);
        _cache.Set("All", new List<Entity> { new() { Id = 1 } });

        _authentication.AuthenticationSucceeded += Raise.Event();

        Assert.False(_cache.HasCache<List<Entity>>("All"));
        _hosts.Received(1).ClearCache();
    }

    [Fact]
    public void TestAnExpiredSessionClearsEveryCache()
    {
        new SessionCacheReset(_cache, _hosts).Attach(_authentication);
        _cache.Set("All", new List<Entity> { new() { Id = 1 } });

        _authentication.SessionExpired += Raise.Event();

        Assert.False(_cache.HasCache<List<Entity>>("All"));
        _hosts.Received(1).ClearCache();
    }

    [Fact]
    public void TestAttachingTwiceClearsOnce()
    {
        var reset = new SessionCacheReset(_cache, _hosts);
        reset.Attach(_authentication);
        reset.Attach(_authentication);

        _authentication.AuthenticationSucceeded += Raise.Event();

        _hosts.Received(1).ClearCache();
    }

    [Fact]
    public void TestNothingIsClearedBeforeASessionChange()
    {
        new SessionCacheReset(_cache, _hosts).Attach(_authentication);
        _cache.Set("All", new List<Entity> { new() { Id = 1 } });

        Assert.True(_cache.HasCache<List<Entity>>("All"));
        _hosts.DidNotReceive().ClearCache();
    }
}
