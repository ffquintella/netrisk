using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class CommentsService: ServiceBase, ICommentsService
{
    private IMessagesService MessagesService { get; }
    
    public CommentsService(ILogger logger, IDalService dalService, IMessagesService messagesService) : base(logger, dalService)
    {
        MessagesService = messagesService;
    }

    public async Task<List<Comment>> GetCommentsAsync(string type)
    {
        await using var dbContext = DalService.GetContext();

        var comments = await dbContext.Comments.Where(c => c.Type == type).ToListAsync();
        
        return comments;
    }

    public async Task<List<Comment>> GetFixRequestCommentsAsync(int fixRequestId)
    {
        await using var dbContext = DalService.GetContext();
        
        var comments = await dbContext.Comments.Where(c => c.Type == "FixRequest" && c.FixRequestId == fixRequestId).ToListAsync();
        
        return comments;
    }

    public async Task<List<Comment>> GetUserCommentsAsync(int userId)
    {
        await using var dbContext = DalService.GetContext();
        
        var comments = await dbContext.Comments.Where(c => c.UserId == userId).ToListAsync();
        
        return comments;
    }
    
    
    public async Task<Comment> CreateCommentsAsync(
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
        )
    {
        await using var dbContext = DalService.GetContext();
        
        if(type == "FixRequest" && fixRequestId == null)
            throw new Exception("FixRequestId is required for FixRequest comments");
        
        if(type == "Risk" && riskId == null)    
            throw new Exception("RiskId is required for Risk comments");

        if (type == "FixRequest")
        {
            // Creating message to the fix requester 
            
            var fixRequest = await dbContext.FixRequests.FirstOrDefaultAsync(fr => fr.Id == fixRequestId);
            if (fixRequest == null)
                throw new Exception("FixRequest not found");
            
            var message = new Message
            {   
                UserId = fixRequest.RequestingUserId!.Value, // change to requesting user ID
                CreatedAt = DateTime.Now,
                ChatId = 1,
                Type = 1, 
                Status = (int)IntStatus.New,
                Id = 0,
                Message1 = "Your fix request: " + fixRequestId.ToString() + " has a new comment",
            };

            await MessagesService.SaveMessageAsync(message);
        }
            

        var comment = new Comment
        {
            UserId = userId,
            Date = date,
            ReplyTo = replyTo,
            Type = type,
            IsAnonymous = isAnonymous ? (sbyte)1 : (sbyte)0,
            CommenterName = commenterName,
            Text = text,
            FixRequestId = fixRequestId,
            RiskId = riskId,
            VulnerabilityId = vulnerabilityId,
            HostId = hostId
        };
        
        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync();
        
        return comment;
    }
}