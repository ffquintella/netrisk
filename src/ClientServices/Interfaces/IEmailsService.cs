using Model.DTO;

namespace ClientServices.Interfaces;

public interface IEmailsService
{
    public Task SendVulnerabilityFixRequestMailAsync(FixRequestDto fixRequestDto, bool sendToGroup = false);
}