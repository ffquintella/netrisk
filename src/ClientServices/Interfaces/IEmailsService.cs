using Model.DTO;

namespace ClientServices.Interfaces;

public interface IEmailsService
{
    /// <summary>
    /// Sends a mail to the group with the fix request
    /// </summary>
    /// <param name="fixRequestDto"></param>
    /// <param name="sendToGroup"></param>
    /// <returns></returns>
    public Task SendVulnerabilityFixRequestMailAsync(FixRequestDto fixRequestDto, bool sendToGroup = false);
    
    /// <summary>
    /// Sends a mail to the group with the fix request using the update comments template
    /// </summary>
    /// <param name="fixRequestId"></param>
    /// <param name="comment"></param>
    /// <returns></returns>
    public Task SendVulnerabilityUpdateMailAsync(int fixRequestId, string comment);
}