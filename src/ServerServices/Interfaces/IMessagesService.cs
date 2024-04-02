using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IMessagesService
{
    public Task SendMessageAsync(string message, int userId, int? chatId = null);
    
    /// <summary>
    /// Gets all user Messages
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<List<Message>> GetAllAsync(int userId);
    
    /// <summary>
    /// Check if the user has any unread messages
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public Task<bool> HasUnreadMessagesAsync(int userId);
    
    public Task<Message> GetMessageAsync(int id);
    
    public Task SaveMessageAsync(Message message);
}