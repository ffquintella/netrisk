using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IMessagesService
{
    public Task SendMessageAsync(string message, int userId, int? chatId = null);
    
    public Task<List<Message>> GetAllAsync(int userId);
    
    public Task<Message> GetMessageAsync(int id);
    
    public Task SaveMessageAsync(Message message);
}