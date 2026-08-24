using ClientServices.Interfaces;

namespace ClientServices.Services;

/// <summary>
/// Cache keyed by type plus name, with a per-entry lifetime.
///
/// Eviction is lazy and synchronous: every read checks the entry it found against the clock and
/// drops it if it is past its expiry, so an expired entry is never served and there is no
/// background sweep that could mutate the store while a caller reads it. Nothing here needs eager
/// eviction — an entry nobody asks for costs only the memory it already occupies.
///
/// Every method takes <see cref="_gate"/>: the service is a singleton, and reads mutate.
/// </summary>
public class MemoryCacheService: IMemoryCacheService
{
    private readonly Dictionary<Type, Dictionary<string, Tuple<object, DateTime>>> _internalCache = new();

    private readonly object _gate = new();

    private const int DefaultTimeSpan = 60;

    public void Set<T>(string key, T value, TimeSpan? timeSpan = null)
    {
        if(value is null) return;

        var expirationTimeSpan = timeSpan ?? TimeSpan.FromMinutes(DefaultTimeSpan);
        var expirationDateTime = DateTime.UtcNow + expirationTimeSpan;

        lock (_gate)
        {
            if (!_internalCache.TryGetValue(typeof(T), out var typeCache))
            {
                typeCache = new Dictionary<string, Tuple<object, DateTime>>();
                _internalCache.Add(typeof(T), typeCache);
            }

            typeCache[key] = new Tuple<object, DateTime>(value, expirationDateTime);
        }
    }

    public T? Get<T>(string key)
    {
        lock (_gate)
        {
            if (!_internalCache.TryGetValue(typeof(T), out var typeCache)) return default;

            if (!typeCache.TryGetValue(key, out var entry)) return default;

            if (HasExpired(entry))
            {
                Evict(typeof(T), key, typeCache);
                return default;
            }

            return (T) entry.Item1;
        }
    }

    public void Remove<T>(string key)
    {
        lock (_gate)
        {
            if (key == "*")
            {
                _internalCache.Remove(typeof(T));
                return;
            }

            if (!_internalCache.TryGetValue(typeof(T), out var typeCache)) return;

            Evict(typeof(T), key, typeCache);
        }
    }

    public bool HasCache<T>(string key)
    {
        lock (_gate)
        {
            if (!_internalCache.TryGetValue(typeof(T), out var typeCache)) return false;

            if (key == "*")
            {
                PruneExpired(typeof(T), typeCache);
                return typeCache.Count > 0;
            }

            if (!typeCache.TryGetValue(key, out var entry)) return false;

            if (!HasExpired(entry)) return true;

            Evict(typeof(T), key, typeCache);
            return false;
        }
    }

    private static bool HasExpired(Tuple<object, DateTime> entry) => DateTime.UtcNow >= entry.Item2;

    /// <summary>Drops one entry, and the type bucket with it once that bucket runs empty.</summary>
    private void Evict(Type type, string key, Dictionary<string, Tuple<object, DateTime>> typeCache)
    {
        typeCache.Remove(key);

        if (typeCache.Count == 0) _internalCache.Remove(type);
    }

    /// <summary>
    /// Drops every expired entry of one type. Only the wildcard check needs this — it answers about
    /// the bucket as a whole, so a bucket holding nothing but expired entries has to read as empty.
    /// </summary>
    private void PruneExpired(Type type, Dictionary<string, Tuple<object, DateTime>> typeCache)
    {
        foreach (var expired in typeCache.Where(entry => HasExpired(entry.Value)).Select(entry => entry.Key).ToList())
        {
            typeCache.Remove(expired);
        }

        if (typeCache.Count == 0) _internalCache.Remove(type);
    }
}
