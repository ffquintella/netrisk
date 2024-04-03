using DAL.Entities;
using Model.Messages;

namespace ServerServices.Interfaces;

public interface IMessagesService
{
    public Task SendMessageAsync(string message, int userId, int? chatId = null, int type = (int)MessageType.Information);
    
    /// <summary>
    /// Gets all user Messages
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="chats"></param>
    /// <returns></returns>
    public Task<List<Message>> GetAllAsync(int userId, List<int?>? chats);
    
    /// <summary>
    /// Check if the user has any unread messages
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="chats"></param>
    /// <returns></returns>
    public Task<bool> HasUnreadMessagesAsync(int userId, List<int?>? chats = null);
    
    /// <summary>
    /// Gets one message by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<Message> GetMessageAsync(int id);
    
    /// <summary>
    /// Save a message
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task SaveMessageAsync(Message message);
    

}