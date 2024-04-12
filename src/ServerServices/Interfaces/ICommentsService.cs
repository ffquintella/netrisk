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
}