using Model.Services;

namespace ClientServices.Interfaces;

public interface IFaceIDService
{
    /// <summary>
    /// Gets the information about the service
    /// </summary>
    /// <returns></returns>
    public Task<ServiceInformation> GetInfo();
}