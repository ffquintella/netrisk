using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using GUIClient.Views;
using DAL.Entities;
using DynamicData;
using GUIClient.Models;
using GUIClient.Models.Events;
using Model.DTO;
using Model.Risks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;
using Tools.Risks;

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
    public string StrImpactTypes { get; }
    public string StrStatusFilter { get; }
    public string StrValue { get; }
    public string StrScoring { get; }
    public string StrProbability { get; }
    public string StrImpact { get; }
    public string StrMitigationNotPlanned { get; }
    public string StrMitigation { get; }
    public string StrUpdate { get; }
    public string StrStrategy { get; }
    public string StrProjected { get; }
    public string StrCost { get; }
    public string StrEffort { get; }
    public string StrClosed { get; }
    public string StrReason { get; }
    public string StrFiles { get; }
    public string StrEntity { get; }
    public string StrSaveDocumentMsg { get; }
    public string StrAddDocumentMsg { get; }
    public string StrNew { get; }
    public string StrMitigationPlanned { get; }
    public string StrManagerReviewed { get; }
    public string StrReviewNotDonne { get; }
    public string StrLastReview { get; }
    public string StrNext { get; }
    public string StrReviewDecision { get; }
    public string StrNextStep { get; }
    public string StrVulnerabilities { get; } = Localizer["Vulnerabilities"];
    public string StrTitle { get; } = Localizer["Title"];
    public string StrScore { get; } = Localizer["Score"];
    public string StrFirstDetection { get; } = Localizer["FirstDetection"];
    public string StrFixTeam { get; } = Localizer["FixTeam"];
    public string StrAnalyst { get; } = Localizer["Analyst"];
    public string StrContributingRisk { get; } = Localizer["ContributingRisk"] + ": ";
    public string StrTotalScore { get; } = Localizer["TotalScore"] + ": ";
    public string StrNoIRPFound { get; } = Localizer["NoIRPFound"] ;
    public string StrApproved { get; } = Localizer["Approved"] + ": ";
    
    
    #endregion

    #region PROPERTIES

    public Window? ParentWindow
    {
        get { return WindowsManager.AllWindows.Find(w => w is MainWindow); }
    }


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
            if (_hdRisk != value)
            {
                // Unsubscribe from the old risk's PropertyChanged event
                if (_hdRisk != null)
                {
                    _hdRisk.RiskPropertyChanged -= Risk_PropertyChanged;
                }

                this.RaiseAndSetIfChanged(ref _hdRisk, value);

                // Subscribe to the new risk's PropertyChanged event
                if (_hdRisk != null)
                {
                    _hdRisk.RiskPropertyChanged += Risk_PropertyChanged;
                }
            }


            
        }
    }

    private string _selectedMitigationStrategy = "";

    public string SelectedMitigationStrategy
    {
        get => _selectedMitigationStrategy;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationStrategy, value);
    }

    private MgmtReview? _lastReview;
    public MgmtReview? LastReview
    {
        get => _lastReview!;
        set
        {
            if (value != null)
            {
                SelectedReviewer = UsersService.GetUserName(value.Reviewer);
            }else SelectedReviewer = null;
            this.RaiseAndSetIfChanged(ref _lastReview, value);
        }
    }

    private string _selectedMitigationCost = "";

    public string SelectedMitigationCost
    {
        get => _selectedMitigationCost;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationCost, value);
    }
    
    private int _selectedMitigationCostId = 0;

    public int SelectedMitigationCostId
    {
        get => _selectedMitigationCostId;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationCostId, value);
    }
    
    private bool _loadingSpinner = false;

    public bool LoadingSpinner
    {
        get => _loadingSpinner;
        set => this.RaiseAndSetIfChanged(ref _loadingSpinner, value);
    }
    
    private string _selectedMitigationEffort = "";

    public string SelectedMitigationEffort
    {
        get => _selectedMitigationEffort;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationEffort, value);
    }
    
    private int _selectedMitigationEffortId = 0;

    public int SelectedMitigationEffortId
    {
        get => _selectedMitigationEffortId;
        set => this.RaiseAndSetIfChanged(ref _selectedMitigationEffortId, value);
    }

    private string? _selectedReviewer;

    public string? SelectedReviewer
    {
        get => _selectedReviewer;
        set => this.RaiseAndSetIfChanged(ref _selectedReviewer, value);
    }

    private bool _selectedRiskHasIncidentResponsePlan;
    public bool SelectedRiskHasIncidentResponsePlan
    {
        get => _selectedRiskHasIncidentResponsePlan;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskHasIncidentResponsePlan, value);
    }
    
    private IncidentResponsePlan? _selectedRiskIncidentResponsePlan;
    public IncidentResponsePlan? SelectedRiskIncidentResponsePlan
    {
        get => _selectedRiskIncidentResponsePlan;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskIncidentResponsePlan, value);
    }
    
    private int? _selectedRiskId;
    public int? SelectedRiskId
    {
        get => _selectedRiskId;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskId, value);
    }

    private string? _selectedRiskCtrlNumber;
    public string? SelectedRiskCtrlNumber
    {
        get => _selectedRiskCtrlNumber;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskCtrlNumber, value);
    }

    private string? _selectedRiskStatus;
    public string? SelectedRiskStatus
    {
        get => _selectedRiskStatus;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskStatus, value);
    }

    private DateTime? _selectedRiskSubmissionDate;
    public DateTime? SelectedRiskSubmissionDate
    {
        get => _selectedRiskSubmissionDate;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskSubmissionDate, value);
    }

    private DateTime? _irpDate;
    public DateTime? IrpDate
    {
        get => _irpDate;
        set => this.RaiseAndSetIfChanged(ref _irpDate, value);
    }

    private bool _irpIsApproved;
    public bool IrpIsApproved
    {
        get => _irpIsApproved;
        set => this.RaiseAndSetIfChanged(ref _irpIsApproved, value);
    }
    
    private Risk? _selectedRisk;
    public Risk? SelectedRisk
    {
        get => _selectedRisk;
        set
        {
            LoadingSpinner = true;
            Task.Run(async () =>
            {
                if (value != null)
                {
                    HdRisk = new Hydrated.Risk(value);

                    if (HdRisk.Mitigation == null) IsMitigationVisible = false;
                    if (HdRisk.LastReview == null) HasReviews = false;
                    
                    SelectedRiskIncidentResponsePlan = await RisksService.GetIncidentResponsePlanAsync(value.Id);
                    IrpDate = SelectedRiskIncidentResponsePlan?.LastUpdate;
                    IrpIsApproved = SelectedRiskIncidentResponsePlan?.HasBeenApproved ?? false;
                    
                    SelectedRiskHasIncidentResponsePlan = SelectedRiskIncidentResponsePlan != null;
                    SelectedVulnerabilities = new ObservableCollection<Vulnerability>(await RisksService.GetOpenVulnerabilitiesAsync(value.Id));
                    
                    SelectedRiskId = value.Id;
                    SelectedRiskCtrlNumber = value.ControlNumber;
                    SelectedRiskStatus = value.Status;
                    SelectedRiskSubmissionDate = value.SubmissionDate;

                }
                else
                {
                    HdRisk = null;
                    IsMitigationVisible = false;
                    HasReviews = false;
                    SelectedReviewer = null;
                    LastReview = null;
                    SelectedRiskId = null;
                    SelectedRiskCtrlNumber = null;
                    SelectedRiskStatus = null;
                    SelectedRiskSubmissionDate = null;
                    IrpDate = null;
                    IrpIsApproved = false;
                }
                
            }).ContinueWith( _ =>
            {
                LoadingSpinner = false;
            });
            
            this.RaiseAndSetIfChanged(ref _selectedRisk, value);
        }
    }
    
    private float _totalRiskScore;
    
    public float TotalRiskScore
    {
        get => _totalRiskScore;
        set => this.RaiseAndSetIfChanged(ref _totalRiskScore, value);
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

    private ObservableCollection<Risk> _risks = new ();
    
    public ObservableCollection<Risk> Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }

    private ObservableCollection<Vulnerability>? _selectedVulnerabilities;

    public ObservableCollection<Vulnerability>? SelectedVulnerabilities
    {
        get => _selectedVulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilities, value);
    }
    
    private ObservableCollection<FileListing>? _selectedRiskFiles;
    
    public ObservableCollection<FileListing>? SelectedRiskFiles
    {
        get => _selectedRiskFiles;
        set => this.RaiseAndSetIfChanged(ref _selectedRiskFiles, value);
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



    private bool _isMitigationVisible;
    public bool IsMitigationVisible
    {
        get => _isMitigationVisible;
        set => this.RaiseAndSetIfChanged(ref _isMitigationVisible, value);
    }
    
    private bool _hasReviews;
    public bool HasReviews
    {
        get => _hasReviews;
        set => this.RaiseAndSetIfChanged(ref _hasReviews, value);
    }

    private List<PlanningStrategy>? Strategies { get; set; }

    private List<MitigationCost>? Costs { get; set; }

    private List<MitigationEffort>? Efforts { get; set; }

    #endregion
    
    #region BUTTONS
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
    public ReactiveCommand<FileListing, Unit> BtFileDownloadClicked { get; }
    public ReactiveCommand<FileListing, Unit> BtFileDeleteClicked { get; }
    public ReactiveCommand<Unit, Unit> BtFileAddClicked { get; }
    public ReactiveCommand<Unit, Unit> BtAddReviewClicked { get; }
    public ReactiveCommand<Unit, Unit> BtEditReviewClicked { get; }
    
    private ReactiveCommand<Window, Unit> BtAddIncidentResponsePlanClicked { get; } 
    
    #endregion

    #region PRIVATE FIELDS
    
    private bool _initialized;
    private List<RiskStatus> _filterStatuses;
    #endregion
    
    #region EVENT HANDLERS

    private void Risk_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hydrated.Risk.Mitigation))
        {
            if (HdRisk == null) return;
            // Handle the property change here
            SelectedRiskFiles = new ObservableCollection<FileListing>(HdRisk.Files);

            if (HdRisk is { Mitigation: not null })
            {
                IsMitigationVisible = true;
                SelectedMitigationStrategy = Strategies!.Where(s => s.Value == HdRisk.Mitigation.PlanningStrategy)
                    .Select(s => s.Name).FirstOrDefault()!;
                SelectedMitigationCost = Costs!.Where(c => c.Value == HdRisk.Mitigation.MitigationCost)
                    .Select(c => c.Name)
                    .FirstOrDefault()!;
                SelectedMitigationCostId = HdRisk.Mitigation.MitigationCost;
                SelectedMitigationEffort = Efforts!.Where(ef => ef.Value == HdRisk.Mitigation.MitigationEffort)
                    .Select(c => c.Name)
                    .FirstOrDefault()!;
                SelectedMitigationEffortId = HdRisk.Mitigation.MitigationEffort;
            }else IsMitigationVisible = false;
        }

        if (e.PropertyName == nameof(Hydrated.Risk.LastReview))
        {
            if (HdRisk == null) return;
            HasReviews = HdRisk.LastReview != null;
            LastReview = HdRisk.LastReview;
            if (LastReview != null)
            {
                SelectedReviewer = UsersService.GetUserName(LastReview.Reviewer);
            }
        }

        if (e.PropertyName == nameof(Hydrated.Risk.Scoring))
        {
            float contributingScore = 0;
            var scoring = HdRisk!.Scoring;
            if (scoring.ContributingScore != null)
                contributingScore = (float) scoring.ContributingScore!.Value;

            if (contributingScore < scoring.CalculatedRisk) contributingScore = scoring.CalculatedRisk;

            TotalRiskScore =
                RiskCalculationTool.CalculateTotalRiskScore(scoring.CalculatedRisk, contributingScore);
        }

    }

    #endregion
    
    #region SERVICES

    private IIncidentResponsePlansService _incidentResponsePlansService = GetService<IIncidentResponsePlansService>();
    
    private IRisksService? _risksServiceStore;
    
    private IRisksService RisksService
    {
        get
        {
            if (_risksServiceStore == null) _risksServiceStore = GetService<IRisksService>();
            return _risksServiceStore;
        }
    }
    
    private IAuthenticationService? _autenticationServiceStore;
    
    private IAuthenticationService AutenticationService
    {
        get
        {
            if (_autenticationServiceStore == null) _autenticationServiceStore = GetService<IAuthenticationService>();
            return _autenticationServiceStore;
        }
    }
    
    private IMitigationService? _mitigationServiceStore;
    
    private IMitigationService MitigationService
    {
        get
        {
            if (_mitigationServiceStore == null) _mitigationServiceStore = GetService<IMitigationService>();
            return _mitigationServiceStore;
        }
    }
    
    private IFilesService? _filesServiceStore;
    
    private IFilesService FilesService
    {
        get
        {
            if (_filesServiceStore == null) _filesServiceStore = GetService<IFilesService>();
            return _filesServiceStore;
        }
    }
    
    
    private IUsersService? _usersServiceStore;
    
    private IUsersService UsersService
    {
        get
        {
            if (_usersServiceStore == null) _usersServiceStore = GetService<IUsersService>();
            return _usersServiceStore;
        }
    }
    
    #endregion
    
    #region CONSTRUCTOR
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
        StrImpactTypes = Localizer["ImpactTypes"] ;
        StrStatusFilter = Localizer["StatusFilter"] ;
        StrValue = Localizer["Value"] + ":";
        StrScoring = Localizer["Scoring"] + ":";
        StrProbability = Localizer["Probability"] + ":";
        StrImpact = Localizer["Impact"] + ":";
        StrMitigationNotPlanned = Localizer["MitigationNotPlannedMSG"];
        StrMitigation = Localizer["Mitigation"];
        StrUpdate = Localizer["Update"];
        StrStrategy = Localizer["Strategy"];
        StrProjected = Localizer["Projected"];
        StrCost = Localizer["Cost"];
        StrEffort = Localizer["Effort"];
        StrClosed = Localizer["Closed"].ToString().ToUpper();
        StrReason = Localizer["Reason"] + ":";
        StrFiles = Localizer["Files"] + ":";
        StrSaveDocumentMsg = Localizer["SaveDocumentMSG"];
        StrAddDocumentMsg = Localizer["AddDocumentMSG"];
        StrEntity = Localizer["Entity"] + ":";
        StrNew = Localizer["New"];
        StrMitigationPlanned = Localizer["MitigationPlanned"];
        StrManagerReviewed = Localizer["ManagerReviewed"];
        StrReviewNotDonne = Localizer["ReviewNotDone"];
        StrLastReview = Localizer["LastReview"] + ":";
        StrNext = Localizer["Next"] + ":";
        StrReviewDecision = Localizer["ReviewDecision"] + ":";
        StrNextStep = Localizer["NextStep"] + ":";
        
        
        BtAddMitigationClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteAddMitigationAsync);
        BtEditMitigationClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteEditMitigationAsync);
        BtAddRiskClicked = ReactiveCommand.Create<Window>(ExecuteAddRisk);
        BtEditRiskClicked = ReactiveCommand.Create<Window>(ExecuteEditRisk);
        BtDeleteRiskClicked = ReactiveCommand.CreateFromTask(ExecuteDeleteRisk);
        BtCloseRiskClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteCloseRiskAsync);
        BtReloadRiskClicked = ReactiveCommand.CreateFromTask(ExecuteReloadRiskAsync);
        BtNewFilterClicked = ReactiveCommand.Create(ApplyNewFilter);
        BtMitigationFilterClicked = ReactiveCommand.Create(ApplyMitigationFilter);
        BtReviewFilterClicked = ReactiveCommand.Create(ApplyReviewFilter);
        BtClosedFilterClicked = ReactiveCommand.Create(ApplyClosedFilter);
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDownloadAsync);
        BtFileDeleteClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDeleteAsync);
        BtFileAddClicked = ReactiveCommand.CreateFromTask(ExecuteFileAddAsync);
        BtAddReviewClicked = ReactiveCommand.CreateFromTask(ExecuteAddReviewAsync);
        BtEditReviewClicked = ReactiveCommand.CreateFromTask(ExecuteEditReviewAsync);
        BtAddIncidentResponsePlanClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteAddIncidentResponsePlanAsync);

        /*
        _risksService = GetService<IRisksService>();
        _autenticationService = GetService<IAuthenticationService>();
        _mitigationService = GetService<IMitigationService>();
        _filesService = GetService<IFilesService>();
        _usersService = GetService<IUsersService>();
        */

        _filterStatuses = new List<RiskStatus>()
        {
            RiskStatus.New,
            RiskStatus.ManagementReview,
            RiskStatus.MitigationPlanned
        };

        AutenticationService.AuthenticationSucceeded += (_, _) =>
        {
            _= InitializeAsync();
            
            if(AutenticationService.AuthenticatedUserInfo!.UserRole == "Admin" ||  
               AutenticationService.AuthenticatedUserInfo!.UserRole == "Administrator" || 
               AutenticationService.AuthenticatedUserInfo!.UserPermissions!.Any(p => p == "delete_risk"))
                CanDeleteRisk = true;
            
        };
        
        //ParentWindow =  WindowsManager.AllWindows.Find(w=>w is MainWindow);
        
    }
    #endregion

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
            LoadRisksAsync();
            ApplyFilter();
        }
        else
        {
            ClosedFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.Closed);
            LoadRisksAsync(true);
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var regex = new Regex(@"\s*id\s*=\s*(?<id>\d*)\s*", RegexOptions.IgnoreCase);
        var match = regex.Match(_riskFilter);

        int? id = null;
        if (match.Success)
        {
            Log.Debug("regex filter found");
            
            var idStr = match.Groups["id"].Value;
            if (int.TryParse(idStr, out var idInt))
            {
                id = idInt;
            }
        }

        if (id != null)
        {
            var cleanFilter = Regex.Replace(_riskFilter, @"id\s*=\s*\d*", "", RegexOptions.IgnoreCase);
            Risks = new ObservableCollection<Risk>(_allRisks!.Where(r => r.Id == id.Value 
                                                                         && r.Subject.Contains(cleanFilter) && _filterStatuses.Any(s => r.Status == RiskHelper.GetRiskStatusName(s))));
        }
        else
        {
            Risks = new ObservableCollection<Risk>(_allRisks!.Where(r => r.Subject.ToLower().Contains(_riskFilter.ToLower()) 
                                                                         && _filterStatuses.Any(s => r.Status == RiskHelper.GetRiskStatusName(s))));
        }
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

    private async Task ExecuteFileDeleteAsync(FileListing listing)
    {
        try
        {
            var messageBoxConfirm = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["FileDeleteConfirmationMSG"]  ,
                    ButtonDefinitions = ButtonEnum.OkAbort,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Icon = Icon.Question,
                });
                        
            var confirmation = await messageBoxConfirm.ShowAsync();

            if (confirmation == ButtonResult.Ok)
            {
                FilesService.DeleteFile(listing.UniqueName);

                if (SelectedRiskFiles == null) throw new Exception("Unexpected error deleting file");

                SelectedRiskFiles.Remove(listing);

                HdRisk!.Files.Remove(listing);
            }



        }
        catch (Exception ex)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["FileDeletionErrorMSG"] + " :" + ex.Message ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
        }
        
    }

    private async Task ExecuteFileAddAsync()
    {
        var topLevel = TopLevel.GetTopLevel(ParentWindow);
        
        var file = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = StrAddDocumentMsg,
        });

        if (file.Count == 0) return;

        if (SelectedRisk == null) return;

        var result = FilesService.UploadFile(file.First().Path, SelectedRisk.Id,
            AutenticationService.AuthenticatedUserInfo!.UserId!.Value, FileUploadType.RiskFile);

        SelectedRiskFiles ??= new ObservableCollection<FileListing>();
        SelectedRiskFiles.Add(result);

        HdRisk!.Files.Add(result);
    }

    private async Task ExecuteAddReviewAsync()
    {
        var reviewWin = new EditMgmtReview()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 1000,
            Height = 530,
            CanResize = false
        };

        if (SelectedRisk == null) return;
        var editMgmtReview =  new EditMgmtReviewViewModel(OperationType.Create, SelectedRisk.Id, reviewWin);
        editMgmtReview.MgmtReviewSaved += MgmtReviewSaved;
        reviewWin.DataContext = editMgmtReview;
        await reviewWin.ShowDialog( ParentWindow! );
    }
    
    private async Task ExecuteEditReviewAsync()
    {
        var reviewWin = new EditMgmtReview()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 1000,
            Height = 530,
            CanResize = false
        };

        if (SelectedRisk == null) return;
        var editMgmtReview = new EditMgmtReviewViewModel(OperationType.Edit, SelectedRisk.Id, reviewWin);
        editMgmtReview.MgmtReviewSaved += MgmtReviewSaved;
        reviewWin.DataContext = editMgmtReview;
        await reviewWin.ShowDialog( ParentWindow! );
    }

    private void MgmtReviewSaved(object? sender, MgmtReviewSavedEventHandlerArgs e)
    {
        LastReview = e.MgmtReview;

        if (SelectedRisk != null)
        {
            var risk = SelectedRisk;
            risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.ManagementReview);
            RisksService.SaveRisk(SelectedRisk);

            var idx = Risks!.IndexOf(risk);
            Risks[idx] = risk;
            SelectedRisk = risk;
            
            this.RaisePropertyChanged(nameof(SelectedRisk));
            this.RaisePropertyChanged(nameof(Risks));
        }

    }

    private async Task ExecuteFileDownloadAsync(FileListing listing)
    {

        var topLevel = TopLevel.GetTopLevel(ParentWindow);
        
        var file = await topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = StrSaveDocumentMsg,
            DefaultExtension = FilesService.ConvertTypeToExtension(listing.Type),
            SuggestedFileName = listing.Name + FilesService.ConvertTypeToExtension(listing.Type),
            
        });

        if (file == null) return;
            
        FilesService.DownloadFile(listing.UniqueName, file.Path);
        
    }

    private async Task ExecuteAddMitigationAsync(Window openWindow)
    {
        var dialog = new EditMitigationWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 1050,
            Height = 530,
            CanResize = false
        };
        dialog.DataContext = new EditMitigationViewModel(OperationType.Create, SelectedRisk!.Id, dialog);
        await dialog.ShowDialog( openWindow );
        var selectedRiskId = SelectedRisk.Id;
        _= ExecuteReloadRiskAsync();
        CleanFilters();
        SelectedRisk = Risks!.FirstOrDefault(r=>r.Id == selectedRiskId);
    }
    
    private async Task ExecuteAddIncidentResponsePlanAsync(Window openWindow)
    {
        var addIrpDc = new IncidentResponsePlanViewModel();
        var addIrp = new IncidentResponsePlanWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 1000,
            Height = 530,
            CanResize = true,
            DataContext = addIrpDc
        };
        
        await addIrp.ShowDialog( openWindow );
    }
    
    private async Task ExecuteCloseRiskAsync(Window openWindow)
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
        await ExecuteReloadRiskAsync();
        CleanFilters();
    }
    
    private async Task ExecuteEditMitigationAsync(Window openWindow)
    {
        var dialog = new EditMitigationWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 1050,
            Height = 530,
            CanResize = false
        };
        dialog.DataContext =
            new EditMitigationViewModel(OperationType.Edit, SelectedRisk!.Id, dialog, HdRisk!.Mitigation);
        await dialog.ShowDialog( openWindow );
        var selectedRiskId = SelectedRisk.Id;
        await ExecuteReloadRiskAsync();
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
            Height = 750,
        };
        await dialog.ShowDialog( openWindow );
        AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync());
    }
    private async void ExecuteEditRisk(Window openWindow)
    {
        if (SelectedRisk == null)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["SelectRiskMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        
        // OPENS a new window to edit the risk

        var dialog = new EditRiskWindow()
        {
            DataContext = new EditRiskViewModel(OperationType.Edit, SelectedRisk),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1000,
            Height = 750,
        };
        await dialog.ShowDialog( openWindow );
        AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync());
    }
    private async Task ExecuteDeleteRisk()
    {
        if (SelectedRisk == null)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["SelectRiskDeleteMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        var messageBoxConfirm = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["RiskDeleteConfirmationMSG"]  ,
                ButtonDefinitions = ButtonEnum.OkAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.ShowAsync();

        if (confirmation == ButtonResult.Ok)
        {
            try
            {
                RisksService.DeleteRiskScoring(SelectedRisk.Id);
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting risk score with id:{Id} details: {Details}", SelectedRisk.Id, ex.Message);
            }
            
            try
            {
                RisksService.DeleteRisk(SelectedRisk);
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting risk  with id:{Id} details: {Details}", SelectedRisk.Id, ex.Message);
            }
            
            AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync());
        }
    }
    
    private async Task LoadRisksAsync(bool includeClosed = false)
    {
        AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync(includeClosed));
    }
    
    private async Task ExecuteReloadRiskAsync()
    {
        if (_filterStatuses.Any(s => s == RiskStatus.Closed)) await LoadRisksAsync(true);
        else await LoadRisksAsync();
        RiskFilter = "";
    }

    private async Task InitializeAsync()
    {
        if (!_initialized)
        {
            AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync());
            
            Strategies = MitigationService.GetStrategies();
            Costs = MitigationService.GetCosts();
            Efforts = MitigationService.GetEfforts();
            
            _initialized = true;
        }
    }
    #endregion
}