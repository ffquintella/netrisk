using AutoMapper;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class IncidentResponsePlansService(
    ILogger logger,
    IDalService dalService, 
    IMapper mapper
    ):ServiceBase(logger, dalService), IIncidentResponsePlansService
{

    private IMapper Mapper { get; } = mapper;
    
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
        
        Log.Information("User:{User} created incidentResponsePlan:{IncidentResponsePlan}", user.Value, result.Entity.Id);
        
        return result.Entity;

    }

    public async Task<IncidentResponsePlan> UpdateAsync(IncidentResponsePlan incidentResponsePlan, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        var existing = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == incidentResponsePlan.Id);
        if (existing == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{incidentResponsePlan.Id}");
        }
        
        Mapper.Map(incidentResponsePlan, existing);
        
        existing.UpdatedById = user.Value;
        existing.LastUpdate = DateTime.Now;
        existing.HasBeenUpdated = true;
        
        await dbContext.SaveChangesAsync();
        
        Log.Information("User:{User} updated incidentResponsePlan:{IncidentResponsePlan}", user.Value, existing.Id);
        
        return existing;
        
    }

    public async Task<IncidentResponsePlan> GetByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == id);
        
        if (irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{id}");
        }
        
        return irp;
    }

    public async Task DeleteAsync(int id, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == id);
        
        if (irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{id}");
        }
        
        Log.Information("User:{User} deleted incidentResponsePlan:{IncidentResponsePlan}", user.Value, irp.Id);        
        
        dbContext.IncidentResponsePlans.Remove(irp);
        
        await dbContext.SaveChangesAsync();
        
    }
}