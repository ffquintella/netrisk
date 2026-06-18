using Model.DTO;

namespace ServerServices.Interfaces;

public interface IConfigurationsService
{
    public void UpdateBackupPassword(string password);

    public string GetBackupPassword();

    /// <summary>Reads the website sync configuration (intervals, URL, insecure flag) from settings.</summary>
    public WebsiteSyncConfigDto GetWebsiteSyncConfig();

    /// <summary>Persists the website sync configuration to settings.</summary>
    public void UpdateWebsiteSyncConfig(WebsiteSyncConfigDto config);
}
