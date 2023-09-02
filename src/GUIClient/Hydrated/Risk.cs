using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Serilog;
using ClientServices.Interfaces;
using Model.DTO;
using Model.Exceptions;
using Model.Risks;
using ReactiveUI;

namespace GUIClient.Hydrated;

public class Risk: BaseHydrated
{
    private DAL.Entities.Risk _baseRisk;

    private IRisksService _risksService;
    
    private IUsersService _usersService;
    
    private IMitigationService _mitigationService;
    
    private IEntitiesService _entitiesService;
    
    public Risk(DAL.Entities.Risk risk)
    {
        _baseRisk = risk;
        _risksService = GetService<IRisksService>();
        _usersService = GetService<IUsersService>();
        _mitigationService = GetService<IMitigationService>();
        _entitiesService = GetService<IEntitiesService>();
        
        var riskEntity = _risksService.GetEntityIdFromRisk(_baseRisk.Id);

        if (riskEntity != null)
        {
            Entity = _entitiesService.GetEntity(riskEntity.Value);
            EntityName = Entity?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value;
        }
    }

    public Entity? Entity { get; }
    
    public string? EntityName { get; }
    public int Id => _baseRisk.Id;
    
    public string Status => _baseRisk.Status;

    public string Subject => _baseRisk.Subject;

    public string Source => _risksService.GetRiskSource(_baseRisk.Source);

    public string Category => _risksService.GetRiskCategory(_baseRisk.Category);

    public string Owner => _usersService.GetUserName(_baseRisk.Owner);
    
    public string SubmittedBy => _usersService.GetUserName(_baseRisk.SubmittedBy);

    
    
    public RiskScoring Scoring => _risksService.GetRiskScoring(_baseRisk.Id);
    
    public List<FileListing> Files => _risksService.GetRiskFiles(_baseRisk.Id);

    private Closure? _closure;
    public Closure? Closure
    {
        get
        {
            if(_closure != null) return _closure;
            if (_baseRisk.Status == RiskHelper.GetRiskStatusName(RiskStatus.Closed))
            {
                _closure = _risksService.GetRiskClosure(_baseRisk.Id);
                return _closure;
            }

            return null;
        }
    }
    
    private List<CloseReason>? _closeReasons;
    public string ClosureReason
    {
        get
        {
            if (_closure == null) return "";
            else
            {
                if(_closeReasons == null) _closeReasons = _risksService.GetRiskCloseReasons();
                var reason = _closeReasons.Where(cr => cr.Value == _closure.CloseReason).FirstOrDefault();
                if(reason == null) throw new DataNotFoundException("ClosureReason", _closure.CloseReason.ToString());
                return reason.Name;
            }
        }
    }

    private Mitigation? _mitigation;
    public Mitigation? Mitigation
    {
        get
        {
            if (_baseRisk.Status != "New")
            {
                if (_mitigation == null || _mitigation.RiskId != _baseRisk.Id)
                {
                    _mitigation = _mitigationService.GetByRiskId(_baseRisk.Id);
                    if(_mitigation != null) this.RaisePropertyChanged();
                }
            }
            else
            {
                _mitigation = null;
            }

            return _mitigation;
        }
    }

    public string Manager
    {
        get
        {
            try
            {
                return _usersService.GetUserName(_baseRisk.Manager);
            }
            catch (Exception ex)
            {
                Log.Warning("Error loading manager: {0}", ex.Message);
                return "";
            }
        }
    } 
    
    public List<RiskCatalog> Types => _risksService.GetRiskTypes(_baseRisk.RiskCatalogMapping);

}