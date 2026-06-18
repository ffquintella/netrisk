using Model.DTO;

namespace ClientServices.Interfaces;

public interface IConfigurationsService
{
    /// <summary>
    /// Verify if the backup password is set
    /// </summary>
    /// <returns></returns>
    public bool BackupPasswordIsSet();

    /// <summary>
    /// Set the backup password
    /// </summary>
    /// <param name="password"></param>
    public void SetBackupPassword(string password);

    /// <summary>Gets the website sync configuration (intervals, URL).</summary>
    public WebsiteSyncConfigDto GetWebsiteSyncConfig();

    /// <summary>Saves the website sync configuration.</summary>
    public void SetWebsiteSyncConfig(WebsiteSyncConfigDto config);
}