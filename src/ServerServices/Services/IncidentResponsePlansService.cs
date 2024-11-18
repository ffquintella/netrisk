using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class IncidentResponsePlansService(
    ILogger logger,
    IDalService dalService
    ):ServiceBase(logger, dalService), IIncidentResponsePlansService
{
    public async Task<List<IncidentResponsePlan>> GetAllAsync()
    {
        await using var dbContext = DalService.GetContext();
        
        var irps = await dbContext.IncidentResponsePlans.ToListAsync();
        
        return irps;
    }
}