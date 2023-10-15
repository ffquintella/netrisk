using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using ReactiveUI.Validation.Extensions;
using System;
using System.Reactive;
using DynamicData;
using Model;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

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
        public string StrComputer { get; } = Localizer["Computer"];
        
    #endregion
    
    #region PROPERTIES

    private string? _strOperation;
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
    
    private ObservableCollection<Host> _hosts = new();
    public ObservableCollection<Host> Hosts
    {
        get => _hosts;
        set => this.RaiseAndSetIfChanged(ref _hosts, value);
    }
    
    private Host? _selectedHost;
    public Host? SelectedHost
    {
        get => _selectedHost;
        set => this.RaiseAndSetIfChanged(ref _selectedHost, value);
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
    
    private bool _saveEnabled;
    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }

    private List<Risk>? Risks { get; set; }
    
    private IEnumerable<SelectEntity> _availableRisks =  new List<SelectEntity>();
    public IEnumerable<SelectEntity> AvailableRisks
    {
        get => _availableRisks;
        set => this.RaiseAndSetIfChanged(ref _availableRisks, value);
    }
    
    private IEnumerable<SelectEntity> _selectedRisks = new List<SelectEntity>();
    public IEnumerable<SelectEntity> SelectedRisks
    {
        get => _selectedRisks;
        set => this.RaiseAndSetIfChanged(ref _selectedRisks, value);
    }
    
    #endregion
    
    #region BUTTONS
        public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Unit, Unit> BtAddHostClicked { get; }
        public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    #endregion

    #region FIELDS
    #endregion
    
    #region SERVICES

    private ITechnologiesService TechnologiesService { get; } = GetService<ITechnologiesService>();
    private IImpactsService ImpactsService { get; } = GetService<IImpactsService>();
    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    private IRisksService RisksService { get; } = GetService<IRisksService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    
    #endregion

    public EditVulnerabilitiesDialogViewModel()
    {
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtAddHostClicked = ReactiveCommand.Create(ExecuteAddHost);
        BtCancelClicked = ReactiveCommand.Create(() => Close(new VulnerabilityDialogResult()
        {
            Action = ResultActions.Cancel
        } ));
        
        this.ValidationRule(
            viewModel => viewModel.Title, 
            val => val != null && val.Length > 0,
            Localizer["PleaseEnterAValueMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.Description, 
            val => val != null && val.Length > 0,
            Localizer["PleaseEnterAValueMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedImpact, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedTechnology, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedTeam, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);

        this.ValidationRule(
            viewModel => viewModel.SelectedHost, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.IsValid().Subscribe(observer =>
        {
            SaveEnabled = observer;
        });
        
    }

    #region METHODS

    public override Task ActivateAsync(VulnerabilityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Operation = parameter.Operation;
            Technologies = new ObservableCollection<Technology>(TechnologiesService.GetAll());
            Impacts = new ObservableCollection<LocalizableListItem>(ImpactsService.GetAll());
            Teams = new ObservableCollection<Team>(TeamsService.GetAll());
            Hosts = new ObservableCollection<Host>(HostsService.GetAll());
            LoadRisks();
            
            if (parameter.Operation == OperationType.Create)
            {
                Vulnerability = new Vulnerability();
            }
            else if (parameter.Operation == OperationType.Edit)
            {
                Vulnerability = VulnerabilitiesService.GetOne(parameter.Vulnerability.Id);
                LoadProperties();
            }
            
        });
    }

    private void LoadProperties()
    {
        
        Title = Vulnerability?.Title ?? "";
        Score = Vulnerability?.Score ?? 5;
        Description = Vulnerability?.Description ?? "";
        Solution = Vulnerability?.Solution ?? "";
        Comments = Vulnerability?.Comments ?? "";
        SelectedImpact = Impacts.FirstOrDefault(i => i.Key.ToString() == Vulnerability?.Severity);
        SelectedTechnology = Technologies.FirstOrDefault(t => t.Name == Vulnerability?.Technology);
        SelectedHost = Hosts.FirstOrDefault(h => h.Id == Vulnerability?.HostId);
        SelectedTeam = Teams.FirstOrDefault(t => t.Value == Vulnerability?.FixTeamId); 
        SelectedRisks = new ObservableCollection<SelectEntity>(
            Risks!.Where(r => 
                Vulnerability?.Risks.Any(vr => vr.Id == r.Id) ?? false).Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
        
        
        AvailableRisks = new ObservableCollection<SelectEntity>(
            Risks!.Where(r => 
                !Vulnerability?.Risks.Any(vr => vr.Id == r.Id) ?? false).Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
    }

    private void LoadRisks()
    {
        Risks = RisksService.GetAllRisks();
        AvailableRisks = new ObservableCollection<SelectEntity>(Risks.Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));
        SelectedRisks = new ObservableCollection<SelectEntity>(Risks.Where(r => Vulnerability?.Risks.Any(vr => vr.Id == r.Id) ?? false).Select(r => new SelectEntity(r.Id.ToString(), r.Subject)));

    }

    private async void ExecuteAddHost()
    {
        
        var dialogNewHost = await DialogService.ShowDialogAsync<HostDialogResult>(nameof(EditHostDialogViewModel));
        
        if(dialogNewHost == null) return;

        if (dialogNewHost.Action == ResultActions.Ok )
        {
            Hosts.Add(dialogNewHost.ResultingHost!);
        }
    }
    
    private void ExecuteSave()
    {
        if(!SelectedRisks.Any())
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["PleaseSelectAtLeastOneRiskMSG"]  ,
                    Icon = Icon.Warning,
                });
                        
            messageBoxStandardWindow.ShowAsync();
            return;
        }

        if(Vulnerability == null) Vulnerability = new Vulnerability();
        Vulnerability.Title = Title;

        var userId = AuthenticationService.AuthenticatedUserInfo!.UserId.Value;

        Vulnerability.AnalystId = userId;
        
        Vulnerability.Score = Score;
        Vulnerability.FixTeamId = SelectedTeam!.Value;
        Vulnerability.Comments = Comments;
        Vulnerability.Solution = Solution;
        Vulnerability.Description = Description;
        if (Operation == OperationType.Create) Vulnerability.Status = (ushort) IntStatus.New;
        Vulnerability.Severity = SelectedImpact!.Key.ToString();
        Vulnerability.Technology = SelectedTechnology!.Name;
        Vulnerability.HostId = SelectedHost!.Id;
        Vulnerability.Host = null;
        Vulnerability.FixTeam = null;
        
        
        //Vulnerability.Risks = Risks!.Where(r => SelectedRisks.Select(sr => sr.Key).Contains(r.Id.ToString())).ToList();

        var riskIds = Risks!.Where(r => SelectedRisks.Select(sr => sr.Key).Contains(r.Id.ToString())).Select(r => r.Id).ToList();
        
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        
        
        try
        {
            if (Operation == OperationType.Create)
            {
                var nraction = new NrAction()
                {
                    DateTime = DateTime.Now,
                    Id = 0,
                    Message = "CREATED BY: " + user,
                    UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                    ObjectType = typeof(Vulnerability).Name,
                };
                
                Vulnerability.Id = 0;
                Vulnerability = VulnerabilitiesService.Create(Vulnerability);
                VulnerabilitiesService.AddAction(Vulnerability!.Id, nraction.UserId!.Value, nraction);
            }
            else if (Operation == OperationType.Edit)
            {
                var nraction = new NrAction()
                {
                    DateTime = DateTime.Now,
                    Id = 0,
                    Message = "EDITED BY: " + user,
                    UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                    ObjectType = typeof(Vulnerability).Name,
                };
                
                var risks = SelectedRisks;
                Vulnerability.Risks.Clear();
                VulnerabilitiesService.Update(Vulnerability);
                VulnerabilitiesService.AddAction(Vulnerability!.Id, nraction.UserId!.Value, nraction);
            }

            VulnerabilitiesService.AssociateRisks(Vulnerability.Id, riskIds);
            
            // Load Vulnerability Risks
            
            Close(new VulnerabilityDialogResult()
            {
                Action = ResultActions.Ok,
                ResultingVulnerability = Vulnerability
            });

        }
        catch (Exception ex)
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorSavingVulnerabilityMSG"]  ,
                    Icon = Icon.Error,
                });
                        
            messageBoxStandardWindow.ShowAsync();
        }
        

    }

    #endregion

}