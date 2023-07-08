using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using DynamicData.Binding;
using GUIClient.Models;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Enums;
using Model.DTO;
using Model.Exceptions;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

namespace GUIClient.ViewModels;

public class EditMitigationViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrMitigation { get; }
    public string StrSubmissionDate { get; }
    
    public string StrSolution { get; }
    public string StrPlannedDate { get; }
    
    public string StrPlanningStrategy { get; }
    
    public string StrSecurityRequirements { get; }
    
    public string StrMitigationEffort { get; }
    
    public string StrMitigationCost { get; }
    public string StrSecurityRecommendation { get; }
    
    public string StrMitigationOwner { get; }
    
    public string StrMitigationTeam { get; }
    
    public string StrMitigationPercent { get; }
    
    public string StrDocumentation { get; }
    
    public string StrSave { get; }
    public string StrCancel { get; }
    
    #endregion

    #region INTERNAL FIELDS

    private readonly OperationType _operationType;
    private Mitigation? _mitigation;
    private int _riskId;
    private readonly IMitigationService _mitigationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ITeamsService _teamsService;
    private readonly IUsersService _usersService;

    #endregion

    public EditMitigationViewModel(OperationType operation, int? riskId,  Mitigation? mitigation = null)
    {
        if (riskId == null)
            throw new InvalidParameterException("riskId", "RiskId cannot be null");
        _riskId = riskId.Value;
        
        _operationType = operation;
        
        if (_operationType == OperationType.Edit && mitigation == null)
        {
            throw new InvalidParameterException("mitigation", "Mitigation cannot be null on edit operations");
        }
        
        
        _mitigation = _operationType == OperationType.Create ? new Mitigation() : mitigation;
        
        StrMitigation = Localizer["Mitigation"];
        StrSubmissionDate = Localizer["SubmissionDate"] + ":";
        StrSolution = Localizer["Solution"] + ":";
        StrPlannedDate = Localizer["PlannedDate"] + ":";
        StrPlanningStrategy = Localizer["PlanningStrategy"] + ":";
        StrSecurityRequirements = Localizer["SecurityRequirements"] + ":";
        StrMitigationEffort = Localizer["MitigationEffort"] + ":";
        StrMitigationCost = Localizer["MitigationCost"] + ":";
        StrSecurityRecommendation = Localizer["SecurityRecommendation"] + ":";
        StrMitigationOwner = Localizer["MitigationOwner"] + ":";
        StrMitigationTeam = Localizer["MitigationTeam"] + ":";
        StrMitigationPercent = Localizer["MitigationPercent"] + ":";
        StrDocumentation = Localizer["Documentation"] + ":";
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        
        _mitigationService = GetService<IMitigationService>();
        _usersService = GetService<IUsersService>();
        _authenticationService = GetService<IAuthenticationService>();
        _teamsService = GetService<ITeamsService>();

        _planningStrategies = new ObservableCollection<PlanningStrategy>(_mitigationService.GetStrategies()!);
        _mitigationCosts = new ObservableCollection<MitigationCost>(_mitigationService.GetCosts()!);
        _mitigationEfforts = new ObservableCollection<MitigationEffort>(_mitigationService.GetEfforts()!);
        _users = new ObservableCollection<UserListing>(_usersService.ListUsers());
        _teams = new ObservableCollection<Team>(_teamsService.GetAll());
        
        if (_operationType == OperationType.Create)
        {
            SubmissionDate = new DateTimeOffset(DateTime.Now);
            Solution = "";
            PlannedDate = new DateTimeOffset(DateTime.Now + TimeSpan.FromDays(2));
            _mitigation!.LastUpdate = DateTime.Now;
            SecurityRequirements = "";
            RecommendedSolution = "";
            SelectedMitigationOwner = Users.ToList().Find(x => x.Id == _authenticationService.AuthenticatedUserInfo!.UserId);
            MitigationPercent = 0;
        }
        else
        {
            SubmissionDate = new DateTimeOffset(_mitigation!.SubmissionDate);
            Solution = _mitigation!.CurrentSolution;
            PlannedDate = new DateTimeOffset(_mitigation!.PlanningDate.ToDateTime(new TimeOnly(0,0)));
            _mitigation.LastUpdate = DateTime.Now;
            SelectedPlanningStrategy = PlanningStrategies.ToList().Find(x => x.Value == _mitigation.PlanningStrategy);
            SecurityRequirements = _mitigation.SecurityRequirements;
            SelectedMitigationEffort = MitigationEfforts.ToList().Find(x => x.Value == _mitigation.MitigationEffort);
            SelectedMitigationCost = MitigationCosts.ToList().Find(x => x.Value == _mitigation.MitigationCost);
            RecommendedSolution = _mitigation.SecurityRecommendations;
            SelectedMitigationOwner = Users.ToList().Find(x => x.Id == _mitigation.MitigationOwner);

            var mitigationTeams = _mitigationService.GetTeamsById(_mitigation.Id);
            if (mitigationTeams != null)
                SelectedMitigationTeam =
                    Teams.FirstOrDefault(at => mitigationTeams.Select(mt => mt.Value).Contains(at.Value));
            
            MitigationPercent = _mitigation.MitigationPercent;
        }
        
        BtSaveClicked = ReactiveCommand.Create<Window>(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create<Window>(ExecuteCancel);
        
        #region VALIDATION
        
        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationOwner, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationCost, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationEffort, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedPlanningStrategy, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
       
        this.IsValid()
            .Subscribe(x =>
            {
                SaveEnabled = x;
            });
        
        #endregion
        
    }

    #region PROPERTIES

        public ReactiveCommand<Window, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
        
        private bool _saveEnabled = true;
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }

        private ObservableCollection<Team> _teams;
        public ObservableCollection<Team> Teams
        {
            get => _teams;
            set => this.RaiseAndSetIfChanged(ref _teams, value);
        }

        //public List<Team> Teams => _teamsService.GetAll();
        
        private Team? _selectedMitigationTeam;
        public Team? SelectedMitigationTeam
        {
            get => _selectedMitigationTeam;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationTeam, value);
        }

        private ObservableCollection<UserListing> _users;
        public ObservableCollection<UserListing> Users
        {
            get => _users;
            set => this.RaiseAndSetIfChanged(ref _users, value);
        }

        //public List<UserListing> Users => _usersService.ListUsers();

        private UserListing? _selectedMitigationOwner;
        public UserListing? SelectedMitigationOwner
        {
            get => _selectedMitigationOwner;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationOwner, value);
        }
        
        private string _recommendedSolution = "";
        public string RecommendedSolution
        {
            get => _recommendedSolution;
            set => this.RaiseAndSetIfChanged(ref _recommendedSolution, value);
        }

        private ObservableCollection<MitigationEffort> _mitigationEfforts;
        public ObservableCollection<MitigationEffort> MitigationEfforts
        {
            get => _mitigationEfforts;
            set => this.RaiseAndSetIfChanged(ref _mitigationEfforts, value);
        }

        //public List<MitigationEffort> MitigationEfforts => _mitigationService.GetEfforts()!;
        
        private MitigationEffort? _selectedMitigationEffort;
        public MitigationEffort? SelectedMitigationEffort
        {
            get => _selectedMitigationEffort;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationEffort, value);
        }
        
        private ObservableCollection<MitigationCost> _mitigationCosts;
        public ObservableCollection<MitigationCost> MitigationCosts 
        {
            get => _mitigationCosts;
            set => this.RaiseAndSetIfChanged(ref _mitigationCosts, value);
        }
        
        //public List<MitigationCost> MitigationCosts => _mitigationService.GetCosts()!;
        
        
        
        private MitigationCost? _selectedMitigationCost;
        public MitigationCost? SelectedMitigationCost
        {
            get => _selectedMitigationCost;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationCost, value);
        }


        private ObservableCollection<PlanningStrategy> _planningStrategies;

        public ObservableCollection<PlanningStrategy> PlanningStrategies
        {
            get => _planningStrategies;
            set => this.RaiseAndSetIfChanged(ref _planningStrategies, value);
        }

        private PlanningStrategy? _selectedPlanningStrategy;
        public PlanningStrategy? SelectedPlanningStrategy
        {
            get => _selectedPlanningStrategy;
            set => this.RaiseAndSetIfChanged(ref _selectedPlanningStrategy, value);
        }
        
        private DateTimeOffset _submissionDate;
        public DateTimeOffset SubmissionDate
        {
            get => _submissionDate;
            set => this.RaiseAndSetIfChanged(ref _submissionDate, value);
        }
        
        private string _solution = "";
        public string Solution
        {
            get => _solution;
            set => this.RaiseAndSetIfChanged(ref _solution, value);
        }
        
        private string _securityRequirements = "";
        public string SecurityRequirements
        {
            get => _securityRequirements;
            set => this.RaiseAndSetIfChanged(ref _securityRequirements, value);
        }
        
        private DateTimeOffset _plannedDate;
        public DateTimeOffset PlannedDate
        {
            get => _plannedDate;
            set => this.RaiseAndSetIfChanged(ref _plannedDate, value);
        }
    
        private decimal _mitigationPercent = 0;
        public decimal MitigationPercent
        {
            get => _mitigationPercent;
            set => this.RaiseAndSetIfChanged(ref _mitigationPercent, value);
        }

    #endregion
    
    #region METHODS

    private async void ExecuteSave(Window baseWindow)
    {
        SyncMitigation();
        if (_operationType == OperationType.Create)
        {
            try
            {
                var newMitigation = _mitigationService.Create(_mitigation!);
                if (newMitigation != null)
                {
                    try
                    {
                        _mitigationService.AssociateMitigationToTeam(newMitigation.Id, SelectedMitigationTeam!.Value);
                        baseWindow.Close();
                    }catch(Exception e)
                    {
                        Logger.Error("Error associating mitigation to team: {Message}", e.Message);

                    }
                }
            }catch(Exception e)
            {
                Logger.Error("Error creating mitigation: {Message}", e.Message);
                
                var msgError = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorMitigationMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.Show();
                
            }

        }
        else
        {
            try
            {
                if(_mitigation == null) throw new InvalidParameterException("_mitigation", "Mitigation is null");
                _mitigationService.Save(_mitigation);
                _mitigationService.DeleteTeamsAssociations(_mitigation.Id);
                _mitigationService.AssociateMitigationToTeam(_mitigation.Id, SelectedMitigationTeam!.Value);
                baseWindow.Close();

            }catch(Exception e)
            {
                Logger.Error("Error saving mitigation: {Message}", e.Message);
                
                var msgError = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorMitigationMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.Show();
                
            }
        }
    }

    private void SyncMitigation()
    {
        _mitigation ??= new Mitigation();
        _mitigation.RiskId = _riskId;
        _mitigation.MitigationCost = SelectedMitigationCost!.Value;
        _mitigation.MitigationEffort = SelectedMitigationEffort!.Value;
        _mitigation.MitigationOwner = SelectedMitigationOwner!.Id;
        _mitigation.PlanningStrategy = SelectedPlanningStrategy!.Value;
        _mitigation.PlanningDate = new DateOnly(PlannedDate.DateTime.Year, PlannedDate.DateTime.Month, PlannedDate.DateTime.Day);
        _mitigation.SecurityRecommendations = RecommendedSolution;
        _mitigation.SecurityRequirements = SecurityRequirements;
        _mitigation.CurrentSolution = Solution;
        _mitigation.SubmissionDate = SubmissionDate.DateTime;
        _mitigation.MitigationPercent = Convert.ToInt32(MitigationPercent);
    }
    
    private  void ExecuteCancel(Window baseWindow)
    {
        baseWindow.Close();
    }

    #endregion
}