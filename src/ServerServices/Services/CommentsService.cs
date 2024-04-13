using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class CommentsService: ServiceBase, ICommentsService
{
    public CommentsService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
    }

    public async Task<List<Comment>> GetCommentsAsync(string type)
    {
        using var dbContext = DalService.GetContext();

        var comments =  dbContext.Comments.Where(c => c.Type == type).ToList();
        
        return comments;
    }
}