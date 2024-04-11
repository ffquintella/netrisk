using DAL.Entities;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class FixRequestsService: ServiceBase, IFixRequestsService
{
    public FixRequestsService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public async Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests.AddAsync(fixRequest);
        await db.SaveChangesAsync();
        
        return result.Entity;
    }
}