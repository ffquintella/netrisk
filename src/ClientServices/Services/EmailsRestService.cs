using ClientServices.Interfaces;

namespace ClientServices.Services;

public class EmailsRestService(IRestService restService) : RestServiceBase(restService), IEmailsService
{
    public Task SendVulnerabilityFixRequestMailAsync(int vulnerabilityId, string comments, string destination,
        bool sendToGroup = false, int fixTeamId = 0)
    {
        throw new NotImplementedException();
    }
    
}