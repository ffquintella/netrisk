using ClientServices.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace ClientServices.Services;

public class MemoryCacheService: IMemoryCacheService
{
    private Dictionary<Type,Dictionary<string, Tuple<object, DateTime>>> _internalCache = new();
    
    private const int DefaultTimeSpan = 60;
    
    public void Set<T>(string key, T value, TimeSpan? timeSpan = null)
    {
        if(value is null) return;
        
        if (!_internalCache.ContainsKey(typeof(T)))
        {
            _internalCache.Add(typeof(T), new Dictionary<string, Tuple<object, DateTime>>());
        }

        var expirationTimeSpan = timeSpan ?? TimeSpan.FromMinutes(DefaultTimeSpan);
        var expirationDateTime = DateTime.Now + expirationTimeSpan;
        
        _internalCache[typeof(T)][key] = new Tuple<object, DateTime>(value, expirationDateTime);;
        
    }

    public T? Get<T>(string key)
    {
        
        CleanCacheAsync();
        
        if (!_internalCache.ContainsKey(typeof(T)))
        {
            return default;
        }

        if (!_internalCache[typeof(T)].ContainsKey(key))
        {
            return default;
        }

        var tuple = _internalCache[typeof(T)][key];

        return (T) tuple.Item1;
    }

    public void Remove<T>(string key)
    {
        CleanCacheAsync();
        if (_internalCache.ContainsKey(typeof(T)))
        {
            if (key == "*") _internalCache[typeof(T)].Clear();
            else _internalCache[typeof(T)].Remove(key);
        }
    }

    public bool HasCache<T>(string key)
    {
        CleanCacheAsync();
        if(key == "*") return _internalCache.ContainsKey(typeof(T));
        return _internalCache.ContainsKey(typeof(T)) && _internalCache[typeof(T)].ContainsKey(key);
    }
    
    private async void CleanCacheAsync()
    {
        await Task.Run(() =>
        {
            foreach (var typeCache in _internalCache)
            {
                if (typeCache.Value.Count == 0)
                {
                    _internalCache.Remove(typeCache.Key);
                }
                else
                {
                    foreach (var keyCache in typeCache.Value)
                    {
                        if (DateTime.Now - keyCache.Value.Item2 > TimeSpan.Zero)
                        {
                            _internalCache[typeCache.Key].Remove(keyCache.Key);
                        }
                    }
                }
            }
        });
    }
}