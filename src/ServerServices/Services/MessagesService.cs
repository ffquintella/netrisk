using AutoMapper;
using DAL.Entities;
using Model;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class MessagesService: ServiceBase, IMessagesService
{
    public IMapper Mapper { get; }
    public MessagesService(ILogger logger, DALService dalService, IMapper mapper) : base(logger, dalService)
    {
        Mapper = mapper;
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
            Id = 0,
            Status = (int)IntStatus.New
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
        
        await dbContext.SaveChangesAsync();
    }

    public async  Task<List<Message>> GetAllAsync(int userId)
    {
        await using var dbContext = DalService.GetContext();

        var messages = dbContext.Messages.Where(m => m.UserId == userId).ToList();

        return messages;
    }
    
}