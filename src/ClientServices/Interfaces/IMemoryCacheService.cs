namespace ClientServices.Interfaces;

public interface IMemoryCacheService
{
    public void Set<T>(string key, T value, TimeSpan? timeSpan = null);
    public T? Get<T>(string key);
    public void Remove<T>(string key);
    public bool HasCache<T>(string key);
}