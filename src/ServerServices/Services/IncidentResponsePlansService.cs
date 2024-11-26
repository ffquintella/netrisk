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

    public async Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false)
    {
        await using var dbContext = DalService.GetContext();
        
        //var query = dbContext.IncidentResponsePlans.AsQueryable();
        
        IncidentResponsePlan? irp = null;
            
        if(includeTasks) irp = dbContext.IncidentResponsePlans.Include(x => x.Tasks).FirstOrDefault(x => x.Id == id);
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

    public async Task<List<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int taskId)
    {
        await using var dbContext = DalService.GetContext();
        
        var irptes = await dbContext.IncidentResponsePlanTaskExecutions.Where(x => x.TaskId == taskId).ToListAsync();
        
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
        IncidentResponsePlanExecution incidentResponsePlanExecution, User user)
    {
        await using var dbContext = DalService.GetContext();
        
        incidentResponsePlanExecution.CreatedById = user.Value;
        incidentResponsePlanExecution.Status = (int)IntStatus.New;
        incidentResponsePlanExecution.LastUpdatedById = user.Value;
        incidentResponsePlanExecution.Id = 0;
        
        var irp = await dbContext.IncidentResponsePlans.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanExecution.PlanId);
        
        if(irp == null)
        {
            throw new DataNotFoundException("incidentResponsePlan",$"{incidentResponsePlanExecution.PlanId}");
        }
        
        irp.Executions.Add(incidentResponsePlanExecution);
        
        await dbContext.SaveChangesAsync();
        
        return incidentResponsePlanExecution;
    }

    public async Task<IncidentResponsePlanTaskExecution> CreateTaskExecutionAsync(
        IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution, User user)
    {
        await using var dbContext = DalService.GetContext();

        incidentResponsePlanTaskExecution.Id = 0;
        incidentResponsePlanTaskExecution.CreatedById = user.Value;
        incidentResponsePlanTaskExecution.Status = (int)IntStatus.New;
        incidentResponsePlanTaskExecution.LastUpdatedById = user.Value;
        incidentResponsePlanTaskExecution.CreatedAt = DateTime.Now;
        incidentResponsePlanTaskExecution.LastUpdatedAt = DateTime.Now;
        
        var irpt = await dbContext.IncidentResponsePlanTasks.FirstOrDefaultAsync(x => x.Id == incidentResponsePlanTaskExecution.TaskId);
        
        if(irpt == null)
        {
            throw new DataNotFoundException("incidentResponsePlanTask",$"{incidentResponsePlanTaskExecution.TaskId}");
        }
        
        irpt.Executions.Add(incidentResponsePlanTaskExecution);
        
        await dbContext.SaveChangesAsync();
        
        return incidentResponsePlanTaskExecution;
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