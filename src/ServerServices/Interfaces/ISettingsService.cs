namespace ServerServices.Interfaces;

public interface ISettingsService
{
    /// <summary>
    /// Change the backup password
    /// </summary>
    /// <param name="password"></param>
    public void ChangeBackupPasswordAsync(string password);
    
    /// <summary>
    /// Check the configuration key exists
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public Task<bool> ConfigurationKeyExistsAsync(string key);
    
    /// <summary>
    /// Get the configuration key value
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public Task<string> GetConfigurationKeyValueAsync(string key);
}