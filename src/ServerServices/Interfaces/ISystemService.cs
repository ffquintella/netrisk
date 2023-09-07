using Model.ClientData;

namespace ServerServices.Interfaces;

public interface ISystemService
{
    /// <summary>
    /// Get´s the client configuration from the disk
    /// </summary>
    /// <returns></returns>
    public Task<ClientInformation> GetClientInformation();
}