using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DAL.Entities;
using Serilog;
using ClientServices.Interfaces;
using GUIClient.Tools;
using Model.DTO;
using Model.Exceptions;
using Model.Risks;
using ReactiveUI;

namespace GUIClient.Hydrated;

public class Risk : BaseHydrated
{
    #region FIELDS

    private readonly DAL.Entities.Risk _baseRisk;

    public int Id => _baseRisk.Id;
    public string Status => _baseRisk.Status;
    public string Subject => _baseRisk.Subject;
    
    #endregion

    #region PROPERTIES
    
    private string _owner = String.Empty;

    public string Owner
    {
        get => _owner;
        set => this.RaiseAndSetIfChanged(ref _owner, value);
    } 
        
    private string _submittedBy = String.Empty;

    public string SubmittedBy
    {
        get => _submittedBy;
        set => this.RaiseAndSetIfChanged(ref _submittedBy, value);
    }
    
    private List<FileListing> _files = new();

    public List<FileListing> Files
    {
        get => _files;
        set => this.RaiseAndSetIfChanged(ref _files, value);
    } 
    
    public Entity? Entity { get; }
    public string? EntityName { get; }
    
    private string _source = String.Empty;

    public string Source
    {
        get => _source;
        set => this.RaiseAndSetIfChanged(ref _source, value);
    }
    
    private string _category = String.Empty;

    public string Category
    {
        get => _category;
        set => this.RaiseAndSetIfChanged(ref _category, value);
    }
 
    public List<RiskCatalog> ImpactTypes => _baseRisk.RiskCatalogs.ToList();
    
    private MgmtReview? _lastReview;

    public MgmtReview? LastReview
    {
        get => _lastReview;
        set
        {
            if (_lastReview != value)
            {
                this.RaiseAndSetIfChanged(ref _lastReview, value);
                OnRiskPropertyChanged(nameof(LastReview));
            }else this.RaiseAndSetIfChanged(ref _lastReview, value);
        }
    }

    private string _likelihood = String.Empty;

    public string Likelihood
    {
        get => _likelihood;
        set => this.RaiseAndSetIfChanged(ref _likelihood, value);
    }
    
    private string _impact = String.Empty;

    public string Impact
    {
        get => _impact;
        set => this.RaiseAndSetIfChanged(ref _impact, value);
    }
    
    
    private RiskScoring _scoring = new RiskScoring();
    public RiskScoring Scoring
    {
        get => _scoring;
        set
        {
            if (_scoring != value)
            {
                this.RaiseAndSetIfChanged(ref _scoring, value);
                OnRiskPropertyChanged(nameof(Scoring));
            }else this.RaiseAndSetIfChanged(ref _scoring, value);
        }
    }
    
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
            _closeReasons ??= _risksService.GetRiskCloseReasons();
            var reason = _closeReasons.FirstOrDefault(cr => cr.Value == _closure.CloseReason);
            if(reason == null) throw new DataNotFoundException("ClosureReason", _closure.CloseReason.ToString());
            return reason.Name;
        }
    }

    private Mitigation? _mitigation;

    public Mitigation? Mitigation
    {
        get => _mitigation;
        set
        {
            if (_mitigation != value)
            {
                this.RaiseAndSetIfChanged(ref _mitigation, value);
                OnRiskPropertyChanged(nameof(Mitigation));
            }else this.RaiseAndSetIfChanged(ref _mitigation, value);
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
                Log.Warning("Error loading manager: {Message}", ex.Message);
                return "";
            }
        }
    } 
    #endregion
    
    #region SERVICES
    private readonly IRisksService _risksService;
    private readonly IUsersService _usersService;
    private readonly IMitigationService _mitigationService;
    private readonly ConstantManager _constantManager;
    
    #endregion
    
    #region EVENT HANDLER

    public event PropertyChangedEventHandler? RiskPropertyChanged;

        protected virtual void OnRiskPropertyChanged(string propertyName)
        {
            RiskPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    #endregion
    
    #region CONSTRUCTOR

    public Risk(DAL.Entities.Risk risk)
    {
        _baseRisk = risk;
        _risksService = GetService<IRisksService>();
        _usersService = GetService<IUsersService>();
        _mitigationService = GetService<IMitigationService>();
        _constantManager = GetService<ConstantManager>();
        var entitiesService = GetService<IEntitiesService>();
        
        var riskEntity = _risksService.GetEntityIdFromRisk(_baseRisk.Id);

        if (riskEntity != null)
        {
            Entity = entitiesService.GetEntity(riskEntity.Value);
            EntityName = Entity?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value;
        }
        
        LoadData();
    }
    
    #endregion

    #region METHODS

    private async void LoadData()
    {
        
        Scoring = await _risksService.GetRiskScoringAsync(_baseRisk.Id);
        
        LoadMitigation();
        
        var like = _constantManager.Likelihoods!.FirstOrDefault(l =>
            Math.Abs(l.Value - Scoring.ClassicLikelihood) < 0.001);
        if (like != null) Likelihood = like.Name!;
        
        var impact =
            _constantManager.Impacts!.FirstOrDefault(i => Math.Abs(i.Value - Scoring.ClassicImpact) < 0.001);
        if (impact != null) Impact = impact.Name!;
        
        LastReview = await _risksService.GetRiskLastMgmtReviewAsync(_baseRisk.Id);
        
        Source = _baseRisk.Source != null ? await _risksService.GetRiskSourceAsync(_baseRisk.Source.Value) : "";
        Category = _baseRisk.Category != null ? await _risksService.GetRiskCategoryAsync(_baseRisk.Category.Value) : "";
        
        Owner = await _usersService.GetUserNameAsync(_baseRisk.Owner);
        SubmittedBy = await _usersService.GetUserNameAsync(_baseRisk.SubmittedBy);
        
        Files = await _risksService.GetRiskFilesAsync(_baseRisk.Id);
        
        this.RaisePropertyChanged(nameof(Scoring));
        this.RaisePropertyChanged(nameof(Closure));
        this.RaisePropertyChanged(nameof(ClosureReason));
        this.RaisePropertyChanged(nameof(Mitigation));
        this.RaisePropertyChanged(nameof(Manager));
    }

    private async void LoadMitigation()
    {
        if (_baseRisk.Status != "New")
        {
            if (Mitigation == null || Mitigation.RiskId != _baseRisk.Id)
            {
                Mitigation = await _mitigationService.GetByRiskIdAsync(_baseRisk.Id);
            }
        }
        else
        {
            Mitigation = null;
        }

    }

    #endregion
    
}