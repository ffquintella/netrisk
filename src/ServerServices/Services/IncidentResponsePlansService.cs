using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
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

    public async Task<IncidentResponsePlan> CreateAsync(IncidentResponsePlan incidentResponsePlan, User user)
    {
        await using var dbContext = DalService.GetContext();

        incidentResponsePlan.Id = 0;
        incidentResponsePlan.CreationDate = DateTime.Now;
        incidentResponsePlan.LastUpdate = DateTime.Now;
        incidentResponsePlan.CreatedById = user.Value;
        incidentResponsePlan.UpdatedById = user.Value;
        incidentResponsePlan.Status = (int)IntStatus.AwaitingApproval;
     
        var result = await dbContext.IncidentResponsePlans.AddAsync(incidentResponsePlan);
        
        await dbContext.SaveChangesAsync();
        
        return result.Entity;

    }
}