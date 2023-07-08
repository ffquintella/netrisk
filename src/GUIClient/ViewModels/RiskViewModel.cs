using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Media;
using ClientServices.Interfaces;
using GUIClient.Views;
using DAL.Entities;
using GUIClient.Models;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Enums;
using Model.Risks;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels;

public class RiskViewModel: ViewModelBase
{

    
    #region LANGUAGE-STRINGS
    public string StrRisk { get; }
    public string StrDetails { get; }
    public string StrSubject { get; }
    public string StrStatus { get; }
    public string StrSource { get; }
    public string StrCategory { get; }
    public string StrNotes { get; }
    public string StrOwner { get; }
    public string StrManager { get; }
    public string StrCreation { get; }
    public string StrSubmittedBy { get; }
    public string StrRiskType { get; }
    public string StrStatusFilter { get; }
    public string StrValue { get; }
    public string StrScoring { get; }
    public string StrProbability { get; }
    public string StrImpact { get; }
    public string StrMitigationNotPlanned { get; }
    //public string StrPlanMitigation { get; }
    public string StrMitigation { get; }
    public string StrUpdate { get; }
    public string StrStrategy { get; }
    public string StrProjected { get; }
    public string StrCost { get; }
    public string StrEffort { get; }
    public string StrClosed { get; }
    public string StrReason { get; }
    #endregion

    #region PROPERTIES
    
