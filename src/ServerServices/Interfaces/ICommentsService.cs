using DAL.Entities;

namespace ServerServices.Interfaces;

public interface ICommentsService
{
    /// <summary>
    /// Gets all comments by type
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public Task<List<Comment>> GetCommentsAsync(string type);

    
    /// <summary>
    /// Creates a new comment
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="date"></param>
    /// <param name="replyTo"></param>
    /// <param name="type"></param>
    /// <param name="isAnonymous"></param>
    /// <param name="commenterName"></param>
    /// <param name="text"></param>
    /// <param name="fixRequestId"></param>
    /// <param name="riskId"></param>
    /// <param name="vulnerabilityId"></param>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public Task<Comment> CreateCommentsAsync(
        int userId,
        DateTime date,
        int replyTo,
        string type,
        bool isAnonymous,
        string commenterName,
        string text,
        int fixRequestId,
        int riskId,
        int vulnerabilityId,
        int hostId
    );
}