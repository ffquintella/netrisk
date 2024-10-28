using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IFixRequestsService
{
    public Task<FixRequest> CreateFixRequestAsync(FixRequestDto fixRequest, bool sendToGroup = false);
    
    public Task<List<FixRequest>> GetVulnerabilitiesFixRequestAsync(List<int> vulnerabilitiesIds);
    
    
}