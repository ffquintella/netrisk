using DAL.Entities;
using Model.Vulnerability;

namespace ClientServices.Interfaces;

public interface IFixRequestsService
{
    public Task<FixRequest> CreateFixRequestAsync(FixRequestDto fixRequest, bool sendToGroup = false);
    
    
}