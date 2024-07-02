using DAL.Entities;

namespace ClientServices.Interfaces;

public interface ICommentsService
{
    public Task<List<Comment>> GetAllUserCommentsAsync();
    
    public Task<List<Comment>> GetFixRequestCommentsAsync(int requestId);
    
    public Task<Comment> CreateCommentAsync(Comment comment);
    
}