using Mapster;
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
    IFilesService filesService, 
    IIncidentResponsePlansService incidentResponsePlansService,
    IIrpAutomationService irpAutomationService,
    INotificationEventPublisher notifications
):ServiceBase(logger, dalService), IIncidentsService
{
    
    
    private IFilesService FilesService { get; } = filesService;
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = incidentResponsePlansService;
    private readonly IIrpAutomationService _irpAutomationService = irpAutomationService;
    
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
        EnsureKindDefined(incident);

        await using var dbContext = DalService.GetContext();
        
        incident.Id = 0;
        incident.CreatedById = user.Value;
        incident.CreationDate = DateTime.Now;
        incident.UpdatedById = user.Value;
        incident.LastUpdate = DateTime.Now;
        if(incident.Status == 0) incident.Status = (int)IntStatus.New;
        
        await dbContext.Incidents.AddAsync(incident);

        await dbContext.SaveChangesAsync();

        // Dynamically evaluate and trigger active IRP templates (SOAR)
        try
        {
            await _irpAutomationService.TriggerForIncidentAsync(incident);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute automatic incident response plan activation for Incident {IncidentId}", incident.Id);
        }

        // Track 4.1.3 — incident.created.
        await notifications.IncidentCreatedAsync(incident);

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
        
        var incident = await dbContext.Incidents.Include(x => x.IncidentResponsePlansActivated)
            .FirstOrDefaultAsync(x => x.Id == id);
        
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
        EnsureKindDefined(incident);

        await using var dbContext = DalService.GetContext();
        
        var existingIncident = await dbContext.Incidents.FirstOrDefaultAsync(x => x.Id == incident.Id);
        
        if (existingIncident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }
        
        incident.Id = existingIncident.Id;
        // Stage 9.9 (S50 §4.4, R11): when the incident entered the register is not editable. It bounds the occurrence
        // backtesting compares registrations with, so moving it later would let a risk written after the fact count as
        // having foreseen the incident.
        incident.CreationDate = existingIncident.CreationDate;
        incident.UpdatedById = user.Value;
        incident.LastUpdate = DateTime.Now;

        incident.Adapt(existingIncident);

        await dbContext.SaveChangesAsync();

        return incident;

    }

    public async Task<IncidentTimelineEntryDto> AddTimelineEntryAsync(int incidentId, string? text, User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var entry = text?.Trim() ?? "";
        if (entry.Length == 0)
            throw new InvalidParameterException("text", "A timeline entry needs text.");
        if (entry.Length > IncidentTimelineEntryDto.MaxTextLength)
            throw new InvalidParameterException("text",
                $"A timeline entry holds at most {IncidentTimelineEntryDto.MaxTextLength} characters.");
        if (entry.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new InvalidParameterException("text", "A timeline entry cannot carry control characters.");

        await using var dbContext = DalService.GetContext();

        // Through the scoped context (Track 2): an incident outside the caller's entities is "not found", never appended to.
        if (!await dbContext.Incidents.AnyAsync(x => x.Id == incidentId))
            throw new DataNotFoundException("Incidents", "Incident not found");

        // Append-only: the incident row itself is not touched, so its last update (and an agent's version of it) stays.
        var action = new NrAction
        {
            ObjectType = nameof(Incident),
            IncidentId = incidentId,
            UserId = user.Value,
            DateTime = DateTime.Now,
            Message = entry
        };
        dbContext.NrActions.Add(action);
        await dbContext.SaveChangesAsync();

        return new IncidentTimelineEntryDto
        {
            Id = action.Id, IncidentId = incidentId, At = action.DateTime, UserId = action.UserId, Text = entry
        };
    }

    /// <summary>
    /// Stage 9.2 (S42 §5.4): an incident is an <c>Incident</c> or a <c>NearMiss</c>. Any other number
    /// would be stored as a third kind nothing reads, and the record would silently stop counting as
    /// either.
    /// </summary>
    private static void EnsureKindDefined(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (!Enum.IsDefined(incident.Kind))
            throw new InvalidParameterException(nameof(Incident.Kind),
                $"Incident kind {(int)incident.Kind} is not one of Incident (1) or NearMiss (2).");
    }

    public async Task DeleteByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        var existingIncident = await dbContext.Incidents.FirstOrDefaultAsync(x => x.Id == id);
        
        if (existingIncident == null)
        {
            throw new DataNotFoundException("Incidents", "Incident not found");
        }

        // T354 review: the incident's timeline entries (nr_actions.IncidentId, fk_inc_actions — RESTRICT since 39.sql)
        // are deleted with it, in the same save; otherwise one appended entry makes the incident undeletable. They are
        // the incident's own history, so they go rather than linger unlinked; the audit table records each deleted row.
        var timeline = await dbContext.NrActions.Where(a => a.IncidentId == id).ToListAsync();
        dbContext.NrActions.RemoveRange(timeline);
        dbContext.Incidents.Remove(existingIncident);

        await dbContext.SaveChangesAsync();

    }
    
}