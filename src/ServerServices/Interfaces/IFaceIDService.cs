using Model.Services;

namespace ServerServices.Interfaces;

public interface IFaceIDService
{
    /// <summary>
    /// Gets the information about the service
    /// </summary>
    /// <returns></returns>
    public Task<ServiceInformation> GetInfoAsync();
    
    /// <summary>
    /// Check if the user is enabled to use the faceid service
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<bool> IsUserEnabledAsync(int userId);
    
    /// <summary>
    /// Set the user enabled status
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="enabled"></param>
    /// <returns></returns>
    public Task SetUserEnabledStatusAsync(int userId , bool enabled);
}