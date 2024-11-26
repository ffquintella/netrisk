using AutoMapper;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class IncidentsService(
    ILogger logger,
    IDalService dalService, 
    IMapper mapper
):ServiceBase(logger, dalService), IIncidentsService
{
    
    private IMapper Mapper { get; } = mapper;
    
    public async Task<List<Incident>> GetAllAsync()
    {
        await using var dbContext = DalService.GetContext();
        
        var incidents = await dbContext.Incidents.ToListAsync();
        
        return incidents;
    }

    public async Task<Incident> CreateAsync(Incident incident, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incident.Id = 0;
        incident.CreatedById = user.Value;
        incident.CreationDate = DateTime.Now;
        incident.UpdatedById = user.Value;
        incident.LastUpdate = DateTime.Now;
        incident.Status = (int)IntStatus.New;
        
        await dbContext.Incidents.AddAsync(incident);

        await dbContext.SaveChangesAsync();

        return incident;

    }

    public async Task<Incident> GetByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var incident = await dbContext.Incidents.FirstOrDefaultAsync(x => x.Id == id);
        
        if (incident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }
        
        return incident;
        
    }
    
    public async Task UpdateAsync(Incident incident, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        var existingIncident = await dbContext.Incidents.FirstOrDefaultAsync(x => x.Id == incident.Id);
        
        if (existingIncident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }
        
        incident.Id = existingIncident.Id;
        incident.UpdatedById = user.Value;
        incident.LastUpdate = DateTime.Now;

        Mapper.Map(incident, existingIncident);

        await dbContext.SaveChangesAsync();
        
    }

    public async Task DeleteByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        var existingIncident = await dbContext.Incidents.FirstOrDefaultAsync(x => x.Id == id);
        
        if (existingIncident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }

        dbContext.Incidents.Remove(existingIncident);
        
        await dbContext.SaveChangesAsync();

    }
    
}