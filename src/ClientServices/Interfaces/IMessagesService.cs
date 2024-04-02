namespace ClientServices.Interfaces;

public interface IMessagesService
{
    /// <summary>
    /// Get the number of messages the user has
    /// </summary>
    /// <returns></returns>
    public Task<int> GetCountAsync();
    
    /// <summary>
    /// Check if the user has any unread messages
    /// </summary>
    /// <returns></returns>
    public Task<bool> HasUnreadMessages();
}