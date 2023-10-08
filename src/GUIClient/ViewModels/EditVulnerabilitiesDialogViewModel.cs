using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaExtraControls.Models;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Globalization;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class EditVulnerabilitiesDialogViewModel: ParameterizedDialogViewModelBaseAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>
{
    #region LANGUAGE
        public string StrVulnerability { get; } = Localizer["Vulnerability"];
        public string StrTile { get; } = Localizer["Title"];
        public string StrScore { get; } = Localizer["Score"];
        public string StrImpact { get; } = Localizer["Impact"];
        public string StrTeam { get; } = Localizer["Team"];
        public string StrTechnologies { get; } = Localizer["Technologies"];
        public string StrDescription { get; } = Localizer["Description"];
        public string StrSolution { get; } = Localizer["Solution"];
        public string StrComments { get; } = Localizer["Comments"];
        public string StrRiskFilter { get; } = Localizer["RiskFilter"];
        public string StrRisks { get; } = Localizer["Risks"];
        public string StrSave { get; } = Localizer["Save"];
        public string StrCancel { get; } = Localizer["Cancel"];
        
    #endregion
    
    #region PROPERTIES

    private string? _strOperation = null;
    public string? StrOperation
    {
        get => _strOperation;
        set => this.RaiseAndSetIfChanged(ref _strOperation, value);
    }
    
    private OperationType _operation;
    private OperationType Operation
    {
        get => _operation;
        set
        {
            if(value == OperationType.Create) StrOperation = Localizer["Create"];
            else if(value == OperationType.Edit) StrOperation = Localizer["Edit"];
            this.RaiseAndSetIfChanged(ref _operation, value);
        }
    }
    
    private ObservableCollection<Technology> _technologies = new();
    public ObservableCollection<Technology> Technologies
    {
        get => _technologies;
        set => this.RaiseAndSetIfChanged(ref _technologies, value);
    }
    
    private ObservableCollection<LocalizableListItem> _impacts = new();
    public ObservableCollection<LocalizableListItem> Impacts
    {
        get => _impacts;
        set => this.RaiseAndSetIfChanged(ref _impacts, value);
    }
    
    private ObservableCollection<Team> _teams = new();
    public ObservableCollection<Team> Teams
    {
        get => _teams;
        set => this.RaiseAndSetIfChanged(ref _teams, value);
    }
    
    private Vulnerability? _vulnerability;
    public Vulnerability? Vulnerability
    {
        get => _vulnerability;
        set => this.RaiseAndSetIfChanged(ref _vulnerability, value);
    }
    
    private string _title = "";
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }
    
    private double _score = 5;
    public double Score
    {
        get => _score;
        set => this.RaiseAndSetIfChanged(ref _score, value);
    }
    
    private string _description = "";
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
    
    private string _solution = "";
    public string Solution
    {
        get => _solution;
        set => this.RaiseAndSetIfChanged(ref _solution, value);
    }

    private string _comments = "";
    public string Comments
    {
        get => _comments;
        set => this.RaiseAndSetIfChanged(ref _comments, value);
    }
    
    private LocalizableListItem? _selectedImpact;
    public LocalizableListItem? SelectedImpact
    {
        get => _selectedImpact;
        set => this.RaiseAndSetIfChanged(ref _selectedImpact, value);
    }
    
    private Technology? _selectedTechnology;
    public Technology? SelectedTechnology
    {
        get => _selectedTechnology;
        set => this.RaiseAndSetIfChanged(ref _selectedTechnology, value);
    }
    
    private Team? _selectedTeam;
    public Team? SelectedTeam
    {
        get => _selectedTeam;
        set => this.RaiseAndSetIfChanged(ref _selectedTeam, value);
    }

    private string? _riskFilter;
    public string? RiskFilter
    {
        get => _riskFilter;
        set
        {
            // TODO: Fix MS bug
            if (value != null)
            {
                AvailableRisks = new ObservableCollection<SelectEntity>(
                    Risks!
                        .Where(r=> !SelectedRisks.Select(sr => sr.Key).Contains(r.Id.ToString()) )    
                        .Where(r => r.Subject.ToLower().Contains(value.ToLower())).Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
            }
            else
            {
                AvailableRisks = new ObservableCollection<SelectEntity>(
                    Risks!
                        .Where(r=> !SelectedRisks.Select(sr => sr.Key).Contains(r.Id.ToString()) )    
                        .Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
            }
            this.RaiseAndSetIfChanged(ref _riskFilter, value);
        }
    }

    private List<Risk>? Risks { get; set; }
    
    private ObservableCollection<SelectEntity>? _availableRisks;
    public ObservableCollection<SelectEntity>? AvailableRisks
    {
        get => _availableRisks;
        set => this.RaiseAndSetIfChanged(ref _availableRisks, value);
    }
    
    private ObservableCollection<SelectEntity> _selectedRisks = new();
    public ObservableCollection<SelectEntity> SelectedRisks
    {
        get => _selectedRisks;
        set => this.RaiseAndSetIfChanged(ref _selectedRisks, value);
    }
    
    #endregion
    
    #region BUTTONS
    #endregion

    #region FIELDS
    #endregion
    
    #region SERVICES

    private ITechnologiesService TechnologiesService { get; } = GetService<ITechnologiesService>();
    private IImpactsService ImpactsService { get; } = GetService<IImpactsService>();
    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    
    private IRisksService RisksService { get; } = GetService<IRisksService>();
    #endregion


    #region METHODS

    public override Task ActivateAsync(VulnerabilityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Operation = parameter.Operation;
            Technologies = new ObservableCollection<Technology>(TechnologiesService.GetAll());
            Impacts = new ObservableCollection<LocalizableListItem>(ImpactsService.GetAll());
            Teams = new ObservableCollection<Team>(TeamsService.GetAll());
            LoadRisks();
            
            if (parameter.Operation == OperationType.Create)
            {
                Vulnerability = new Vulnerability();
            }
            else if (parameter.Operation == OperationType.Edit)
            {
                Vulnerability = parameter.Vulnerability;
                LoadProperties();
            }
            
        });
    }

    private void LoadProperties()
    {
        Title = Vulnerability?.Title ?? "";
    }

    private void LoadRisks()
    {
        Risks = RisksService.GetAllRisks();
        AvailableRisks = new ObservableCollection<SelectEntity>(Risks.Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
        SelectedRisks = new ObservableCollection<SelectEntity>(Risks.Where(r => Vulnerability?.Risks.Any(vr => vr.Id == r.Id) ?? false).Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));

    }

    #endregion

}