using Mapster;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using Model.Messages;
using ServerServices.Interfaces;
using Mapster;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class MessagesService: ServiceBase, IMessagesService
{
    public MessagesService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
    }

    public async Task SendMessageAsync(string message, int userId, int? chatId = null, int type = (int)MessageType.Information)
    {
        await using var dbContext = DalService.GetContext();
        
        var mess = new Message
        {
            UserId = userId,
            ChatId = chatId,
            Message1 = message,
            CreatedAt = DateTime.Now,
            Id = 0,
            Status = (int)IntStatus.New,
            Type = type
        };
        
        await dbContext.Messages.AddAsync(mess);
        await dbContext.SaveChangesAsync();
    }

    public async Task<Message> GetMessageAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var dbMessage = await dbContext.Messages.FindAsync(id);
        if (dbMessage == null)
            throw new Exception("Message not found");

        return dbMessage;
    }

    public async Task SaveMessageAsync(Message message)
    {
        await using var dbContext = DalService.GetContext();
        
        var dbMessage = await dbContext.Messages.FindAsync(message.Id);
        if (dbMessage == null)
            throw new Exception("Message not found");
        
        message.Adapt(dbMessage);
        
        await dbContext.SaveChangesAsync();
    }

    public async  Task<List<Message>> GetAllAsync(int userId, List<int?>? chats = null)
    {
        await using var dbContext = DalService.GetContext();

        if(chats == null) return await dbContext.Messages.Where(m => m.UserId == userId).ToListAsync();

        // TODO: Remove this intermediate state - this whas added to fix a bug in pomelo provider
        var messages = await dbContext.Messages.ToListAsync();
            
        return messages.Where(m => m.UserId == userId && chats.Contains(m.ChatId)).ToList();
    }

    public async Task<bool> HasUnreadMessagesAsync(int userId, List<int?>? chats = null)
    {
        await using var dbContext = DalService.GetContext();

        List<Message> messages = chats == null ? 
            dbContext.Messages.Where(m => m.UserId == userId && m.Status == (int)IntStatus.New).ToList() : 
            dbContext.Messages.Where(m => m.UserId == userId && chats.Contains(m.ChatId)).ToList();

        if(messages.Count > 0)
            return true;
        return false;
    }

    public async Task DeleteMessageAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var dbMessage = await dbContext.Messages.FindAsync(id);
        if (dbMessage == null)
            throw new DataNotFoundException("Message", "Message not found");
        
        dbContext.Messages.Remove(dbMessage);
        await dbContext.SaveChangesAsync();

    }
    
}