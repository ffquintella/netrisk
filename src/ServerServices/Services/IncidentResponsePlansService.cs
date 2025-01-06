using AutoMapper;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Model;
using Model.Exceptions;
using Model.Messages;
using Serilog;
using ServerServices.EmailTemplates;
using ServerServices.Interfaces;
using Tools.Security;

namespace ServerServices.Services;


public class IncidentResponsePlansService(
    ILogger logger,
    IDalService dalService, 
    IMapper mapper,
    IMessagesService messagesService,
    ILocalizationService localizationService, 
    IEmailService emailService,
    IEntitiesService entitiesService,
    IConfiguration configuration
    ):LocalizableService(logger, dalService, localizationService), IIncidentResponsePlansService
{

    private IMapper Mapper { get; } = mapper;
    private IMessagesService MessagesService { get; } = messagesService;
    private IEmailService EmailService { get; } = emailService;
    private IEntitiesService EntitiesService { get; } = entitiesService;
    private IConfiguration Configuration { get; } = configuration;
    
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
        
        if(user == null)
        {
            throw new DataNotFoundException("user","---");
        }
        
        if(user.Admin == false)
        {
            if(!PermissionTool.VerifyPermission("incident-response-plans", user)) throw new PermissionInvalidException("incident-response-plans", user.Value, "IncidentResponsePlan.CreateAsync");
            
            if (incidentResponsePlan.HasBeenApproved == true)
            {
                if(PermissionTool.VerifyPermission("irp-approve", user)) incidentResponsePlan.Status = (int)IntStatus.Approved;
                else throw new PermissionInvalidException("irp-approve", user.Value, "IncidentResponsePlan.CreateAsync");
            }
            if (incidentResponsePlan.HasBeenExercised == true)
            {
                if(!PermissionTool.VerifyPermission("irp-exercise", user)) throw new PermissionInvalidException("irp-exercise", user.Value, "IncidentResponsePlan.CreateAsync");
            }     
            if (incidentResponsePlan.HasBeenReviewed == true)
            {
                if(!PermissionTool.VerifyPermission("irp-review", user)) throw new PermissionInvalidException("irp-review", user.Value, "IncidentResponsePlan.CreateAsync");
            }     
            if (incidentResponsePlan.HasBeenTested == true)
            {
                if(!PermissionTool.VerifyPermission("irp-test", user)) throw new PermissionInvalidException("irp-test", user.Value, "IncidentResponsePlan.CreateAsync");
            } 
            if (incidentResponsePlan.HasBeenUpdated == true)
            {
                if(!PermissionTool.VerifyPermission("irp-update", user)) throw new PermissionInvalidException("irp-update", user.Value, "IncidentResponsePlan.CreateAsync");
            } 
        }
     
        
        
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

    public async Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false, bool includeActivatedBy = false)
    {
        await using var dbContext = DalService.GetContext();
        
        //var query = dbContext.IncidentResponsePlans.AsQueryable();
        
        IncidentResponsePlan? irp = null;
            
        if(includeTasks && !includeActivatedBy) irp = dbContext.IncidentResponsePlans.Include(x => x.Tasks).FirstOrDefault(x => x.Id == id);
        else if(!includeTasks && includeActivatedBy)
            irp = dbContext.IncidentResponsePlans.Include(x => x.ActivatedBy).FirstOrDefault(x => x.Id == id);
        else if(includeTasks && includeActivatedBy)
            irp = dbContext.IncidentResponsePlans.Include(x => x.ActivatedBy)
                .Include(y => y.Tasks)
                .FirstOrDefault(x => x.Id == id);
        
        else irp = dbContext.IncidentResponsePlans.FirstOrDefault(x => x.Id == id);
        
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

    public async Task<IncidentResponsePlanTask> CreateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask, User user)
    {
        incidentResponsePlanTask.CreatedById = user.Value;
        incidentResponsePlanTask.Status = (int)IntStatus.AwaitingApproval;
        incidentResponsePlanTask.Id = 0;
        incidentResponsePlanTask.LastUpdate = DateTime.Now;
        incidentResponsePlanTask.UpdatedById = user.Value;

        if (incidentResponsePlanTask is { IsParallel: not null, IsOptional: not null })
        {
            if (incidentResponsePlanTask.IsParallel.Value && incidentResponsePlanTask.IsOptional.Value)
                throw new InvalidParameterException(nameof(IncidentResponsePlanTask),"Cannot have a parallel and sequential task at the same time");
        }
        
        await using var dbContext = DalService.GetContext();
        
        var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanTask.PlanId);
        
        if(irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{incidentResponsePlanTask.PlanId}");
        }
        
        incidentResponsePlanTask.Id = 0;
        
        irp.Tasks.Add(incidentResponsePlanTask);
        
        await dbContext.SaveChangesAsync();
        
        Log.Information("User:{User} created task:{Task} for incidentResponsePlan:{IncidentResponsePlan}", user.Value, incidentResponsePlanTask.Id, irp.Id);

        
        return incidentResponsePlanTask;
    }

    public async Task UpdateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask, User user)
    {
        incidentResponsePlanTask.LastUpdate = DateTime.Now;
        incidentResponsePlanTask.UpdatedById = user.Value;
        
        if (incidentResponsePlanTask is { IsParallel: not null, IsOptional: not null })
        {
            if (incidentResponsePlanTask.IsParallel.Value && incidentResponsePlanTask.IsOptional.Value)
                throw new InvalidParameterException(nameof(IncidentResponsePlanTask),"Cannot have a parallel and sequential task at the same time");
        }
        
        await using var dbContext = DalService.GetContext();
        
        var existing = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanTask.Id);
        
        if (existing == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{incidentResponsePlanTask.Id}");
        }
        
        Mapper.Map(incidentResponsePlanTask, existing);
        
        await dbContext.SaveChangesAsync();
    }

    public async Task<IncidentResponsePlanTask> GetTaskByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpt = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == id);
        
        if (irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{id}");
        }
        
        return irpt;
    }

    public async Task DeleteTaskAsync(int taskId)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpt = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == taskId);
        
        if (irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{taskId}");
        }
        
        dbContext.IncidentResponsePlanTasks.Remove(irpt);

        await dbContext.SaveChangesAsync();

    }

    public async Task<List<IncidentResponsePlanTask>> GetTasksByPlanIdAsync(int planId)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpts = await dbContext.IncidentResponsePlanTasks.Where(x => x.PlanId == planId).ToListAsync();
        
        return irpts;
    }

    public async Task<List<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int id)
    {
        await using var dbContext = DalService.GetContext();
        
        var irptes = await dbContext.IncidentResponsePlanTaskExecutions.Where(x => x.Id == id).ToListAsync();
        
        return irptes;
    }
    

    public async Task<List<IncidentResponsePlanExecution>> GetExecutionsByPlanIdAsync(int planId)
    {
        await using var dbContext = DalService.GetContext();
        
        var executions = await dbContext.IncidentResponsePlanExecutions.Where(x => x.PlanId == planId).ToListAsync();
        
        return executions;
    }

    public async Task<IncidentResponsePlanTaskExecution> GetTaskExecutionByIdAsync(int taskExecutionId)
    {
        await using var dbContext = DalService.GetContext();
        
        var execution = await dbContext.IncidentResponsePlanTaskExecutions.FirstOrDefaultAsync(x => x.Id == taskExecutionId);

        if (execution == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTaskExecution",$"{taskExecutionId}");
        }

        return execution;
    }

    public async Task<IncidentResponsePlanExecution> CreateExecutionAsync(
        IncidentResponsePlanExecution incidentResponsePlanExecution, Incident incident, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incidentResponsePlanExecution.CreatedById = user.Value;
        incidentResponsePlanExecution.Status = (int)IntStatus.New;
        incidentResponsePlanExecution.LastUpdatedById = user.Value;
        incidentResponsePlanExecution.Id = 0;
        
        var irp = await dbContext.IncidentResponsePlans.Include(i => i.Tasks)
            .FirstOrDefaultAsync(x => x.Id == incidentResponsePlanExecution.PlanId);
        
        if(irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{incidentResponsePlanExecution.PlanId}");
        }
        
        irp.Executions.Add(incidentResponsePlanExecution);
        
        await dbContext.SaveChangesAsync();
        
        // Send the message to the Assignee

        await MessagesService.SendMessageAsync(Localizer["You have been assigned to execute the Incident Response Plan: "] + irp.Name, user.Value, (int)ChatTypes.GeneralAlerts);
        
        // Let's create the tasks if it's not a test
        
        if(incidentResponsePlanExecution.IsTest == false)
        {
            foreach (var task in irp.Tasks)
            {
                var taskExecution = new IncidentResponsePlanTaskExecution()
                {
                    TaskId = task.Id,
                    PlanExecutionId = incidentResponsePlanExecution.Id,
                    ExecutionDate = DateTime.Now,
                    CreatedById = user.Value,
                    LastUpdatedById = user.Value,
                    LastUpdatedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    Status = (int)IntStatus.New,
                    
                };
                
                var createdTask = await CreateTaskExecutionAsync(taskExecution, incident, user);
                
                
                //incidentResponsePlanExecution.TasksExecuted.Add(createdTask);
            }
            
            //await dbContext.SaveChangesAsync();
        }
        
        return incidentResponsePlanExecution;
    }

    public async Task<IncidentResponsePlanTaskExecution> CreateTaskExecutionAsync(
        IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution, Incident incident,  User user)
    {
        await using var dbContext = DalService.GetContext();

        incidentResponsePlanTaskExecution.Id = 0;
        incidentResponsePlanTaskExecution.CreatedById = user.Value;
        incidentResponsePlanTaskExecution.Status = (int)IntStatus.New;
        incidentResponsePlanTaskExecution.LastUpdatedById = user.Value;
        incidentResponsePlanTaskExecution.CreatedAt = DateTime.Now;
        incidentResponsePlanTaskExecution.LastUpdatedAt = DateTime.Now;

        var irpe = dbContext.IncidentResponsePlanExecutions.FirstOrDefault(irpe =>
            irpe.Id == incidentResponsePlanTaskExecution.PlanExecutionId);
        
        if(irpe == null) throw new DataNotFoundException("incidentResponsePlanExecution",$"{incidentResponsePlanTaskExecution.PlanExecutionId}");
        
        var irpt = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanTaskExecution.TaskId);
        
        if(irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{incidentResponsePlanTaskExecution.TaskId}");
        }
        
        if (incidentResponsePlanTaskExecution.ExecutedById == null)
        {
            incidentResponsePlanTaskExecution.ExecutedById = irpt.AssignedToId;
        }
        
        
        irpt.Executions.Add(incidentResponsePlanTaskExecution);
        
        await dbContext.SaveChangesAsync();
        
        //Send Task E-mail 

        var destination = EntitiesService.GetEntity(incidentResponsePlanTaskExecution.ExecutedById.Value).EntitiesProperties.FirstOrDefault(p => p.Type == "email")?.Value;
        
        if(destination == null) throw new DataNotFoundException("destination",$"{incidentResponsePlanTaskExecution.ExecutedById}");

        var mailParameters = new TaskExecution()
        {
            IncidentName = incident.Name,
            IncidentDescription = incident.Description,
            TaskName = irpt.Name,
            TaskCompletionCriteria = irpt.CompletionCriteria ?? "",
            TaskFailureCriteria = irpt.FailureCriteria ?? "",
            TaskSuccessCriteria = irpt.SuccessCriteria ?? "",
            TaskVerificationCriteria = irpt.VerificationCriteria ?? "",
            TaskConditionToProceed = irpt.ConditionToProceed ?? "",
            TaskConditionToSkip = irpt.ConditionToSkip ?? "",
            ReportLink = Configuration["website:protocol"] + "://" + Configuration["website:host"] + ":" + Configuration["website:port"] + "/IRTEReport?key=" + irpt.Id,
            
        };
        
        await EmailService.SendEmailAsync(destination, "Incident Taks", "TaskExecution", user.Lang!.ToLower(), mailParameters);
        
        return incidentResponsePlanTaskExecution;
    }

    public async Task<Incident> GetIncidentByTaskIdAsync(int taskId)
    {
        await using var dbContext = DalService.GetContext();

        var irpt = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == taskId);
        
        if(irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{taskId}");
        }
        
        var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == irpt.PlanId);
        
        if(irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{irpt.PlanId}");
        }

        var incident = await dbContext.Incidents.Where(x => x.IncidentResponsePlansActivated.Contains(irp))
            .OrderByDescending(i => i.CreationDate).FirstOrDefaultAsync();
        
        if(incident == null) throw new DataNotFoundException("Incidents",$"{irpt.PlanId}");
        
        return incident;

    }

    public async Task ChangeExecutionTaskSatusByIdAsync(int taskId, int status)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpte = await dbContext.IncidentResponsePlanTaskExecutions.FirstOrDefaultAsync(x => x.Id == taskId);
        
        if(irpte == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTaskExecution",$"{taskId}");
        }
        
        irpte.Duration = DateTime.Now - irpte.CreatedAt;
        irpte.Status = status;
        
        await dbContext.SaveChangesAsync();
    }

    public async Task<IncidentResponsePlanExecution> UpdateExecutionAsync(
        IncidentResponsePlanExecution incidentResponsePlanExecution, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incidentResponsePlanExecution.LastUpdatedById = user.Value;
        incidentResponsePlanExecution.LastUpdateDate = DateTime.Now;
        
        var existing = await dbContext.IncidentResponsePlanExecutions.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanExecution.Id);
        
        if(existing == null)
        {
            throw new DataNotFoundException("incidentResponsePlanExecution",$"{incidentResponsePlanExecution.Id}");
        }
        
        Mapper.Map(incidentResponsePlanExecution, existing);
        
        await dbContext.SaveChangesAsync();
        
        return existing;
        
    }
    
    public async Task<IncidentResponsePlanTaskExecution> UpdateTaskExecutionAsync(
        IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incidentResponsePlanTaskExecution.LastUpdatedById = user.Value;
        incidentResponsePlanTaskExecution.LastUpdatedAt = DateTime.Now;

        var irpt = await dbContext.IncidentResponsePlanTaskExecutions.FirstOrDefaultAsync(irpte =>
            irpte.Id == incidentResponsePlanTaskExecution.Id);
        
        if(irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTaskExecution",$"{incidentResponsePlanTaskExecution.TaskId}");
        }

        Mapper.Map(incidentResponsePlanTaskExecution, irpt);
        
        await dbContext.SaveChangesAsync();
        
        return irpt;
    }

    public async Task DeleteExecutionAsync(int incidentResponsePlanExecutionId)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpe = await dbContext.IncidentResponsePlanExecutions.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanExecutionId);
        
        if(irpe == null)
        {
            throw new DataNotFoundException("incidentResponsePlanExecution",$"{incidentResponsePlanExecutionId}");
        }
        
        dbContext.IncidentResponsePlanExecutions.Remove(irpe);
        
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteTaskExecutionAsync(int incidentResponsePlanTaskExecutionId)
    {
        await using var dbContext = DalService.GetContext();
        var irpte = await dbContext.IncidentResponsePlanTaskExecutions.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanTaskExecutionId);
        
        if(irpte == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTaskExecution",$"{incidentResponsePlanTaskExecutionId}");
        }
        
        dbContext.IncidentResponsePlanTaskExecutions.Remove(irpte);
        
        await dbContext.SaveChangesAsync();
        
    }

    public async Task<IncidentResponsePlanExecution> GetExecutionByIdAsync(int executionId)
    {
        await using var dbContext = DalService.GetContext();
        
        var irpe = await dbContext.IncidentResponsePlanExecutions.FirstOrDefaultAsync(x => x.Id == executionId);
        
        if(irpe == null)
        {
            throw new DataNotFoundException("incidentResponsePlanExecution",$"{executionId}");
        }
        
        return irpe;
    }


}