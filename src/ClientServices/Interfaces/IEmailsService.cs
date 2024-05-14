namespace ClientServices.Interfaces;

public interface IEmailsService
{
    public Task SendVulnerabilityFixRequestMailAsync(int vulnerabilityId, string comments, string destination, bool sendToGroup = false, int fixTeamId = 0);
}