    private string _riskFilter = "";
    public string RiskFilter
    {
        get => _riskFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _riskFilter, value);
            ApplyFilter();
        }
    }
    
    private Hydrated.Risk? _hdRisk;
    public Hydrated.Risk? HdRisk
    {
        get => _hdRisk;
        set
        {
            this.RaiseAndSetIfChanged(ref _hdRisk, value);
            if (_likelihoods != null && _impacts != null && _hdRisk != null)
            {
                var probs = _likelihoods.FirstOrDefault(l =>
                    Math.Abs(l.Value - _hdRisk.Scoring.ClassicLikelihood) < 0.001);
                if(probs != null) Probability = probs.Name;
                var impact = _impacts.FirstOrDefault(i => Math.Abs(i.Value - _hdRisk.Scoring.ClassicImpact) < 0.001);
                if(impact != null ) Impact = impact.Name;
            }

            if (_hdRisk is { Mitigation: not null })
            {
                SelectedMitigationStrategy = Strategies!.Where(s => s.Value == _hdRisk.Mitigation.PlanningStrategy)
                    .Select(s => s.Name).FirstOrDefault()!;
                SelectedMitigationCost = Costs!.Where(c => c.Value == _hdRisk.Mitigation.MitigationCost).Select(c => c.Name)
                    .FirstOrDefault()!;
                SelectedMitigationEffort = Efforts!.Where(e => e.Value == _hdRisk.Mitigation.MitigationEffort).Select(c => c.Name)
                    .FirstOrDefault()!;
            }

        }
    }

    private string _selectedMitigationStrategy = "";

    public string SelectedMitigationStrategy
    {
        get => _selectedMitigationStrategy;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationStrategy, value);
    }

    private string _selectedMitigationCost = "";

    public string SelectedMitigationCost
    {
        get => _selectedMitigationCost;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationCost, value);
    }
    
    private string _selectedMitigationEffort = "";

    public string SelectedMitigationEffort
    {
        get => _selectedMitigationEffort;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationEffort, value);
    }
    
    private Risk? _selectedRisk;

    public Risk? SelectedRisk
    {
        get => _selectedRisk;
        set
        {
            if (value != null)
            {
                HdRisk = new Hydrated.Risk(value);
                IsMitigationVisible = HdRisk.Mitigation != null;
            }
            else
            {
                HdRisk = null;
                IsMitigationVisible = false;
            }
            this.RaiseAndSetIfChanged(ref _selectedRisk, value);
        }
    }
    private ObservableCollection<Risk>? _allRisks;
    
    public ObservableCollection<Risk>? AllRisks
    {
        get => _allRisks;
        set
        {
            Risks = value;
            this.RaiseAndSetIfChanged(ref _allRisks, value);
        }
    }

    private ObservableCollection<Risk>? _risks;
    
    public ObservableCollection<Risk>? Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }

    private bool _hasDeleteRiskPermission;

    public bool CanDeleteRisk
    {
        get => _hasDeleteRiskPermission;
        set => this.RaiseAndSetIfChanged(ref _hasDeleteRiskPermission, value);
    }

    private IImmutableSolidColorBrush _newFilterColor = Brushes.DodgerBlue;
    public IImmutableSolidColorBrush NewFilterColor
    {
        get => _newFilterColor;
        set => this.RaiseAndSetIfChanged(ref _newFilterColor, value);
    }
    
    private IImmutableSolidColorBrush _mitigationFilterColor = Brushes.DodgerBlue;
    public IImmutableSolidColorBrush MitigationFilterColor
    {
        get => _mitigationFilterColor;
        set => this.RaiseAndSetIfChanged(ref _mitigationFilterColor, value);
    }
    
    private IImmutableSolidColorBrush _reviewFilterColor = Brushes.DodgerBlue;
    public IImmutableSolidColorBrush ReviewFilterColor
    {
        get => _reviewFilterColor;
        set => this.RaiseAndSetIfChanged(ref _reviewFilterColor, value);
    }
    
    private IImmutableSolidColorBrush _closedFilterColor = Brushes.White;
    public IImmutableSolidColorBrush ClosedFilterColor
    {
        get => _closedFilterColor;
        set => this.RaiseAndSetIfChanged(ref _closedFilterColor, value);
    }

    private List<Likelihood>? _likelihoods;
    private List<Impact>? _impacts;

    private string? _probability;

    public string? Probability
    {
        get => _probability;
        set => this.RaiseAndSetIfChanged(ref _probability, value);
    }

    private string? _impact;

    public string? Impact
    {
        get => _impact;
        set => this.RaiseAndSetIfChanged(ref _impact, value);
    }

    private bool _isMitigationVisible;
    public bool IsMitigationVisible
    {
        get => _isMitigationVisible;
        set => this.RaiseAndSetIfChanged(ref _isMitigationVisible, value);
    }

    public List<PlanningStrategy>? Strategies { get; set; }
    
    public List<MitigationCost>? Costs { get; set; }
    
    public List<MitigationEffort>? Efforts { get; set; }

    public ReactiveCommand<Window, Unit> BtAddMitigationClicked { get; }
    public ReactiveCommand<Window, Unit> BtEditMitigationClicked { get; }
    public ReactiveCommand<Window, Unit> BtAddRiskClicked { get; }
    public ReactiveCommand<Window, Unit> BtEditRiskClicked { get; }
    public ReactiveCommand<Unit, Unit> BtReloadRiskClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteRiskClicked { get; }
    public ReactiveCommand<Window, Unit> BtCloseRiskClicked { get; }
    public ReactiveCommand<Unit, Unit> BtNewFilterClicked { get; }
    public ReactiveCommand<Unit, Unit> BtMitigationFilterClicked { get; }
    public ReactiveCommand<Unit, Unit> BtReviewFilterClicked { get; }
    public ReactiveCommand<Unit, Unit> BtClosedFilterClicked { get; }
    
    #endregion

    #region PRIVATE FIELDS
    private IRisksService _risksService;
    private IAuthenticationService _autenticationService;
    private IMitigationService _mitigationService;
    
    private bool _initialized;
    private List<RiskStatus> _filterStatuses;
    #endregion
    
    public RiskViewModel()
    {
        StrRisk = Localizer["Risk"];
        StrDetails= Localizer["Details"];
        StrSubject = Localizer["Subject"] + ": ";
        StrStatus = Localizer["Status"] + ": ";
        StrSource = Localizer["Source"] + ": ";
        StrCategory = Localizer["Category"] + ": ";
        StrNotes = Localizer["Notes"] + ": ";
        StrOwner = Localizer["Owner"] + ":";
        StrManager = Localizer["Manager"] + ":";
        StrCreation = Localizer["Creation"] + ":";
        StrSubmittedBy = Localizer["SubmittedBy"] + ":";
        StrRiskType = Localizer["RiskType"] ;
        StrStatusFilter = Localizer["StatusFilter"] ;
        StrValue = Localizer["Value"] + ":";
        StrScoring = Localizer["Scoring"] + ":";
        StrProbability = Localizer["Probability"] + ":";
        StrImpact = Localizer["Impact"] + ":";
        StrMitigationNotPlanned = Localizer["MitigationNotPlannedMSG"];
        //StrPlanMitigation = Localizer["PlanMitigation"];
        StrMitigation = Localizer["Mitigation"];
        StrUpdate = Localizer["Update"];
        StrStrategy = Localizer["Strategy"];
        StrProjected = Localizer["Projected"];
        StrCost = Localizer["Cost"];
        StrEffort = Localizer["Effort"];
        StrClosed = Localizer["Closed"].ToString().ToUpper();
        StrReason = Localizer["Reason"] + ":";

        _risks = new ObservableCollection<Risk>();
        
        
        BtAddMitigationClicked = ReactiveCommand.Create<Window>(ExecuteAddMitigation);
        BtEditMitigationClicked = ReactiveCommand.Create<Window>(ExecuteEditMitigation);
        BtAddRiskClicked = ReactiveCommand.Create<Window>(ExecuteAddRisk);
        BtEditRiskClicked = ReactiveCommand.Create<Window>(ExecuteEditRisk);
        BtDeleteRiskClicked = ReactiveCommand.Create(ExecuteDeleteRisk);
        BtCloseRiskClicked = ReactiveCommand.Create<Window>(ExecuteCloseRisk);
        BtReloadRiskClicked = ReactiveCommand.Create(ExecuteReloadRisk);
        BtNewFilterClicked = ReactiveCommand.Create(ApplyNewFilter);
        BtMitigationFilterClicked = ReactiveCommand.Create(ApplyMitigationFilter);
        BtReviewFilterClicked = ReactiveCommand.Create(ApplyReviewFilter);
        BtClosedFilterClicked = ReactiveCommand.Create(ApplyClosedFilter);

        _risksService = GetService<IRisksService>();
        _autenticationService = GetService<IAuthenticationService>();
        _mitigationService = GetService<IMitigationService>();

        _filterStatuses = new List<RiskStatus>()
        {
            RiskStatus.New,
            RiskStatus.ManagementReview,
            RiskStatus.MitigationPlanned
        };

        _autenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
            
            if(_autenticationService.AuthenticatedUserInfo!.UserRole == "Admin" ||  
               _autenticationService.AuthenticatedUserInfo!.UserRole == "Administrator" || 
               _autenticationService.AuthenticatedUserInfo!.UserPermissions!.Any(p => p == "delete_risk"))
                CanDeleteRisk = true;
            
        };
        
    }

    #region METHODS
    private void ApplyNewFilter()
    {
        if (_filterStatuses.Any(s => s == RiskStatus.New))
        {
            NewFilterColor = Brushes.White;
            _filterStatuses.Remove(RiskStatus.New);
            ApplyFilter();
        }
        else
        {
            NewFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.New);
            ApplyFilter();
        }
    }
    
    private void ApplyMitigationFilter()
    {
        if (_filterStatuses.Any(s => s == RiskStatus.MitigationPlanned))
        {
            MitigationFilterColor = Brushes.White;
            _filterStatuses.Remove(RiskStatus.MitigationPlanned);
            ApplyFilter();
        }
        else
        {
            MitigationFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.MitigationPlanned);
            ApplyFilter();
        }
    }
    
    private void ApplyReviewFilter()
    {
        if (_filterStatuses.Any(s => s == RiskStatus.ManagementReview))
        {
            ReviewFilterColor = Brushes.White;
            _filterStatuses.Remove(RiskStatus.ManagementReview);
            ApplyFilter();
        }
        else
        {
            ReviewFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.ManagementReview);
            ApplyFilter();
        }
    }
    
    private void ApplyClosedFilter()
    {
        if (_filterStatuses.Any(s => s == RiskStatus.Closed))
        {
            ClosedFilterColor = Brushes.White;
            _filterStatuses.Remove(RiskStatus.Closed);
            LoadRisks();
            ApplyFilter();
        }
        else
        {
            ClosedFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.Closed);
            LoadRisks(true);
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        Risks = new ObservableCollection<Risk>(_allRisks!.Where(r => r.Subject.Contains(_riskFilter) && _filterStatuses.Any(s => r.Status == RiskHelper.GetRiskStatusName(s))));
    }

    private void CleanFilters()
    {
        _filterStatuses = new List<RiskStatus>()
        {
            RiskStatus.New,
            RiskStatus.ManagementReview,
            RiskStatus.MitigationPlanned
        };
        ClosedFilterColor = Brushes.White;
        ReviewFilterColor = Brushes.DodgerBlue;
        MitigationFilterColor = Brushes.DodgerBlue;
        NewFilterColor = Brushes.DodgerBlue; 
        ApplyFilter();
        
    }
    
    private async void ExecuteAddMitigation(Window openWindow)
    {
        var dialog = new EditMitigationWindow()
        {
            DataContext = new EditMitigationViewModel(OperationType.Create, SelectedRisk!.Id),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 1050,
            //Height = 530,
            CanResize = false
        };
        await dialog.ShowDialog( openWindow );
        var selectedRiskId = SelectedRisk.Id;
        ExecuteReloadRisk();
        CleanFilters();
        SelectedRisk = Risks!.FirstOrDefault(r=>r.Id == selectedRiskId);
    }
    
    private async void ExecuteCloseRisk(Window openWindow)
    {
        var dialog = new CloseRiskWindow()
        {
            DataContext = new CloseRiskViewModel(SelectedRisk!),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 500,
            Height = 250,
            SizeToContent = SizeToContent.Height,
            CanResize = false
        };
        await dialog.ShowDialog( openWindow );
        ExecuteReloadRisk();
        CleanFilters();
    }
    
    private async void ExecuteEditMitigation(Window openWindow)
    {
        var dialog = new EditMitigationWindow()
        {
            DataContext = new EditMitigationViewModel(OperationType.Edit, SelectedRisk!.Id, HdRisk!.Mitigation),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1050,
            Height = 530,
            CanResize = false
        };
        await dialog.ShowDialog( openWindow );
        var selectedRiskId = SelectedRisk.Id;
        ExecuteReloadRisk();
        CleanFilters();
        SelectedRisk = Risks!.FirstOrDefault(r=>r.Id == selectedRiskId);
    }

    private async void ExecuteAddRisk(Window openWindow)
    {
        // OPENS a new window to create the risk
        
        var dialog = new EditRiskWindow()
        {
            DataContext = new EditRiskViewModel(OperationType.Create),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1000,
            Height = 650,
        };
        await dialog.ShowDialog( openWindow );
        AllRisks = new ObservableCollection<Risk>(_risksService.GetAllRisks());
    }
    private async void ExecuteEditRisk(Window openWindow)
    {
        if (SelectedRisk == null)
        {
            var msgSelect = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["SelectRiskMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.Show();
            return;
        }
        
        // OPENS a new window to edit the risk

        var dialog = new EditRiskWindow()
        {
            DataContext = new EditRiskViewModel(OperationType.Edit, SelectedRisk),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1000,
            Height = 650,
        };
        await dialog.ShowDialog( openWindow );
        AllRisks = new ObservableCollection<Risk>(_risksService.GetAllRisks());
    }
    private async void ExecuteDeleteRisk()
    {
        if (SelectedRisk == null)
        {
            var msgSelect = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["SelectRiskDeleteMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.Show();
            return;
        }
        var messageBoxConfirm = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["RiskDeleteConfirmationMSG"]  ,
                ButtonDefinitions = ButtonEnum.OkAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.Show();

        if (confirmation == ButtonResult.Ok)
        {
            try
            {
                _risksService.DeleteRiskScoring(SelectedRisk.Id);
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting risk score with id:{Id} details: {Details}", SelectedRisk.Id, ex.Message);
            }
            
            try
            {
                _risksService.DeleteRisk(SelectedRisk);
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting risk  with id:{Id} details: {Details}", SelectedRisk.Id, ex.Message);
            }
            
           
            
            AllRisks = new ObservableCollection<Risk>(_risksService.GetAllRisks());
        }
    }
    
    private void LoadRisks(bool includeClosed = false)
    {
        AllRisks = new ObservableCollection<Risk>(_risksService.GetAllRisks(includeClosed));
    }
    
    private void ExecuteReloadRisk()
    {
        if(_filterStatuses.Any(s => s == RiskStatus.Closed))
            LoadRisks(true);
        else
            LoadRisks();
        RiskFilter = "";
    }

    private void Initialize()
    {
        if (!_initialized)
        {
            AllRisks = new ObservableCollection<Risk>(_risksService.GetAllRisks());
            
            _impacts = _risksService.GetImpacts();
            _likelihoods = _risksService.GetProbabilities();
            Strategies = _mitigationService.GetStrategies();
            Costs = _mitigationService.GetCosts();
            Efforts = _mitigationService.GetEfforts();
            
            _initialized = true;
        }
    }
    #endregion
}