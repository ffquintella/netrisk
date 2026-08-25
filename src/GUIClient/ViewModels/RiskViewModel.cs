using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs;
using GUIClient.Tools;
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
using GUIClient.Models;
using GUIClient.Models.Events;
using Model.DTO;
using Model.File;
using Model.Risks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Serilog;
using Tools.Risks;
using Tools.Security;

namespace GUIClient.ViewModels;

public class RiskViewModel: ViewModelBase
{

    #region CONSTS
    
        private const int _irpWindowWidth = 1000;
        private const int _irpWindowHeight = 900;
        
    #endregion
    
    #region LANGUAGE-STRINGS
    public string StrRisk { get; }
    public string StrDetails { get; }
    public string StrSubject { get; }
    public string StrCtrlNumber { get; }
    public string StrDate { get; }
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
    public string StrReopen { get; }
    public string StrPlanMitigation { get; }
    public string StrReviseMitigation { get; }
    public string StrAddReview { get; }
    public string StrLifecycle { get; }
    public string StrCloseRisk { get; }
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

    private int _selectedVulnerabilityPage = 1;
    
    public int SelectedVulnerabilityPage
    {
        get => _selectedVulnerabilityPage;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityPage, value);
    }

    private int _totalSelectedVulnerabilities = 0;
    public int TotalSelectedVulnerabilities
    {
        get => _totalSelectedVulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _totalSelectedVulnerabilities, value);
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
                    
                    //SelectedVulnerabilities = new ObservableCollection<Vulnerability>(await RisksService.GetOpenVulnerabilitiesAsync(value.Id));

                    var pageTuple = await RisksService.GetOpenVulnerabilitiesPageAsync(value.Id, 1, 10);
                    
                    SelectedVulnerabilities = new ObservableCollection<Vulnerability>(pageTuple.Item2);
                    TotalSelectedVulnerabilities = pageTuple.Item1;
                    
                    SelectedRiskId = value.Id;
                    SelectedRiskCtrlNumber = value.ControlNumber;
                    SelectedRiskStatus = value.Status;
                    SelectedRiskSubmissionDate = value.SubmissionDate;
                    SelectedVulnerabilityPage = 1;

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

                ProcessLifecycleButtons();
                
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
            if (value == null) return;
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

    #region LIFECYCLE TOOLBAR

    // IX-6: every stage action of the risk lifecycle is visible on the module view's toolbar and
    // enabled per the risk's current state, modelled on the vulnerability triage toolbar. These
    // used to be 22px icon buttons buried in the scrolling detail pane, with Reopen dead.

    private bool _btPlanMitigationEnabled;
    public bool BtPlanMitigationEnabled
    {
        get => _btPlanMitigationEnabled;
        set => this.RaiseAndSetIfChanged(ref _btPlanMitigationEnabled, value);
    }

    private bool _btReviseMitigationEnabled;
    public bool BtReviseMitigationEnabled
    {
        get => _btReviseMitigationEnabled;
        set => this.RaiseAndSetIfChanged(ref _btReviseMitigationEnabled, value);
    }

    private bool _btAddReviewEnabled;
    public bool BtAddReviewEnabled
    {
        get => _btAddReviewEnabled;
        set => this.RaiseAndSetIfChanged(ref _btAddReviewEnabled, value);
    }

    private bool _btEditReviewEnabled;
    public bool BtEditReviewEnabled
    {
        get => _btEditReviewEnabled;
        set => this.RaiseAndSetIfChanged(ref _btEditReviewEnabled, value);
    }

    private bool _btCloseRiskEnabled;
    public bool BtCloseRiskEnabled
    {
        get => _btCloseRiskEnabled;
        set => this.RaiseAndSetIfChanged(ref _btCloseRiskEnabled, value);
    }

    private bool _btReopenEnabled;
    public bool BtReopenEnabled
    {
        get => _btReopenEnabled;
        set => this.RaiseAndSetIfChanged(ref _btReopenEnabled, value);
    }

    /// <summary>
    /// Recomputes which lifecycle stage actions are available for the selected risk. Called
    /// whenever the selection, its mitigation or its review changes, so the toolbar always
    /// reflects the current state rather than the state at selection time.
    /// </summary>
    private void ProcessLifecycleButtons()
    {
        var risk = SelectedRisk;
        var status = risk == null ? null : RiskHelper.GetRiskStatusFromName(risk.Status);
        var isClosed = status == RiskStatus.Closed;

        // A closed risk has exactly one exit: reopen it.
        BtReopenEnabled = risk != null && isClosed;

        if (risk == null || isClosed)
        {
            BtPlanMitigationEnabled = false;
            BtReviseMitigationEnabled = false;
            BtAddReviewEnabled = false;
            BtEditReviewEnabled = false;
            BtCloseRiskEnabled = false;
            return;
        }

        BtPlanMitigationEnabled = !IsMitigationVisible;
        BtReviseMitigationEnabled = IsMitigationVisible;
        BtAddReviewEnabled = !HasReviews;
        BtEditReviewEnabled = HasReviews;

        // Closing is a permissioned action, and only meaningful for an open risk.
        BtCloseRiskEnabled = CanDeleteRisk;
    }

    #endregion
    
    private bool _userHasPermissionToAccessIncidentResponsePlans;
    
    public bool UserHasPermissionToAccessIncidentResponsePlans
    {
        get => _userHasPermissionToAccessIncidentResponsePlans;
        set => this.RaiseAndSetIfChanged(ref _userHasPermissionToAccessIncidentResponsePlans, value);
    }

    private bool _userHasPermissionToDeleteIncidentResponsePlans;
    
    public bool UserHasPermissionToDeleteIncidentResponsePlans
    {
        get => _userHasPermissionToDeleteIncidentResponsePlans;
        set => this.RaiseAndSetIfChanged(ref _userHasPermissionToDeleteIncidentResponsePlans, value);
    }
    
    private bool _filterVisible;
    
    public bool FilterVisible
    {
        get => _filterVisible;
        set => this.RaiseAndSetIfChanged(ref _filterVisible, value);
    }

    private List<PlanningStrategy>? Strategies { get; set; }

    private List<MitigationCost>? Costs { get; set; }

    private List<MitigationEffort>? Efforts { get; set; }

    #endregion
    
    #region BUTTONS
    public ReactiveCommand<RxVoid, RxVoid> BtAddMitigationClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEditMitigationClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEditRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReloadRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCloseRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReopenRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewFilterClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtMitigationFilterClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReviewFilterClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtClosedFilterClicked { get; }
    public ReactiveCommand<FileListing, RxVoid> BtFileDownloadClicked { get; }
    public ReactiveCommand<FileListing, RxVoid> BtFileDeleteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtFileAddClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddReviewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEditReviewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddIncidentResponsePlanClicked { get; } 
    public ReactiveCommand<RxVoid, RxVoid> BtViewIncidentResponsePlanClicked { get; } 
    public ReactiveCommand<RxVoid, RxVoid> BtEditIncidentResponsePlanClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteIncidentResponsePlanClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtFilterViewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> PrevPage { get; }
    public ReactiveCommand<RxVoid, RxVoid> NextPage { get; }
    
    
    #endregion

    #region PRIVATE FIELDS
    
    private bool _initialized;
    private List<RiskStatus> _filterStatuses;
    #endregion
    
    #region EVENT HANDLERS

    private void Risk_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (HdRisk == null)
        {
            Log.Error("HdRisk should not be null here: NR001");
            return;
        }
        
        if (e.PropertyName == nameof(Hydrated.Risk.Files))
        {
            SelectedRiskFiles = new ObservableCollection<FileListing>(HdRisk.Files);
        }
        
        if (e.PropertyName == nameof(Hydrated.Risk.Mitigation))
        {
            if (HdRisk == null) return;
            // Handle the property change here
            

            if (HdRisk is { Mitigation: not null })
            {
                IsMitigationVisible = true;
                SelectedMitigationStrategy = Strategies!.Where(s => s.Value == HdRisk.Mitigation.PlanningStrategy)
                    .Select(s => s.Name).FirstOrDefault()!;
                SelectedMitigationCost = Costs!.Where(c => c.Value == HdRisk.Mitigation.MitigationCost)
                    .Select(c => c.Name)
                    .FirstOrDefault()!;
                SelectedMitigationCostId = HdRisk.Mitigation.MitigationCost;
                if (Efforts != null)
                    SelectedMitigationEffort = Efforts!.Where(ef => ef.Value == HdRisk.Mitigation.MitigationEffort)
                        .Select(c => c.Name)
                        .FirstOrDefault()!;
                SelectedMitigationEffortId = HdRisk.Mitigation.MitigationEffort;
            }else IsMitigationVisible = false;

            ProcessLifecycleButtons();
        }

        if (e.PropertyName == nameof(Hydrated.Risk.LastReview))
        {
            if (HdRisk == null) return;
            HasReviews = HdRisk.LastReview != null;
            ProcessLifecycleButtons();
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
    private IDialogService DialogService { get; } = GetService<IDialogService>();

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
        StrCtrlNumber = Localizer["CtrlNumber"] + ": ";
        StrDate = Localizer["Date"] + ". ";
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
        StrReopen = Localizer["Reopen"];
        StrPlanMitigation = Localizer["PlanMitigation"];
        StrReviseMitigation = Localizer["ReviseMitigation"];
        StrAddReview = Localizer["AddReview"];
        StrLifecycle = Localizer["RiskLifecycle"];
        StrCloseRisk = Localizer["CloseRisk"];
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
        
        
        BtAddMitigationClicked = ReactiveCommand.CreateFromTask(ExecuteAddMitigationAsync);
        BtEditMitigationClicked = ReactiveCommand.CreateFromTask(ExecuteEditMitigationAsync);
        BtAddRiskClicked = ReactiveCommand.CreateFromTask(ExecuteAddRiskAsync);
        BtEditRiskClicked = ReactiveCommand.CreateFromTask(ExecuteEditRiskAsync);
        BtDeleteRiskClicked = ReactiveCommand.CreateFromTask(ExecuteDeleteRisk);
        BtCloseRiskClicked = ReactiveCommand.CreateFromTask(ExecuteCloseRiskAsync);
        BtReopenRiskClicked = ReactiveCommand.CreateFromTask(ExecuteReopenRiskAsync);
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
        BtAddIncidentResponsePlanClicked = ReactiveCommand.CreateFromTask(ExecuteAddIncidentResponsePlanAsync);
        BtViewIncidentResponsePlanClicked = ReactiveCommand.CreateFromTask(ExecuteViewIncidentResponsePlanAsync);
        BtEditIncidentResponsePlanClicked = ReactiveCommand.CreateFromTask(ExecuteEditIncidentResponsePlanAsync);
        BtDeleteIncidentResponsePlanClicked = ReactiveCommand.CreateFromTask(ExecuteDeleteIncidentResponsePlanAsync);
        BtFilterViewClicked = ReactiveCommand.Create(ExecuteShowFilter);
        PrevPage = ReactiveCommand.CreateFromTask(ExecutePrevPage);
        NextPage = ReactiveCommand.CreateFromTask(ExecuteNextPage);
            
        _filterStatuses = new List<RiskStatus>()
        {
            RiskStatus.New,
            RiskStatus.ManagementReview,
            RiskStatus.MitigationPlanned
        };

        AutenticationService.AuthenticationSucceeded += (_, _) =>
        {
            if(AutenticationService.AuthenticatedUserInfo == null) return;

            if(AutenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("riskmanagement"))
                _= InitializeAsync();
            
            CanDeleteRisk = PermissionTool.VerifyPermission("delete_risk", AutenticationService.AuthenticatedUserInfo);
            ProcessLifecycleButtons();

            UserHasPermissionToAccessIncidentResponsePlans = PermissionTool.VerifyPermission("incident-response-plans",
                AutenticationService.AuthenticatedUserInfo);
            
            UserHasPermissionToDeleteIncidentResponsePlans = PermissionTool.VerifyPermission("irp-delete", AutenticationService.AuthenticatedUserInfo);

        };
        
    }
    #endregion

    #region METHODS

    private async Task ExecutePrevPage()
    {
        if(SelectedRisk == null) return;

        if (SelectedVulnerabilityPage > 1)
        {
            SelectedVulnerabilityPage--;
            var pageTuple = await RisksService.GetOpenVulnerabilitiesPageAsync(SelectedRisk.Id, SelectedVulnerabilityPage, 10);
                    
            SelectedVulnerabilities = new ObservableCollection<Vulnerability>(pageTuple.Item2);
            TotalSelectedVulnerabilities = pageTuple.Item1;
        }
    }

    private async Task ExecuteNextPage()
    {
        if(SelectedRisk == null) return;

        if (SelectedVulnerabilityPage * 10 <= TotalSelectedVulnerabilities)
        {
            SelectedVulnerabilityPage++;
        
            var pageTuple = await RisksService.GetOpenVulnerabilitiesPageAsync(SelectedRisk.Id, SelectedVulnerabilityPage, 10);
                    
            SelectedVulnerabilities = new ObservableCollection<Vulnerability>(pageTuple.Item2);
            TotalSelectedVulnerabilities = pageTuple.Item1;
        }

    }
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
            _= LoadRisksAsync();
            ApplyFilter();
        }
        else
        {
            ClosedFilterColor = Brushes.DodgerBlue;
            _filterStatuses.Add(RiskStatus.Closed);
            _= LoadRisksAsync(true);
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
            if (await ConfirmationDialog.ConfirmDeleteAsync(listing.Name))
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
        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;
        
        var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = StrAddDocumentMsg,
        });

        if (file.Count == 0) return;

        if (SelectedRisk == null) return;

        try
        {
            var result = await FilesService.UploadFileAsync(file.First().Path, SelectedRisk.Id,
                AutenticationService.AuthenticatedUserInfo!.UserId!.Value, FileCollectionType.RiskFile);

            SelectedRiskFiles ??= new ObservableCollection<FileListing>();
            SelectedRiskFiles.Add(result);

            HdRisk!.Files.Add(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading file to risk {Id}", SelectedRisk.Id);

            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorUploadingFileMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }

    private Task ExecuteAddReviewAsync() => ShowMgmtReviewDialogAsync(OperationType.Create);

    private Task ExecuteEditReviewAsync() => ShowMgmtReviewDialogAsync(OperationType.Edit);

    private async Task ShowMgmtReviewDialogAsync(OperationType operation)
    {
        if (SelectedRisk == null) return;

        var result = await DialogService
            .ShowDialogAsync<MgmtReviewDialogResult, MgmtReviewDialogParameter>(
                nameof(EditMgmtReviewViewModel),
                new MgmtReviewDialogParameter { Operation = operation, RiskId = SelectedRisk.Id });

        if (result?.Action != ResultActions.Ok || result.SavedReview == null) return;

        ApplyMgmtReviewSaved(result.SavedReview);

        await OfferNextStepAsync(result);
    }

    /// <summary>
    /// IX-2: the caller updates its own collection in place from the dialog's typed result,
    /// rather than the dialog reaching into the parent through an event.
    /// </summary>
    private void ApplyMgmtReviewSaved(MgmtReview review)
    {
        LastReview = review;

        if (SelectedRisk == null) return;

        var risk = SelectedRisk;
        risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.ManagementReview);
        RisksService.SaveRisk(risk);

        var idx = Risks!.IndexOf(risk);
        if (idx >= 0) Risks[idx] = risk;
        SelectedRisk = risk;

        this.RaisePropertyChanged(nameof(SelectedRisk));
        this.RaisePropertyChanged(nameof(Risks));
    }

    /// <summary>
    /// IX-6 next-step affordance: the review's chosen next step used to be captured as data and
    /// then ignored. Now the stage that just committed offers the stage it points at.
    /// </summary>
    private async Task OfferNextStepAsync(MgmtReviewDialogResult result)
    {
        var nextStep = result.NextStep;
        if (nextStep == null || string.IsNullOrWhiteSpace(result.NextStepName)) return;

        var followUp = NextStepFollowUp(nextStep.Value);
        if (followUp == null) return;

        var accepted = await ConfirmationDialog.ConfirmAsync(
            Localizer["NextStep"],
            string.Format(Localizer["NextStepPromptMSG"], result.NextStepName));

        if (!accepted) return;

        await followUp();
    }

    /// <summary>
    /// Maps a review's next-step value to the stage that carries it out, or <c>null</c> when the
    /// next step needs no immediate action in the app.
    /// </summary>
    private Func<Task>? NextStepFollowUp(int nextStep) =>
        RiskHelper.GetNextStepAction(nextStep) switch
        {
            RiskNextStepAction.PlanMitigation => ExecuteAddMitigationAsync,
            RiskNextStepAction.ReviseMitigation => ExecuteEditMitigationAsync,
            RiskNextStepAction.CloseRisk => ExecuteCloseRiskAsync,
            _ => null
        };

    private async Task ExecuteFileDownloadAsync(FileListing listing)
    {

        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;

        if (listing.Type == null)
        {
            Log.Error("File listing type is null: NR003");
            throw new NullReferenceException("File listing type is null");
        }
        
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = StrSaveDocumentMsg,
            DefaultExtension = FilesService.ConvertTypeToExtension(listing.Type),
            SuggestedFileName = listing.Name + FilesService.ConvertTypeToExtension(listing.Type),
            
        });

        if (file == null) return;
            
        _= FilesService.DownloadFileAsync(listing.UniqueName, file.Path);
        
    }

    private Task ExecuteAddMitigationAsync() =>
        ShowMitigationDialogAsync(OperationType.Create);
    
    private Task ExecuteAddIncidentResponsePlanAsync() =>
        ShowIncidentResponsePlanDialogAsync(OperationType.Create);

    private Task ExecuteViewIncidentResponsePlanAsync() =>
        ShowIncidentResponsePlanDialogAsync(OperationType.View);

    private Task ExecuteEditIncidentResponsePlanAsync() =>
        ShowIncidentResponsePlanDialogAsync(OperationType.Edit);

    /// <summary>
    /// IX-1: the plan window is now a modal dialog opened through <see cref="IDialogService"/>,
    /// sized by its own XAML. It used to open modeless while its task children were modal.
    /// </summary>
    private async Task ShowIncidentResponsePlanDialogAsync(OperationType operation)
    {
        if (SelectedRisk == null) return;

        if (operation == OperationType.Create && SelectedRiskIncidentResponsePlan != null) return;

        if (operation != OperationType.Create && SelectedRiskIncidentResponsePlan == null)
        {
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["OperationNotValidForThisRiskMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
            return;
        }

        var result = await DialogService
            .ShowDialogAsync<IrpDialogResult, IrpDialogParameter>(
                nameof(IncidentResponsePlanViewModel),
                new IrpDialogParameter
                {
                    Operation = operation,
                    RelatedRisk = SelectedRisk,
                    Plan = operation == OperationType.Create ? null : SelectedRiskIncidentResponsePlan
                });

        if (result?.Action != ResultActions.Ok) return;

        var selectedRiskId = SelectedRisk.Id;
        await ExecuteReloadRiskAsync();
        SelectedRisk = Risks!.FirstOrDefault(r => r.Id == selectedRiskId);
    }

    private async Task ExecuteDeleteIncidentResponsePlanAsync()
    {
        if (await ConfirmationDialog.ConfirmDeleteAsync(SelectedRiskIncidentResponsePlan?.Name,
                Localizer["IRPDeleteConfirmationMSG"]))
        {
            try
            {
                _= _incidentResponsePlansService.DeleteAsync(SelectedRiskIncidentResponsePlan!.Id);
                SelectedRiskIncidentResponsePlan = null;
                SelectedRiskHasIncidentResponsePlan = false;
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting IRP with id:{Id} details: {Details}", SelectedRiskIncidentResponsePlan!.Id, ex.Message);
            }
        }
    }

    private async Task ExecuteCloseRiskAsync()
    {
        if (SelectedRisk == null) return;

        var result = await DialogService
            .ShowDialogAsync<CloseRiskDialogResult, CloseRiskDialogParameter>(
                nameof(CloseRiskViewModel),
                new CloseRiskDialogParameter { Risk = SelectedRisk });

        if (result?.Action != ResultActions.Ok) return;

        await ExecuteReloadRiskAsync();
        CleanFilters();
    }
    
    private Task ExecuteEditMitigationAsync() =>
        ShowMitigationDialogAsync(OperationType.Edit);

    private async Task ShowMitigationDialogAsync(OperationType operation)
    {
        if (SelectedRisk == null) return;

        var result = await DialogService
            .ShowDialogAsync<MitigationDialogResult, MitigationDialogParameter>(
                nameof(EditMitigationViewModel),
                new MitigationDialogParameter
                {
                    Operation = operation,
                    RiskId = SelectedRisk.Id,
                    Mitigation = operation == OperationType.Edit ? HdRisk?.Mitigation : null
                });

        if (result?.Action != ResultActions.Ok) return;

        var selectedRiskId = SelectedRisk.Id;
        await ExecuteReloadRiskAsync();
        CleanFilters();
        SelectedRisk = Risks!.FirstOrDefault(r => r.Id == selectedRiskId);
    }

    private async Task ExecuteReopenRiskAsync()
    {
        if (SelectedRisk == null) return;

        var confirm = await MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = string.Format(Localizer["ReopenRiskConfirmMSG"], SelectedRisk.Subject),
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNo,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            })
            .ShowAsync();

        if (confirm != ButtonResult.Yes) return;

        var reopenedId = SelectedRisk.Id;

        try
        {
            await RisksService.ReopenRiskAsync(reopenedId);
        }
        catch (Exception ex)
        {
            Logger.Error("Error reopening risk {Id}: {Message}", reopenedId, ex.Message);

            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorReopeningRiskMSG"] + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
            return;
        }

        await ExecuteReloadRiskAsync();
        CleanFilters();
        SelectedRisk = Risks!.FirstOrDefault(r => r.Id == reopenedId);
    }

    private Task ExecuteAddRiskAsync() => ShowRiskDialogAsync(OperationType.Create);
    private async Task ExecuteEditRiskAsync()
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
        
        await ShowRiskDialogAsync(OperationType.Edit, SelectedRisk);
    }

    private async Task ShowRiskDialogAsync(OperationType operation, Risk? risk = null)
    {
        var result = await DialogService
            .ShowDialogAsync<RiskDialogResult, RiskDialogParameter>(
                nameof(EditRiskViewModel),
                new RiskDialogParameter { Operation = operation, Risk = risk });

        if (result?.Action != ResultActions.Ok) return;

        AllRisks = new ObservableCollection<Risk>(await RisksService.GetAllRisksAsync());

        // IX-6 next-step affordance: a freshly created risk's next stage is planning its mitigation.
        if (operation != OperationType.Create || result.SavedRisk == null) return;

        SelectedRisk = Risks?.FirstOrDefault(r => r.Id == result.SavedRisk.Id);
        if (SelectedRisk == null) return;

        if (await ConfirmationDialog.ConfirmAsync(
                Localizer["Mitigation"], Localizer["PlanMitigationNowMSG"]))
        {
            await ShowMitigationDialogAsync(OperationType.Create);
        }
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
        if (await ConfirmationDialog.ConfirmDeleteAsync(SelectedRisk.Subject,
                Localizer["RiskDeleteConfirmationMSG"]))
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
            
            Strategies = await MitigationService.GetStrategiesAsync();
            Costs = await MitigationService.GetCostsAsync();
            Efforts = await MitigationService.GetEffortsAsync();
            
            _initialized = true;
        }
    }

    private void ExecuteShowFilter()
    {
        FilterVisible = !FilterVisible;
    }
    #endregion
}