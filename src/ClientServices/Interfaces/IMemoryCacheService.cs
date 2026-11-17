namespace ClientServices.Interfaces;

public interface IMemoryCacheService
{
    public void Set<T>(string key, T value, TimeSpan? timeSpan = null);
    public T? Get<T>(string key);
    public void Remove<T>(string key);
    public bool HasCache<T>(string key);

    /// <summary>
    /// Drops every entry of every type. Called when the signed-in session changes (<c>SessionCacheReset</c>): nothing a
    /// previous session read may be served to the next one.
    /// </summary>
    public void Clear();
}