using DAL.Entities;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class MessagesService: ServiceBase, IMessagesService
{
    public MessagesService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public async Task SendMessageAsync(string message, int userId, int? chatId = null)
    {
        await using var dbContext = DalService.GetContext();
        
        var mess = new Message
        {
            UserId = userId,
            ChatId = chatId,
            Message1 = message,
            CreatedAt = DateTime.Now,
            Id = 0
        };
        
        await dbContext.Messages.AddAsync(mess);
        await dbContext.SaveChangesAsync();
    }

    public async  Task<List<Message>> GetAllAsync(int userId)
    {
        await using var dbContext = DalService.GetContext();

        var messages = dbContext.Messages.Where(m => m.UserId == userId).ToList();

        return messages;
    }
    
}