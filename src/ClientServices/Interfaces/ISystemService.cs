namespace ClientServices.Interfaces;

public interface ISystemService
{
    /// <summary>
    /// Compares the client assembly version with the version provided by the server and returns true if the client needs to be upgraded
    /// </summary>
    /// <returns></returns>
    public bool NeedsUpgrade();
    
    /// <summary>
    /// Gets the client assembly version
    /// </summary>
    /// <returns></returns>
    public string GetClientAssemblyVersion();
    
    /// <summary>
    /// Gets the system Temporary path
    /// </summary>
    /// <returns></returns>
    public string GetTempPath();
    
    /// <summary>
    /// Gets the name of the current OS
    /// </summary>
    /// <returns></returns>
    public string GetCurrentOsName();
    
    /// <summary>
    /// Downloads the upgrade script to the tempo folder
    /// </summary>
    public void DownloadUpgradeScript();
    
    /// <summary>
    /// Download the application to the temp folder
    /// </summary>
    public void DownloadApplication();
}