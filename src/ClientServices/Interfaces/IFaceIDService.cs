using Model.FaceID;
using Model.Services;

namespace ClientServices.Interfaces;

public interface IFaceIDService
{
    /// <summary>
    /// Gets the information about the service
    /// </summary>
    /// <returns></returns>
    public Task<ServiceInformation> GetInfo();
    
    /// <summary>
    /// Check if the user is enabled to use the faceid service
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<bool> IsUserEnabledAsync(int userId);
    
    /// <summary>
    /// Check if the user has a face set
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<bool> UserHasFaceSetAsync(int userId);
    
    /// <summary>
    /// Set the user enabled status
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="enabled"></param>
    /// <returns></returns>
    public Task SetUserEnabledStatusAsync(int userId , bool enabled);
    
    /// <summary>
    /// Saves the FaceID image to the database.
    /// </summary>
    /// <param name="userId">The ID of the user for whom the image is being saved.</param>
    /// <param name="imageData">The image data to be saved.</param>
    /// <param name="imageType">The type of the image (e.g., SKBitmap).</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the identifier of the saved image.</returns>
    public Task<string> SaveAsync(int userId, string imageData, string imageType);

    /// <summary>
    /// Saves the FaceID image to the database.
    /// </summary>
    /// <param name="userId">The ID of the user for whom the image is being saved.</param>
    /// <param name="imageData">The image data to be saved.</param>
    /// <param name="imageType">The type of the image (e.g., SKBitmap).</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the identifier of the saved image.</returns>
    public Task<string> SaveAsync(int userId, string imageJson);


    /// <summary>
    /// Retrieves face transaction data for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user for whom the face transaction data is being retrieved.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the face transaction data for the specified user.</returns>
    public Task<FaceTransactionData?> GetFaceTransactionDataAsync(int userId);

    /// <summary>
    /// Commits a FaceID transaction for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user for whom the transaction is being committed.</param>
    /// <param name="faceTData">The data associated with the FaceID transaction.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the FaceToken resulting from the transaction.</returns>
    public Task<FaceToken> CommitTransactionAsync(int userId, FaceTransactionData faceTData);
}