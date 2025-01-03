using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class IncidentsService(
    ILogger logger,
    IDalService dalService, 
    IMapper mapper,
    IFilesService filesService, 
    IIncidentResponsePlansService incidentResponsePlansService
):ServiceBase(logger, dalService), IIncidentsService
{
    
    private IMapper Mapper { get; } = mapper;
    
    private IFilesService FilesService { get; } = filesService;
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = incidentResponsePlansService;
    
    public async Task<List<Incident>> GetAllAsync()
    {
        await using var dbContext = DalService.GetContext();
        
        var incidents = await dbContext.Incidents.ToListAsync();
        
        return incidents;
    }

    public async Task<int> GetNextSequenceAsync(int year = -1)
    {
        if(year == -1)
        {
            year = DateTime.Now.Year;
        }
        
        await using var dbContext = DalService.GetContext();
        
        //check if there are incidents for the year
        if (!await dbContext.Incidents.AnyAsync(x => x.Year == year))
        {
            return 1;
        }
        
        var sequence = await dbContext.Incidents.Where(i => i.Year == year).MaxAsync(x => x.Sequence);
        
        return sequence + 1;
    }

    public async Task<Incident> CreateAsync(Incident incident, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incident.Id = 0;
        incident.CreatedById = user.Value;
        incident.CreationDate = DateTime.Now;
        incident.UpdatedById = user.Value;
        incident.LastUpdate = DateTime.Now;
        if(incident.Status == 0) incident.Status = (int)IntStatus.New;
        
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

    public async Task<List<FileListing>> GetAttachmentsByIdAsync(int id)
    {
       
        return await FilesService.GetObjectFileListingsAsync(id, FileCollectionType.IncidentFile);
        
    }

    public async Task<List<int>> GetIncidentResponsPlanIdsByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var incident = await dbContext.Incidents.Include(x => x.IncidentResponsePlansActivated).FirstOrDefaultAsync(x => x.Id == id);
        
        if (incident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }
        
        return incident.IncidentResponsePlansActivated.Select(x => x.Id).ToList();
    }

    public async Task AssociateIncidentResponsPlanIdsByIdAsync(int id, List<int> ids, User loggedUser)
    {
        await using var dbContext = DalService.GetContext();
        
        var incident = await dbContext.Incidents.Include(x => x.IncidentResponsePlansActivated).FirstOrDefaultAsync(x => x.Id == id);
        
        if (incident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }
        
        // Now let's check if there was any changes
        var currentIds = incident.IncidentResponsePlansActivated.Select(x => x.Id).ToList();
        
        var changes = false;
        
        List<int> newIds = new List<int>();
        
        foreach (var i in ids)
        {
            if (!currentIds.Contains(i))
            {
                newIds.Add(i);
                changes = true;
                break;
            }
        }
        
        if (!changes)
        {
            return;
        }
        

        
        // Ok now we know there are changes, let's update the incident
        
        //Let's create a new execution for each new id
        
        foreach (var i in newIds)
        {
            var execution = new IncidentResponsePlanExecution()
            {
                PlanId = i,
                ExecutionDate = DateTime.Now,
                CreatedById = incident.CreatedById,
                CreationDate = DateTime.Now,
                LastUpdateDate = DateTime.Now,
                LastUpdatedById = incident.CreatedById,
                Status = (int)IntStatus.New,
                IsTest = false,
                IsExercise = false,
                ExecutionTrigger = $"Incident-{id}",
                ExecutionResult = "---"
            };
            
            await IncidentResponsePlansService.CreateExecutionAsync(execution, incident, loggedUser);
        }
        
        incident.IncidentResponsePlansActivated = new List<IncidentResponsePlan>();
        
        foreach (var i in ids)
        {
            var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == i);
            
            if (irp == null)
            {
                throw new DataNotFoundException("IncidentResponsePlans", "IncidentResponsePlan not found");
            }
            
            incident.IncidentResponsePlansActivated.Add(irp);
        }
        
        await dbContext.SaveChangesAsync();
        

    }
    
    public async Task<Incident> UpdateAsync(Incident incident, User user)
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

        return incident;

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