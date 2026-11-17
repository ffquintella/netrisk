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
    /// Creates a comment written by <paramref name="author"/>, the authenticated caller (T375). The author, date
    /// and display name are never taken from the request; the caller must be allowed to read the record.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">Unknown type, missing target, bad text or reply.</exception>
    /// <exception cref="Model.Exceptions.DataNotFoundException">The record does not exist in the caller's scope.</exception>
    /// <exception cref="Model.Exceptions.UserNotAuthorizedException">The caller may not comment on that record.</exception>
    public Task<Comment> CreateUserCommentAsync(
        User author,
        string? type,
        int? replyTo,
        bool isAnonymous,
        string? text,
        int? fixRequestId,
        int? riskId,
        int? vulnerabilityId,
        int? hostId);

    /// <summary>
    /// Creates a comment for a trusted ingest (the website sync), with no authorization of its own. Never call it
    /// with an author id taken from a request: use <see cref="CreateUserCommentAsync"/>.
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
        int? userId,
        DateTime date,
        int? replyTo,
        string type,
        bool isAnonymous,
        string commenterName,
        string text,
        int? fixRequestId,
        int? riskId,
        int? vulnerabilityId,
        int? hostId
    );
    
    /// <summary>
    /// Gets all comments on a fix request, with no check of who asks — for the website sync push. An anonymous
    /// comment comes back without its author id.
    /// </summary>
    public Task<List<Comment>> GetFixRequestCommentsAsync(int fixRequestId);

    /// <summary>
    /// The comments on a fix request as <paramref name="reader"/> may see them: only for its requester, its fix team,
    /// the finding's analyst or a holder of <c>vulnerabilities</c>, inside the reader's entity scope.
    /// </summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">Missing, out of scope, or the reader takes no part.</exception>
    public Task<List<Comment>> GetFixRequestCommentsForUserAsync(User reader, int fixRequestId);

    /// <summary>
    /// Gets all comments left on a host, oldest first; an anonymous comment comes back without its author id.
    /// </summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">The host is missing or outside the caller's scope.</exception>
    public Task<List<Comment>> GetHostCommentsAsync(int hostId);


    /// <summary>
    /// Return all the user comments
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<List<Comment>> GetUserCommentsAsync(int userId);
    
    

}