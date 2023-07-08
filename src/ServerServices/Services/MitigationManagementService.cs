using AutoMapper;
using DAL;
using DAL.Entities;
using Model.Exceptions;
using Model.Risks;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class MitigationManagementService: IMitigationManagementService
{
    private DALManager _dalManager;
    private ILogger _log;
    private readonly IRoleManagementService _roleManagement;
    private IMapper _mapper;

    public MitigationManagementService(
        ILogger logger, 
        DALManager dalManager,
        IMapper mapper,
        IRoleManagementService roleManagementService
    )
    {
        _dalManager = dalManager;
        _log = logger;
        _roleManagement = roleManagementService;
        _mapper = mapper;
    }
    
    public Mitigation GetById(int id)
    {
        using var context = _dalManager.GetContext();
        var mitigation = context.Mitigations.FirstOrDefault(m => m.Id == id);
        if (mitigation == null)
        {
            Log.Error("Mitigation with id {Id} not found", id);
            throw new DataNotFoundException("Mitigation", id.ToString());
        }

        return mitigation;
    }
    
    public Mitigation GetByRiskId(int id)
    {
        using var context = _dalManager.GetContext();
        var mitigation = context.Mitigations.FirstOrDefault(m => m.RiskId == id);
        if (mitigation == null)
        {
            Log.Error("Mitigation with id {Id} not found", id);
            throw new DataNotFoundException("Mitigation", id.ToString());
        }

        return mitigation;
    }

    public List<PlanningStrategy> ListStrategies()
    {
        using var context = _dalManager.GetContext();
        var strategies = context.PlanningStrategies.ToList();
        return strategies;

    }

    public List<MitigationEffort> ListEfforts()
    {
        using var context = _dalManager.GetContext();
        var efforts = context.MitigationEfforts.ToList();
        if (efforts == null)
        {
            Log.Error("Error Listing Efforts");
            throw new DataNotFoundException("MitigationEffort", "");
        }

        return efforts;
    }

    public List<MitigationCost> ListCosts()
    {
        using var context = _dalManager.GetContext();
        var costs = context.MitigationCosts.ToList();
        if (costs == null)
        {
            Log.Error("Error Listing Efforts");
            throw new DataNotFoundException("MitigationEffort", "");
        }

        return costs;
    }

    public Mitigation Create(Mitigation mitigation)
    {
        using var context = _dalManager.GetContext();

        var risk = context.Risks.FirstOrDefault(r => r.Id == mitigation.RiskId);

        if (risk == null) throw new DataNotFoundException("Risk", mitigation.RiskId.ToString());
        
        var createdMitigation = context.Mitigations.Add(mitigation);
        context.SaveChanges();
        
        risk.MitigationId = createdMitigation.Entity.Id;
        risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.MitigationPlanned);
        context.SaveChanges();
        
        return createdMitigation.Entity;
    }

    public void Save(Mitigation mitigation)
    {
        using var context = _dalManager.GetContext();
        // First let´s check if the mitigation exists
        var existingMitigation = context.Mitigations.FirstOrDefault(m => m.Id == mitigation.Id);
        if (existingMitigation == null)
        {
            Log.Error("Mitigation with id {Id} not found", mitigation.Id);
            throw new DataNotFoundException("Mitigation", mitigation.Id.ToString());
        }
        _mapper.Map(mitigation, existingMitigation);
        context.SaveChanges();

    }

    public void DeleteTeamsAssociations(int mitigationId)
    {
        using var context = _dalManager.GetContext();
        
        var mitigation = context.Mitigations.FirstOrDefault(m => m.Id == mitigationId);
        if(mitigation == null) throw new DataNotFoundException("Mitigation", mitigationId.ToString());
        var associations = context.MitigationToTeams.Where(m => m.MitigationId == mitigationId).ToList();
        foreach (var association in associations)
        {
            context.MitigationToTeams.Remove(association);
        }
        context.SaveChanges();
    }
}