using Model.FaceID;
using Model.Services;

namespace ServerServices.Interfaces;

public interface IFaceIDService
{
    /// <summary>
    /// Retrieves information about the FaceID service.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the service information.</returns>
    public Task<ServiceInformation> GetInfoAsync();
    
    /// <summary>
    /// Checks whether the user is enabled to use the FaceID service.
    /// </summary>
    /// <param name="userId">The ID of the user to check.</param>
    /// <returns>Returns a boolean indicating whether the user is enabled.</returns>
    public Task<bool> IsUserEnabledAsync(int userId);
    
    
    /// <summary>
    /// Checks if the user has a registered face set.
    /// </summary>
    /// <param name="userId">The ID of the user to check.</param>
    /// <returns>Returns a boolean indicating whether the user has a registered face set.</returns>
    public Task<bool> UserHasFaceSetAsync(int userId);
    
    ///<summary>
    /// Sets the enabled status for a user in the FaceID service.
    /// </summary>
    /// <param name="userId">The ID of the user whose status is being updated.</param>
    /// <param name="enabled">A boolean indicating whether the user should be enabled or disabled.</param>
    /// <param name="loggedUserId">The ID of the user performing the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetUserEnabledStatusAsync(int userId , bool enabled, int loggedUserId);

    /// <summary>
    /// Saves the face ID for the specified user by creating a face descriptor from the provided image data.
    /// </summary>
    /// <param name="userId">The ID of the user for whom the face ID is being saved.</param>
    /// <param name="faceData">The data containing the face image and associated information.</param>
    /// <param name="loggedUserId">The ID of the user performing the operation.</param>
    /// <returns>Returns a string representing the generated face descriptor.</returns>
    public Task<string> SaveFaceIdAsync(int userId, FaceData faceData, int loggedUserId);

    /// <summary>
    /// Starts a face recognition transaction for the specified user.
    /// </summary>
    /// <param name="userId">The identifier of the user for whom the transaction is being initiated.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains data related to the face transaction.</returns>
    public Task<FaceTransactionData> StartTransactionAsync(int userId);
}