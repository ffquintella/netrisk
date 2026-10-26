using System.Text;
using DAL.Context;
using Microsoft.Extensions.Caching.Memory;
using ServerServices.Security;
using Tools.Security;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// The index's read cache (S40 §8.1, §11.3): an entry is keyed by what can change the payload — the endpoint and its
/// arguments, the resolved date and profile, the caller's <b>expanded</b> scope with the hierarchy's revision, and the
/// module permissions redaction consults. Two callers who differ in any of these never share an entry: that is the rule
/// whose absence made the Master Dashboard's cache (one process-wide entry for every caller; retired by T294) the
/// anti-example.
/// </summary>
public interface IRiskIndexReadCache
{
    /// <summary>The scope part of a key: <c>global</c>, or the SHA-256 of the sorted expanded entity list and the tree revision.</summary>
    string ScopeKey(EntityScope scope);

    Task<T> GetOrCreateAsync<T>(string key, TimeSpan timeToLive, Func<Task<T>> factory) where T : class;

    /// <summary>Drops every entry — for a mutation the key cannot see (a profile approved, the publication changed).</summary>
    void Clear();
}

public sealed class RiskIndexReadCache : IRiskIndexReadCache, IDisposable
{
    /// <summary>Snapshot and derived values: the date is in the key, so a new snapshot never collides with an old one.</summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromHours(24);

    /// <summary>"Latest snapshot" is resolved first, with a short life (S40 §8.1).</summary>
    public static readonly TimeSpan LatestTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>Entries, not bytes: each is one response DTO.</summary>
    public const long DefaultSizeLimit = 4096;

    private readonly IEntityHierarchy? _hierarchy;
    private MemoryCache _cache;
    private readonly object _gate = new();

    /// <param name="hierarchy">The host's tree cache (T292): its revision enters every scoped key, so moving a subtree out of
    /// a unit misses the cache at once, without waiting for the entry to expire. Absent in hosts that do not register it.</param>
    public RiskIndexReadCache(IEntityHierarchy? hierarchy = null)
    {
        _hierarchy = hierarchy;
        _cache = NewCache();
    }

    private static MemoryCache NewCache() => new(new MemoryCacheOptions { SizeLimit = DefaultSizeLimit });

    public string ScopeKey(EntityScope scope)
    {
        if (scope.IsUnrestricted) return "global";
        var text = new StringBuilder();
        foreach (var id in scope.EntityIds.Order()) text.Append(id).Append(',');
        text.Append("|rev=").Append(_hierarchy?.Revision ?? 0);
        return HashTool.CreateSha256(text.ToString());
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan timeToLive, Func<Task<T>> factory) where T : class
    {
        MemoryCache cache;
        lock (_gate) cache = _cache;

        if (cache.TryGetValue(key, out var hit) && hit is T value) return value;

        var created = await factory();
        cache.Set(key, created, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive, Size = 1 });
        return created;
    }

    /// <summary>Swaps in an empty cache; the old one is left to the collector, since a reader may still hold it.</summary>
    public void Clear()
    {
        lock (_gate) _cache = NewCache();
    }

    public void Dispose()
    {
        lock (_gate) _cache.Dispose();
    }
}
