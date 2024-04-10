using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IFixRequestsService
{
    public Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest);
}