using Model.FaceID;
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
    /// <param name="loggedUserId"></param>
    /// <returns></returns>
    public Task SetUserEnabledStatusAsync(int userId , bool enabled, int loggedUserId);
    
    /// <summary>
    /// Save the face id for the user creating the face descriptor from the image
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="faceData"></param>
    /// <param name="loggedUserId"></param>
    /// <returns></returns>
    public Task SaveFaceIdAsync(int userId, FaceData faceData, int loggedUserId);
}