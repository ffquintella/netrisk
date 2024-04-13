﻿using DAL.Entities;
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
        await using var dbContext = DalService.GetContext();

        var comments =  dbContext.Comments.Where(c => c.Type == type).ToList();
        
        return comments;
    }
    
    public async Task<Comment> CreateCommentsAsync(
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
        )
    {
        await using var dbContext = DalService.GetContext();

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
        
        var newcomment = dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync();
        
        return newcomment.Entity;
    }
}