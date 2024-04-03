using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IMessagesService
{
    /// <summary>
    /// Get the number of messages the user has
    /// </summary>
    /// <returns></returns>
    public Task<int> GetCountAsync(List<int?>? chats = null);
    
    /// <summary>
    /// Check if the user has any unread messages
    /// </summary>
    /// <returns></returns>
    public Task<bool> HasUnreadMessages(List<int?>? chats = null);
    
    /// <summary>
    /// Get all messages the user has
    /// </summary>
    /// <returns></returns>
    public Task<List<Message>> GetMessagesAsync(List<int?>? chats = null);
    
    /// <summary>
    /// Mark de message as read
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task ReadMessageAsync(int id);
}