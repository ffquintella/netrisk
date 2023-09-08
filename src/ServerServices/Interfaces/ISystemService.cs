using Model.ClientData;

namespace ServerServices.Interfaces;

public interface ISystemService
{
    /// <summary>
    /// Get´s the client configuration from the disk
    /// </summary>
    /// <returns></returns>
    public Task<ClientInformation> GetClientInformation();
    
    /// <summary>
    ///  Get´s the update script from the disk
    /// </summary>
    /// <param name="osFamily"></param>
    /// <returns></returns>
    public Task<string> GetUpdateScriptAsync(string osFamily);
}